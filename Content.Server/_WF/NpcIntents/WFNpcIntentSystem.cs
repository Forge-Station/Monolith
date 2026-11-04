using System.Numerics;
using Content.Server._WF.CavernEncounters;
using Content.Server._WF.NpcCrew.Components;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server.Chat.Systems;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.NpcIntents;
using Content.Shared._WF.Wolfmed.Consciousness;
using Content.Shared.Chat;
using Content.Shared.CombatMode;
using Content.Shared.Damage;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.Physics;
using Content.Shared.Verbs;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.NpcIntents;

/// <summary>
/// Decides how an NPC's fight with a player ends. It rolls an endpoint from the NPC's set when it picks a target, and
/// stops the NPC attacking a downed target (by putting them on its ignore list), takes what the endpoint calls for and
/// walks off. Whoever it has finished with is left alone for the set's cooldown, unless they hit it.
/// </summary>
public sealed partial class WFNpcIntentSystem : EntitySystem
{
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private HTNSystem _htn = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private NPCSystem _npc = default!;
    [Dependency] private NpcFactionSystem _factions = default!;
    [Dependency] private RotateToFaceSystem _rotate = default!;
    [Dependency] private SharedCombatModeSystem _combat = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WFCavernWalkerSystem _walker = default!;
    [Dependency] private WFCrewSystem _crew = default!;
    [Dependency] private WoundDamageRoutingSystem _wounds = default!;

    /// <summary>The blackboard key that makes a brain with the hold branch stand and aim.</summary>
    public const string HoldKey = "WFIntentHold";

    /// <summary>How often the engagements are checked.</summary>
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(0.25);

    /// <summary>How long a fight may go on without its target in sight before it counts as lost.</summary>
    private static readonly TimeSpan LostAfter = TimeSpan.FromSeconds(15);

    /// <summary>A gang forms from NPCs of the same set this close to the one that picks a target.</summary>
    private const float GangRange = 12f;

    /// <summary>A fight further apart than this is lost at once.</summary>
    private const float LostRange = 40f;

    /// <summary>A boarder counts a blow to one of its crew for this long.</summary>
    private static readonly TimeSpan GrudgeTime = TimeSpan.FromSeconds(90);

    private TimeSpan _next;

    /// <summary>Who has hit a boarding party, by crew group, until when.</summary>
    private readonly Dictionary<string, Dictionary<EntityUid, TimeSpan>> _grudges = new();

    private readonly List<EntityUid> _scratch = new();
    private readonly HashSet<Entity<WFNpcIntentComponent>> _gang = new();

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();

        // Before the wound system cancels the hit and loses who threw it.
        SubscribeLocalEvent<WFNpcIntentComponent, BeforeDamageChangedEvent>(OnBeforeDamage,
            before: new[] { typeof(WoundDamageRoutingSystem) });
        SubscribeLocalEvent<WFNpcIntentComponent, AttackAttemptEvent>(OnAttackAttempt);
        SubscribeLocalEvent<WFNpcIntentComponent, ShotAttemptedEvent>(OnShotAttempted);
        SubscribeLocalEvent<WFNpcIntentComponent, MobStateChangedEvent>(OnMobState);
        SubscribeLocalEvent<WFNpcIntentComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<WFNpcIntentComponent, GetVerbsEvent<InteractionVerb>>(OnVerbs);
    }

    /// <summary>Whether a body is down: not alive, or not on its feet in the medical sense.</summary>
    public bool IsDowned(EntityUid body)
    {
        if (TerminatingOrDeleted(body))
            return true;

        if (TryComp(body, out MobStateComponent? mob) && mob.CurrentState != MobState.Alive)
            return true;

        return TryComp(body, out WolfmedConsciousnessComponent? consciousness)
               && consciousness.State != WolfmedConsciousness.Up;
    }

    /// <summary>Whether an NPC is currently leaving a target alone.</summary>
    public bool InCooldown(EntityUid npc, EntityUid target)
    {
        return TryComp(npc, out WFNpcIntentComponent? comp)
               && comp.Cooldowns.TryGetValue(target, out var until) && _timing.CurTime < until;
    }

    /// <summary>Takes a target off those an NPC has finished with, because it has been forced to fight them again.</summary>
    public void Provoked(EntityUid npc, EntityUid by)
    {
        if (TryComp(npc, out WFNpcIntentComponent? comp) && comp.Cooldowns.Remove(by))
            _factions.UnignoreEntity(npc, by);
    }

    /// <summary>
    /// Whether a crewman keeps its fire from a target: one it has finished with, or, for a boarder aboard a ship that
    /// is not its own, anyone who has not hit its party.
    /// </summary>
    public bool HoldsFireOn(EntityUid npc, EntityUid target)
    {
        if (!TryComp(npc, out WFNpcIntentComponent? comp))
            return false;

        if (comp.Cooldowns.ContainsKey(target))
            return true;

        return comp.Boarder && IsBoarding(npc) && !HasGrudge(npc, target);
    }

    private bool IsBoarding(EntityUid npc)
    {
        if (!TryComp(npc, out WFCrewComponent? crew) || Transform(npc).GridUid is not { } grid)
            return false;

        return crew.Post is { } post && post.EntityId != grid;
    }

    private bool HasGrudge(EntityUid npc, EntityUid target)
    {
        if (!TryComp(npc, out WFCrewComponent? crew) || !_grudges.TryGetValue(crew.Group, out var group))
            return false;

        return group.TryGetValue(target, out var until) && _timing.CurTime < until;
    }

    private void OnBeforeDamage(Entity<WFNpcIntentComponent> ent, ref BeforeDamageChangedEvent args)
    {
        // The wound system's routed pass raises this again for the same hit.
        if (args.Cancelled || _wounds.IsRouting(ent) || !args.Damage.AnyPositive() || !_mobState.IsAlive(ent))
            return;

        if (args.Origin is not { } source || source == ent.Owner || TerminatingOrDeleted(source))
            return;

        var attacker = _crew.Wielder(source);
        if (ent.Comp.Boarder && TryComp(ent, out WFCrewComponent? crew))
        {
            if (!_grudges.TryGetValue(crew.Group, out var group))
                _grudges[crew.Group] = group = new Dictionary<EntityUid, TimeSpan>();
            group[attacker] = _timing.CurTime + GrudgeTime;
        }

        // Hitting one it has finished with, or one it holds up, starts the fight.
        if (ent.Comp.Cooldowns.Remove(attacker))
            _factions.UnignoreEntity(ent.Owner, attacker);

        if (ent.Comp.Phase is WFNpcIntentPhase.Demanding or WFNpcIntentPhase.Warning && ent.Comp.Target == attacker)
            Refuse(ent);
    }

    private void OnAttackAttempt(Entity<WFNpcIntentComponent> ent, ref AttackAttemptEvent args)
    {
        if (args.Target is { } target && Spares(ent.Comp) && IsDowned(target))
            args.Cancel();
    }

    private void OnShotAttempted(Entity<WFNpcIntentComponent> ent, ref ShotAttemptedEvent args)
    {
        if (args.Used.Comp.Target is { } target && Spares(ent.Comp) && IsDowned(target))
            args.Cancel();
    }

    /// <summary>Whether this NPC never finishes off a downed target.</summary>
    private static bool Spares(WFNpcIntentComponent comp) => comp.Active != WFNpcIntentKind.Lethal;

    private void OnMobState(Entity<WFNpcIntentComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Alive)
            Reset(ent);
    }

    /// <inheritdoc/>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        if (now < _next)
            return;

        _next = now + Interval;

        var query = EntityQueryEnumerator<WFNpcIntentComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var comp, out var xform))
        {
            if (!_mobState.IsAlive(uid))
                continue;

            ExpireCooldowns((uid, comp), now);
            Tick((uid, comp), xform, now);
        }

        if (_grudges.Count > 0)
            ExpireGrudges(now);
    }

    private void ExpireCooldowns(Entity<WFNpcIntentComponent> ent, TimeSpan now)
    {
        if (ent.Comp.Cooldowns.Count == 0)
            return;

        _scratch.Clear();
        foreach (var (target, until) in ent.Comp.Cooldowns)
        {
            if (now >= until || TerminatingOrDeleted(target))
                _scratch.Add(target);
        }

        foreach (var target in _scratch)
        {
            ent.Comp.Cooldowns.Remove(target);
            _factions.UnignoreEntity(ent.Owner, target);
        }
    }

    private void ExpireGrudges(TimeSpan now)
    {
        foreach (var group in _grudges.Values)
        {
            _scratch.Clear();
            foreach (var (target, until) in group)
            {
                if (now >= until || TerminatingOrDeleted(target))
                    _scratch.Add(target);
            }

            foreach (var target in _scratch)
            {
                group.Remove(target);
            }
        }
    }

    private void Tick(Entity<WFNpcIntentComponent> ent, TransformComponent xform, TimeSpan now)
    {
        var comp = ent.Comp;
        if (comp.Phase != WFNpcIntentPhase.None && comp.Phase != WFNpcIntentPhase.Leaving
            && (comp.Target is not { } held || TerminatingOrDeleted(held)))
        {
            Reset(ent);
            return;
        }

        switch (comp.Phase)
        {
            case WFNpcIntentPhase.None:
                Look(ent, xform);
                break;
            case WFNpcIntentPhase.Demanding:
                TickDemand(ent, now);
                break;
            case WFNpcIntentPhase.Warning:
                TickWarning(ent, now);
                break;
            case WFNpcIntentPhase.Fighting:
                TickFight(ent, xform, now);
                break;
            case WFNpcIntentPhase.Taking:
                TickTaking(ent, now);
                break;
            case WFNpcIntentPhase.Leaving:
                if (now >= comp.PhaseEnd)
                    Reset(ent);
                break;
        }
    }

    /// <summary>Picks up a new engagement: from whom the brain is fighting, or, for a holder, whom it can see.</summary>
    private void Look(Entity<WFNpcIntentComponent> ent, TransformComponent xform)
    {
        var comp = ent.Comp;
        EntityUid? target = null;

        if (TryComp(ent, out NPCMeleeCombatComponent? melee))
            target = melee.Target;
        else if (TryComp(ent, out NPCRangedCombatComponent? ranged))
            target = ranged.Target;

        if (target is { } fighting && !Candidate(ent, fighting, requireSight: false))
            target = null;

        if (target == null && (comp.Holds || WarnsOff(comp)))
            target = Spot(ent);

        if (target is not { } mark)
            return;

        var kind = Roll(comp, comp.Holds);
        Begin(ent, mark, kind);
        Recruit(ent, mark, kind);
    }

    private bool WarnsOff(WFNpcIntentComponent comp)
    {
        foreach (var choice in _proto.Index(comp.Set).Intents)
        {
            if (choice.Kind == WFNpcIntentKind.WarnOff)
                return true;
        }

        return false;
    }

    /// <summary>The nearest hostile player in plain sight that it has not finished with.</summary>
    private EntityUid? Spot(Entity<WFNpcIntentComponent> ent)
    {
        EntityUid? best = null;
        var bestDistance = float.MaxValue;
        var here = _transform.GetWorldPosition(ent);
        var seeker = new Entity<NpcFactionMemberComponent?, FactionExceptionComponent?>(ent, null, null);

        foreach (var hostile in _factions.GetNearbyHostiles(seeker, ent.Comp.SightRange))
        {
            if (!Candidate(ent, hostile, requireSight: true))
                continue;

            var distance = Vector2.Distance(here, _transform.GetWorldPosition(hostile));
            if (distance >= bestDistance)
                continue;

            best = hostile;
            bestDistance = distance;
        }

        return best;
    }

    private bool Candidate(Entity<WFNpcIntentComponent> ent, EntityUid target, bool requireSight)
    {
        if (target == ent.Owner || TerminatingOrDeleted(target) || !HasComp<MobStateComponent>(target)
            || IsDowned(target) || ent.Comp.Cooldowns.ContainsKey(target) || _factions.IsIgnored(ent.Owner, target))
            return false;

        if (ent.Comp.PlayersOnly && requireSight && !HasComp<ActorComponent>(target))
            return false;

        return !requireSight || _interaction.InRangeUnobstructed(ent.Owner, target, ent.Comp.SightRange, CollisionGroup.Opaque);
    }

    /// <summary>Rolls an endpoint from the set; a hold-up is only an option for a brain that can hold.</summary>
    private WFNpcIntentKind Roll(WFNpcIntentComponent comp, bool held)
    {
        var set = _proto.Index(comp.Set);
        var total = 0f;
        foreach (var choice in set.Intents)
        {
            if (Allowed(choice.Kind, held))
                total += choice.Weight;
        }

        if (total <= 0f)
            return set.Refusal;

        var roll = _random.NextFloat() * total;
        foreach (var choice in set.Intents)
        {
            if (!Allowed(choice.Kind, held))
                continue;

            roll -= choice.Weight;
            if (roll <= 0f)
                return choice.Kind;
        }

        return set.Refusal;
    }

    private static bool Allowed(WFNpcIntentKind kind, bool held) => kind != WFNpcIntentKind.HoldUp || held;

    /// <summary>Starts an engagement with a target and an endpoint.</summary>
    public void Begin(Entity<WFNpcIntentComponent> ent, EntityUid target, WFNpcIntentKind kind)
    {
        var comp = ent.Comp;
        if (comp.Phase != WFNpcIntentPhase.None)
            Reset(ent);

        var now = _timing.CurTime;
        var set = _proto.Index(comp.Set);

        comp.Active = kind;
        comp.Target = target;
        comp.Started = now;
        comp.LastSeen = now;
        comp.Paid = 0;
        comp.SecondShot = false;

        switch (kind)
        {
            case WFNpcIntentKind.HoldUp when comp.Holds:
                comp.Demand = _random.Next(set.DemandMin, set.DemandMax + 1);
                comp.Phase = WFNpcIntentPhase.Demanding;
                comp.PhaseEnd = now + TimeSpan.FromSeconds(set.DemandSeconds);
                Hold(ent, target);
                Say(ent, set.DemandLines, ("amount", comp.Demand));
                break;
            case WFNpcIntentKind.WarnOff:
                comp.Phase = WFNpcIntentPhase.Warning;
                comp.PhaseEnd = now + TimeSpan.FromSeconds(set.WarnSeconds);
                Hold(ent, target);
                Say(ent, set.WarnLines);
                FireWarning(ent, target);
                break;
            default:
                comp.Phase = WFNpcIntentPhase.Fighting;
                break;
        }
    }

    /// <summary>Has the NPC's gang-mates take up the same endpoint against the same target.</summary>
    private void Recruit(Entity<WFNpcIntentComponent> leader, EntityUid target, WFNpcIntentKind kind)
    {
        _gang.Clear();
        _lookup.GetEntitiesInRange(Transform(leader).Coordinates, GangRange, _gang);
        foreach (var mate in _gang)
        {
            if (mate.Owner == leader.Owner || mate.Comp.Set != leader.Comp.Set || mate.Comp.Phase != WFNpcIntentPhase.None
                || !_mobState.IsAlive(mate) || !Candidate(mate, target, requireSight: false))
                continue;

            mate.Comp.Follower = true;
            Begin(mate, target, kind);
            mate.Comp.Demand = leader.Comp.Demand;
        }
    }

    private void TickFight(Entity<WFNpcIntentComponent> ent, TransformComponent xform, TimeSpan now)
    {
        var comp = ent.Comp;
        var target = comp.Target!.Value;
        var kind = comp.Active ?? WFNpcIntentKind.Lethal;

        if (_mobState.IsDead(target))
        {
            Reset(ent);
            return;
        }

        var there = Transform(target);
        var distance = there.MapID == xform.MapID
            ? Vector2.Distance(_transform.GetWorldPosition(xform), _transform.GetWorldPosition(there))
            : float.MaxValue;
        if (distance <= LostRange && _interaction.InRangeUnobstructed(ent.Owner, target, 14f, CollisionGroup.Opaque))
            comp.LastSeen = now;

        // It lost them, or, warning them off, they have gone.
        var gone = kind == WFNpcIntentKind.WarnOff && distance > _proto.Index(comp.Set).WarnRange;
        if (distance > LostRange || gone || now - comp.LastSeen > LostAfter)
        {
            if (gone)
                Say(ent, _proto.Index(comp.Set).WarnOffLines);
            if (gone || kind != WFNpcIntentKind.Lethal)
                Spare(ent, target);
            Reset(ent);
            return;
        }

        if (kind == WFNpcIntentKind.Lethal || !IsDowned(target))
            return;

        Down(ent, target);
    }

    /// <summary>The target is down: stop, take what the endpoint calls for and go.</summary>
    private void Down(Entity<WFNpcIntentComponent> ent, EntityUid target)
    {
        var comp = ent.Comp;
        var set = _proto.Index(comp.Set);
        var now = _timing.CurTime;

        Spare(ent, target);

        switch (comp.Active)
        {
            case WFNpcIntentKind.Mug or WFNpcIntentKind.Harvest when !comp.Follower:
                comp.Phase = WFNpcIntentPhase.Taking;
                comp.PhaseEnd = now + TimeSpan.FromSeconds(comp.TakeSeconds);
                _walker.WalkTo(ent, new EntityCoordinates(target, Vector2.Zero));
                break;
            case WFNpcIntentKind.BeatDown:
                if (!comp.Follower)
                    Say(ent, set.BeatLines);
                Leave(ent);
                break;
            case WFNpcIntentKind.WarnOff:
                Say(ent, set.WarnOffLines);
                Leave(ent);
                break;
            default:
                Leave(ent);
                break;
        }
    }

    /// <summary>Stops the NPC attacking a target and leaves them be for the set's cooldown.</summary>
    private void Spare(Entity<WFNpcIntentComponent> ent, EntityUid target)
    {
        var set = _proto.Index(ent.Comp.Set);
        ent.Comp.Cooldowns[target] = _timing.CurTime + TimeSpan.FromSeconds(set.Cooldown);
        _factions.IgnoreEntity(ent.Owner, target);
        _factions.DeAggroEntity(ent.Owner, target);
        StopFighting(ent);
    }

    private void StopFighting(EntityUid npc)
    {
        RemComp<NPCMeleeCombatComponent>(npc);
        RemComp<NPCRangedCombatComponent>(npc);

        if (!TryComp(npc, out HTNComponent? htn))
            return;

        htn.Blackboard.Remove<EntityUid>("Target");
        htn.Blackboard.Remove<EntityCoordinates>("TargetCoordinates");
        _htn.Replan(htn);
    }

    /// <summary>Has the NPC aim at a target and stand still, on a brain with the hold branch.</summary>
    private void Hold(Entity<WFNpcIntentComponent> ent, EntityUid target)
    {
        _factions.IgnoreEntity(ent.Owner, target);
        _factions.DeAggroEntity(ent.Owner, target);
        StopFighting(ent);

        if (ent.Comp.Holds && TryComp(ent, out HTNComponent? htn))
        {
            _npc.SetBlackboard(ent, HoldKey, true, htn);
            _htn.Replan(htn);
        }

        _combat.SetInCombatMode(ent, true);
    }

    private void ReleaseHold(EntityUid npc)
    {
        if (!TryComp(npc, out HTNComponent? htn) || !htn.Blackboard.Remove<bool>(HoldKey))
            return;

        _htn.Replan(htn);
    }

    /// <summary>The hold-up or warning failed: the target fights, and the NPC answers with its refusal endpoint.</summary>
    public void Refuse(Entity<WFNpcIntentComponent> ent)
    {
        var comp = ent.Comp;
        var set = _proto.Index(comp.Set);
        var target = comp.Target;

        ReleaseHold(ent);
        if (target is { } mark)
            _factions.UnignoreEntity(ent.Owner, mark);

        if (!comp.Follower)
            Say(ent, set.RefusedLines);

        var next = comp.Active == WFNpcIntentKind.WarnOff ? WFNpcIntentKind.BeatDown : set.Refusal;
        comp.Active = next;
        comp.Phase = WFNpcIntentPhase.Fighting;
        comp.LastSeen = _timing.CurTime;

        if (target is { } enemy)
            _factions.AggroEntity(ent.Owner, enemy);

        // The rest of the gang that was holding the same target takes the fight up too.
        var query = EntityQueryEnumerator<WFNpcIntentComponent>();
        while (query.MoveNext(out var uid, out var other))
        {
            if (uid == ent.Owner || other.Target != target || other.Phase is not (WFNpcIntentPhase.Demanding or WFNpcIntentPhase.Warning))
                continue;

            ReleaseHold(uid);
            if (target is { } t)
            {
                _factions.UnignoreEntity(uid, t);
                _factions.AggroEntity(uid, t);
            }

            other.Active = next;
            other.Phase = WFNpcIntentPhase.Fighting;
            other.LastSeen = _timing.CurTime;
        }
    }

    /// <summary>Walks the NPC off and leaves it a while to get clear.</summary>
    private void Leave(Entity<WFNpcIntentComponent> ent)
    {
        var comp = ent.Comp;
        comp.Phase = WFNpcIntentPhase.Leaving;
        comp.PhaseEnd = _timing.CurTime + TimeSpan.FromSeconds(comp.LeaveSeconds);
        ReleaseHold(ent);
        _combat.SetInCombatMode(ent, false);

        var xform = Transform(ent);
        var away = new Vector2(_random.NextFloat(-1f, 1f), _random.NextFloat(-1f, 1f));
        if (comp.Target is { } target && !TerminatingOrDeleted(target) && Transform(target).MapID == xform.MapID)
            away = _transform.GetWorldPosition(xform) - _transform.GetWorldPosition(target);

        if (away.LengthSquared() < 0.01f)
            away = Vector2.UnitX;

        comp.LeaveTo = xform.Coordinates.Offset(Vector2.Normalize(away) * comp.LeaveDistance);
        _walker.WalkTo(ent, comp.LeaveTo.Value);
    }

    /// <summary>Ends an engagement and lets the brain go back to what it was doing.</summary>
    private void Reset(Entity<WFNpcIntentComponent> ent)
    {
        var comp = ent.Comp;
        if (comp.Phase is WFNpcIntentPhase.Demanding or WFNpcIntentPhase.Warning)
        {
            ReleaseHold(ent);
            if (comp.Target is { } held && !comp.Cooldowns.ContainsKey(held))
                _factions.UnignoreEntity(ent.Owner, held);
        }

        if (comp.LeaveTo is { } to && _walker.MoveTarget(ent) is { } current && current.Equals(to))
            _walker.Stop(ent);

        comp.LeaveTo = null;
        comp.Phase = WFNpcIntentPhase.None;
        comp.Active = null;
        comp.Target = null;
        comp.Follower = false;
        comp.Paid = 0;
    }

    private void Speak(EntityUid npc, List<LocId> lines, params (string, object)[] args)
    {
        if (lines.Count == 0)
            return;

        var message = Loc.GetString(_random.Pick(lines), args);
        _chat.TrySendInGameICMessage(npc, message, InGameICChatType.Speak, hideChat: false, hideLog: true,
            ignoreActionBlocker: true);
    }

    private void Say(Entity<WFNpcIntentComponent> ent, List<LocId> lines, params (string, object)[] args)
    {
        if (!ent.Comp.Follower)
            Speak(ent.Owner, lines, args);
    }
}
