using System.Linq;
using System.Numerics;
using Content.Server._NF.Bank;
using Content.Server._WF.Cowardice;
using Content.Server._WF.Planets.Bounds;
using Content.Server._WF.Vehicles.Npc;
using Content.Server.Chat.Systems;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Server.Shuttles.Components;
using Content.Shared._WF.Strangers;
using Content.Shared._WF.Vehicles.Core;
using Content.Shared.Chat;
using Content.Shared.Damage;
using Content.Shared.Examine;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Humanoid;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.Stacks;
using Content.Shared.Verbs;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.Strangers;

/// <summary>
/// Strangers: they notice players, walk up and hail them, and talk them through a branching dialogue that can end in a
/// sale, a mugging or a temper; or, for a stalker, keep a few metres back and turn up aboard the player's ship. Their own
/// brain stays off while they do it and only takes over when they turn hostile, until there's nobody left to fight.
/// Roamers wander the world; those with a home stay about it, go out to meet visitors and walk home after (see
/// WFStrangerSystem.Home.cs).
/// </summary>
public sealed partial class WFStrangerSystem : EntitySystem
{
    [Dependency] private BankSystem _bank = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private ExamineSystemShared _examine = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private HTNSystem _htn = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private ISharedPlayerManager _players = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private NPCSteeringSystem _steering = default!;
    [Dependency] private NpcFactionSystem _factions = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private SharedStackSystem _stacks = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WFPlanetBoundsSystem _bounds = default!;

    private const float Interval = 0.5f;

    /// <summary>How long a hail waits on an answer before it counts as ignored.</summary>
    private static readonly TimeSpan HailTimeout = TimeSpan.FromSeconds(15);

    /// <summary>How long he leaves someone be after they've talked.</summary>
    private static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(10);

    /// <summary>How long a hostile stranger goes without anyone to fight before he calms down.</summary>
    private static readonly TimeSpan CalmDelay = TimeSpan.FromSeconds(30);

    /// <summary>How far apart a conversation can stretch before he gives up on it.</summary>
    private const float ConversationRange = 4f;

    private float _accumulator;
    private readonly HashSet<Entity<HumanoidAppearanceComponent>> _seen = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WFStrangerHailReplyEvent>(OnHailReply);
        SubscribeNetworkEvent<WFStrangerChooseEvent>(OnChoose);
        SubscribeLocalEvent<WFStrangerComponent, GetVerbsEvent<ActivationVerb>>(OnVerbs);
        SubscribeLocalEvent<WFStrangerComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<WFStrangerComponent, BeforeDamageChangedEvent>(OnDamaged);
        SubscribeLocalEvent<WFStrangerComponent, MobStateChangedEvent>(OnMobState);
    }

    private void OnMapInit(Entity<WFStrangerComponent> ent, ref MapInitEvent args)
    {
        // He walks under his own steam until he turns on someone.
        if (TryComp(ent, out HTNComponent? htn))
            _htn.SetHTNEnabled((ent, htn), false);
        ent.Comp.NextMutter = _timing.CurTime + ent.Comp.MutterInterval;
        ent.Comp.Home = FindHome(ent);
    }

    /// <summary>
    /// Whoever hurts him, he goes for, and his housemates with him. Read before the damage lands, as medical routing
    /// applies a body's damage itself and the attacker doesn't reach the change.
    /// </summary>
    private void OnDamaged(Entity<WFStrangerComponent> ent, ref BeforeDamageChangedEvent args)
    {
        if (TryComp(ent, out WFCowardiceComponent? coward) && coward.State != WFCowardState.None)
            return;
        if (!args.Damage.AnyPositive() || args.Origin is not { } origin || Culprit(origin) is not { } attacker || attacker == ent.Owner
            || !_mobs.IsAlive(ent))
            return;
        if (ent.Comp.Hostile && _factions.GetHostiles(ent.Owner).Contains(attacker))
            return;
        TurnOn(ent, attacker, rally: true);
    }

    /// <summary>Dead, he lets go of whatever he was holding, a squatter's vehicle key included.</summary>
    private void OnMobState(Entity<WFStrangerComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Dead)
            return;
        foreach (var held in _hands.EnumerateHeld(ent.Owner).ToList())
            _hands.TryDrop(ent.Owner, held);
    }

    /// <summary>Who is behind a hit: whoever fired it, or the vehicle it came from.</summary>
    private EntityUid? Culprit(EntityUid origin)
    {
        var current = origin;
        for (var i = 0; i < 6 && current.IsValid(); i++)
        {
            if (HasComp<MobStateComponent>(current) || HasComp<VehicleInteriorComponent>(current))
                return current;
            current = Transform(current).ParentUid;
        }
        return null;
    }

    private void OnVerbs(Entity<WFStrangerComponent> ent, ref GetVerbsEvent<ActivationVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || !_mobs.IsAlive(ent) || !HasComp<ActorComponent>(args.User)
            || HoldsGrudge(ent, args.User))
            return;
        var user = args.User;
        args.Verbs.Add(new ActivationVerb
        {
            Text = Loc.GetString("wf-stranger-verb-talk"),
            Act = () => StartConversation(ent, user),
        });
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        _accumulator += frameTime;
        if (_accumulator < Interval)
            return;
        _accumulator = 0f;

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WFStrangerComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var stranger, out var xform))
        {
            if (!_mobs.IsAlive(uid) || xform.MapUid is not { } map || Paused(map))
                continue;
            // Frightened, he is left to the cowardice system until he calms down.
            if (TryComp(uid, out WFCowardiceComponent? scared) && scared.State != WFCowardState.None)
                continue;
            if (stranger.Hostile)
                CalmDown((uid, stranger), now);
            else if (Grudge((uid, stranger), xform) is { } grudge)
                TurnOn((uid, stranger), grudge, rally: false);
            else if (stranger.Behaviour == WFStrangerBehaviour.Squat)
            {
                if (Nearest((uid, stranger), xform, now) is { } intruder)
                    TurnOn((uid, stranger), intruder, rally: false);
            }
            else if (stranger.Behaviour == WFStrangerBehaviour.Stalk)
                Stalk((uid, stranger), xform, now, HomeOf(stranger));
            else
                Approach((uid, stranger), xform, now, HomeOf(stranger));
        }
    }

    private void Approach(Entity<WFStrangerComponent> ent, TransformComponent xform, TimeSpan now, Entity<WFStrangerHomeComponent>? home = null)
    {
        var stranger = ent.Comp;
        if (stranger.Node != null)
        {
            // Mid-conversation: it ends if they walk off.
            if (stranger.Target is not { } partner || !Near(ent, partner, ConversationRange))
                EndConversation(ent, leave: false);
            return;
        }

        if (stranger.Target is { } target && !Usable(ent, target, now, home))
        {
            LetGo(ent, target, now, home);
            stranger.Target = null;
            stranger.Hailed = false;
            _steering.Unregister(ent);
        }

        stranger.Target ??= home is { } place ? Visitor(ent, xform, place, now) : Nearest(ent, xform, now);
        if (stranger.Target is not { } mark)
        {
            if (home is { } residence)
                Wander(ent, xform, now, residence, residence.Comp.Radius * 0.8f);
            else
                Wander(ent, xform, now, null, stranger.WanderRange);
            return;
        }

        if (stranger.Hailed)
        {
            if (now - stranger.HailedAt > HailTimeout)
                Ignored(ent, mark);
            return;
        }

        if (Near(ent, mark, stranger.TalkRange))
        {
            _steering.Unregister(ent);
            Hail(ent, mark, now);
            return;
        }

        StopWandering(stranger);
        if (home is { } place2)
            Greet(ent, place2, mark, now);
        Follow(ent, mark);
    }

    /// <summary>
    /// A resident who lost someone past his leash leaves them be for a while, so he walks home instead of turning
    /// round at the edge of it and going out again.
    /// </summary>
    private void LetGo(Entity<WFStrangerComponent> ent, EntityUid target, TimeSpan now, Entity<WFStrangerHomeComponent>? home)
    {
        if (home is not { } place || TerminatingOrDeleted(target) || !_mobs.IsAlive(target))
            return;
        if (!WithinLeash(place, ent) || !WithinLeash(place, target) || !Reachable(ent, target))
            ent.Comp.Done[target] = now + LeashCooldown;
    }

    /// <summary>Walks after someone, asking for a new path only once they've moved off from where he was headed.</summary>
    private void Follow(EntityUid ent, EntityUid target, float slack = 1.5f)
    {
        var there = Transform(target).Coordinates;
        if (TryComp(ent, out NPCSteeringComponent? steering) && steering.Status == SteeringStatus.Moving
            && _transform.InRange(steering.Coordinates, there, slack))
            return;
        Walk(ent, there);
    }

    /// <summary>Somebody he hasn't finished with yet: alive, a player, in sight, and within a resident's leash.</summary>
    private bool Usable(Entity<WFStrangerComponent> ent, EntityUid target, TimeSpan now, Entity<WFStrangerHomeComponent>? home = null)
    {
        return !TerminatingOrDeleted(target) && _mobs.IsAlive(target) && HasComp<ActorComponent>(target)
               && (!ent.Comp.Done.TryGetValue(target, out var until) || now >= until)
               && Transform(target).MapID == Transform(ent).MapID
               && Near(ent, target, ent.Comp.SightRange * 1.5f)
               && (home is not { } place || WithinLeash(place, ent) && Reachable(ent, target));
    }

    private EntityUid? Nearest(Entity<WFStrangerComponent> ent, TransformComponent xform, TimeSpan now, Entity<WFStrangerHomeComponent>? home = null)
    {
        // Too far from home to pick anyone up: he's on his way back.
        if (home is { } place && !WithinLeash(place, ent))
            return null;
        _seen.Clear();
        var origin = _transform.GetMapCoordinates(ent, xform);
        _lookup.GetEntitiesInRange(origin, ent.Comp.SightRange, _seen);
        EntityUid? best = null;
        var bestDistance = float.MaxValue;
        foreach (var other in _seen)
        {
            if (other.Owner == ent.Owner || !Usable(ent, other, now, home))
                continue;
            var there = _transform.GetMapCoordinates(other);
            var distance = Vector2.Distance(origin.Position, there.Position);
            if (distance >= bestDistance || !_examine.InRangeUnOccluded(origin, there, ent.Comp.SightRange, null))
                continue;
            best = other;
            bestDistance = distance;
        }
        return best;
    }

    /// <summary>
    /// Sets off for <paramref name="to"/>. Steering that has given up (no path, as when it asked before the grid's
    /// pathfinding chunks were built) never retries the same coordinates, so it is dropped and asked again.
    /// </summary>
    private void Walk(EntityUid ent, EntityCoordinates to)
    {
        if (TryComp(ent, out NPCSteeringComponent? steering) && steering.Status == SteeringStatus.NoPath)
            _steering.Unregister(ent, steering);
        _steering.Register(ent, to);
    }

    private bool Near(EntityUid a, EntityUid b, float range)
    {
        var first = _transform.GetMapCoordinates(a);
        var second = _transform.GetMapCoordinates(b);
        return first.MapId == second.MapId && Vector2.Distance(first.Position, second.Position) <= range;
    }

    /// <summary>Calls out and asks the player for a word.</summary>
    private void Hail(Entity<WFStrangerComponent> ent, EntityUid target, TimeSpan now)
    {
        if (!_prototypes.TryIndex(ent.Comp.Dialogue, out var dialogue) || !TryComp(target, out ActorComponent? actor))
            return;

        ent.Comp.Hailed = true;
        ent.Comp.HailedAt = now;
        var line = dialogue.Hail is { } hail ? Loc.GetString(hail) : Loc.GetString(dialogue.Nodes[dialogue.Start].Line);
        _chat.TrySendInGameICMessage(ent, line, InGameICChatType.Speak, false);
        RaiseNetworkEvent(new WFStrangerHailEvent { Stranger = GetNetEntity(ent), Name = Name(ent), Line = line }, actor.PlayerSession);
    }

    private void OnHailReply(WFStrangerHailReplyEvent ev, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { } user || !TryGetEntity(ev.Stranger, out var uid)
            || !TryComp(uid, out WFStrangerComponent? stranger) || stranger.Target != user || !stranger.Hailed)
            return;

        if (ev.Accept)
            StartConversation((uid.Value, stranger), user);
        else
            Ignored((uid.Value, stranger), user);
    }

    /// <summary>Waved off: his dialogue says how he takes it.</summary>
    private void Ignored(Entity<WFStrangerComponent> ent, EntityUid target)
    {
        ent.Comp.Hailed = false;
        if (_prototypes.TryIndex(ent.Comp.Dialogue, out var dialogue) && dialogue.Ignored is { } node)
        {
            ent.Comp.Target = target;
            GoTo(ent, target, dialogue, node);
            return;
        }
        Finish(ent, target);
    }

    /// <summary>Opens the conversation at its start; a player can start one himself through the Talk verb.</summary>
    public void StartConversation(Entity<WFStrangerComponent> ent, EntityUid user)
    {
        if (!_prototypes.TryIndex(ent.Comp.Dialogue, out var dialogue) || !_mobs.IsAlive(ent))
            return;
        if (ent.Comp.Node != null && ent.Comp.Target != user)
            return;
        // He remembers: there's no talking to him once you're in his bad books.
        if (HoldsGrudge(ent, user))
        {
            if (!ent.Comp.Hostile)
                TurnOn(ent, user, rally: true);
            return;
        }

        _steering.Unregister(ent);
        ent.Comp.Target = user;
        ent.Comp.Hailed = false;
        GoTo(ent, user, dialogue, dialogue.Start);
    }

    private void OnChoose(WFStrangerChooseEvent ev, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { } user || !TryGetEntity(ev.Stranger, out var uid)
            || !TryComp(uid, out WFStrangerComponent? stranger))
            return;
        Choose((uid.Value, stranger), user, ev.Option);
    }

    /// <summary>The player answers with an option of the current node.</summary>
    public void Choose(Entity<WFStrangerComponent> ent, EntityUid user, int index)
    {
        var stranger = ent.Comp;
        if (stranger.Target != user || stranger.Node is not { } nodeId || !Near(ent, user, ConversationRange)
            || !_prototypes.TryIndex(stranger.Dialogue, out var dialogue) || !dialogue.Nodes.TryGetValue(nodeId, out var node)
            || index < 0 || index >= node.Options.Count)
            return;

        var option = node.Options[index];
        _chat.TrySendInGameICMessage(user, Loc.GetString(option.Text), InGameICChatType.Speak, false);

        if (option.Cost > 0 && !TryPay(user, option.Cost))
        {
            if (option.Broke is { } broke)
                GoTo(ent, user, dialogue, broke);
            else
                EndConversation(ent, leave: false);
            return;
        }

        if (option.Items.Count > 0)
        {
            var item = Spawn(_random.Pick(option.Items), Transform(user).Coordinates);
            _hands.PickupOrDrop(user, item);
        }

        if (option.Next is { } next)
            GoTo(ent, user, dialogue, next);
        else
            EndConversation(ent, leave: true);
    }

    /// <summary>Moves the talk on to a node: says its line, does its action, shows the player the answers.</summary>
    private void GoTo(Entity<WFStrangerComponent> ent, EntityUid user, WFStrangerDialoguePrototype dialogue, string nodeId)
    {
        if (!dialogue.Nodes.TryGetValue(nodeId, out var node))
        {
            Log.Error($"Stranger dialogue {dialogue.ID} has no node {nodeId}");
            EndConversation(ent, leave: false);
            return;
        }

        var line = Loc.GetString(node.Line);
        _chat.TrySendInGameICMessage(ent, line, InGameICChatType.Speak, false);
        var ended = node.Options.Count == 0;
        ent.Comp.Node = ended ? null : nodeId;
        if (TryComp(user, out ActorComponent? actor))
        {
            RaiseNetworkEvent(new WFStrangerDialogueEvent
            {
                Stranger = GetNetEntity(ent),
                Name = Name(ent),
                Line = line,
                Options = node.Options.Select(option => option.Cost > 0
                    ? Loc.GetString("wf-stranger-option-cost", ("text", Loc.GetString(option.Text)), ("cost", option.Cost))
                    : Loc.GetString(option.Text)).ToList(),
                Ended = ended,
            }, actor.PlayerSession);
        }

        switch (node.Action)
        {
            case WFStrangerAction.Hostile:
                TurnOn(ent, user, rally: true);
                break;
            case WFStrangerAction.Leave:
                Finish(ent, user);
                break;
            default:
                if (ended)
                    Finish(ent, user);
                break;
        }
    }

    private void EndConversation(Entity<WFStrangerComponent> ent, bool leave)
    {
        if (ent.Comp.Target is { } user)
        {
            if (TryComp(user, out ActorComponent? actor))
                RaiseNetworkEvent(new WFStrangerDialogueEvent { Stranger = GetNetEntity(ent), Ended = true }, actor.PlayerSession);
            Finish(ent, user);
        }
        ent.Comp.Node = null;
    }

    /// <summary>He's done with this player for a while and goes back to wandering, or home.</summary>
    private void Finish(Entity<WFStrangerComponent> ent, EntityUid user)
    {
        ent.Comp.Done[user] = _timing.CurTime + Cooldown;
        ent.Comp.NextWander = _timing.CurTime;
        StopWandering(ent.Comp);
        ent.Comp.Target = null;
        ent.Comp.Node = null;
        ent.Comp.Hailed = false;
        _steering.Unregister(ent);
    }

    /// <summary>
    /// He goes for someone: his own brain takes over, hostile to them alone and to the vehicle they're in. Rallied, the
    /// others who live with him join in.
    /// </summary>
    private void TurnOn(Entity<WFStrangerComponent> ent, EntityUid user, bool rally)
    {
        if (ent.Comp.Target == user && ent.Comp.Node != null)
            EndConversation(ent, leave: false);
        Finish(ent, user);
        ent.Comp.Done[user] = TimeSpan.MaxValue;
        ent.Comp.Hostile = true;
        ent.Comp.CalmAt = null;
        _factions.AggroEntity(ent.Owner, user);
        if (TryComp(user, out VehicleInteriorOccupantComponent? occupant) && Exists(occupant.Vehicle))
            _factions.AggroEntity(ent.Owner, occupant.Vehicle);
        if (TryComp(ent, out HTNComponent? htn))
            _htn.SetHTNEnabled((ent, htn), true);

        if (!rally || ent.Comp.Home is not { } home)
            return;
        var mates = EntityQueryEnumerator<WFStrangerComponent>();
        while (mates.MoveNext(out var mate, out var stranger))
        {
            if (mate != ent.Owner && stranger.Home == home && _mobs.IsAlive(mate)
                && !_factions.GetHostiles(mate).Contains(user))
                TurnOn((mate, stranger), user, rally: false);
        }
    }

    /// <summary>He loses his nerve: no more fighting, hailing or wandering, and he drops the quarrel with whoever scared him.</summary>
    public void Frighten(Entity<WFStrangerComponent> ent, EntityUid threat)
    {
        if (ent.Comp.Target == threat && ent.Comp.Node != null)
            EndConversation(ent, leave: false);
        Finish(ent, threat);
        ent.Comp.Hostile = false;
        ent.Comp.CalmAt = null;
        _factions.DeAggroEntity(ent.Owner, threat);
        if (TryComp(ent, out HTNComponent? htn))
            _htn.SetHTNEnabled((ent, htn), false);
        _steering.Unregister(ent);
    }

    /// <summary>With nobody left to fight for a while he calms down and goes back to his business, grudges kept.</summary>
    private void CalmDown(Entity<WFStrangerComponent> ent, TimeSpan now)
    {
        if (_factions.GetNearbyHostiles(ent.Owner, ent.Comp.SightRange * 1.5f).Any())
        {
            ent.Comp.CalmAt = null;
            return;
        }
        ent.Comp.CalmAt ??= now + CalmDelay;
        if (now < ent.Comp.CalmAt)
            return;

        ent.Comp.Hostile = false;
        ent.Comp.CalmAt = null;
        ent.Comp.NextWander = now;
        StopWandering(ent.Comp);
        if (TryComp(ent, out HTNComponent? htn))
            _htn.SetHTNEnabled((ent, htn), false);
        _steering.Unregister(ent);
    }

    /// <summary>Whether he has turned on this person before.</summary>
    public bool HoldsGrudge(Entity<WFStrangerComponent> ent, EntityUid user)
    {
        return ent.Comp.Done.TryGetValue(user, out var until) && until == TimeSpan.MaxValue;
    }

    /// <summary>Someone he holds a grudge against, back in sight.</summary>
    private EntityUid? Grudge(Entity<WFStrangerComponent> ent, TransformComponent xform)
    {
        if (!TryComp(ent, out FactionExceptionComponent? exceptions) || exceptions.Hostiles.Count == 0)
            return null;
        var here = _transform.GetMapCoordinates(ent, xform);
        foreach (var enemy in exceptions.Hostiles)
        {
            if (TerminatingOrDeleted(enemy) || !_mobs.IsAlive(enemy) && !HasComp<WFOccupiedVehicleComponent>(enemy))
                continue;
            var there = _transform.GetMapCoordinates(enemy);
            if (there.MapId == here.MapId && _examine.InRangeUnOccluded(here, there, ent.Comp.SightRange, null))
                return enemy;
        }
        return null;
    }

    /// <summary>Cash in hand first, then the bank.</summary>
    private bool TryPay(EntityUid user, int cost)
    {
        foreach (var held in _hands.EnumerateHeld(user))
        {
            if (!TryComp(held, out StackComponent? stack) || stack.StackTypeId != "Credit" || stack.Count < cost)
                continue;
            _stacks.SetCount(held, stack.Count - cost, stack);
            return true;
        }
        return _bank.TryBankWithdraw(user, cost);
    }

    /// <summary>
    /// Keeps a few metres behind his mark, mutters now and then, and when the mark goes aboard a ship he isn't on, turns
    /// up aboard it a little later, somewhere out of the way. One with a home keeps to its leash and never boards.
    /// </summary>
    private void Stalk(Entity<WFStrangerComponent> ent, TransformComponent xform, TimeSpan now, Entity<WFStrangerHomeComponent>? home = null)
    {
        var stranger = ent.Comp;
        if (stranger.Target is not { } mark || TerminatingOrDeleted(mark) || !_mobs.IsAlive(mark) || !HasComp<ActorComponent>(mark)
            || home is { } leashed && (Transform(mark).MapID != xform.MapID || !WithinLeash(leashed, mark) || !Reachable(ent, mark)))
        {
            if (stranger.Target is { } lost)
            {
                LetGo(ent, lost, now, home);
                _steering.Unregister(ent);
            }
            stranger.Target = Nearest(ent, xform, now, home);
            if (stranger.Target != null)
                StopWandering(stranger);
            stranger.MarkAboardSince = null;
            if (stranger.Target == null)
            {
                if (home is { } residence)
                    Wander(ent, xform, now, residence, residence.Comp.Radius * 0.8f);
                else
                    Wander(ent, xform, now, null, stranger.WanderRange);
            }
            return;
        }

        var markXform = Transform(mark);
        if (home == null && markXform.GridUid is { } ship && ship != xform.GridUid && HasComp<ShuttleComponent>(ship))
        {
            stranger.MarkAboardSince ??= now;
            if (now - stranger.MarkAboardSince >= stranger.BoardDelay)
            {
                stranger.MarkAboardSince = null;
                Board(ent, mark, ship);
            }
            return;
        }
        stranger.MarkAboardSince = null;

        if (markXform.MapID != xform.MapID)
            return;

        // He makes for the middle of his band only once he's out of it, and settles once he's well inside, so he
        // doesn't dither back and forth on its edges.
        var here = _transform.GetWorldPosition(xform);
        var there = _transform.GetWorldPosition(markXform);
        var distance = Vector2.Distance(here, there);
        var middle = (stranger.StalkNear + stranger.StalkFar) / 2f;
        if ((distance > stranger.StalkFar || distance < stranger.StalkNear - 1f) && distance > 0.01f)
        {
            var spot = new EntityCoordinates(xform.MapUid!.Value, there + (here - there) / distance * middle);
            if (!TryComp(ent, out NPCSteeringComponent? steering) || steering.Status != SteeringStatus.Moving
                || !_transform.InRange(steering.Coordinates, spot, 1.5f))
                Walk(ent, spot);
        }
        else if (Math.Abs(distance - middle) < 1f)
            _steering.Unregister(ent);

        if (now >= stranger.NextMutter && stranger.Mutters.Count > 0)
        {
            stranger.NextMutter = now + stranger.MutterInterval * (0.75 + _random.NextDouble() * 0.5);
            _chat.TrySendInGameICMessage(ent, Loc.GetString(_random.Pick(stranger.Mutters)), InGameICChatType.Whisper, false);
        }
    }

    /// <summary>Turns up aboard the mark's ship, as far from the mark as its open deck allows.</summary>
    private void Board(Entity<WFStrangerComponent> ent, EntityUid mark, EntityUid ship)
    {
        if (!TryComp(ship, out MapGridComponent? grid))
            return;
        var markTile = _maps.TileIndicesFor(ship, grid, Transform(mark).Coordinates);
        var floor = _maps.GetAllTiles(ship, grid)
            .Select(tile => tile.GridIndices)
            .Where(tile => !_maps.GetAnchoredEntitiesEnumerator(ship, grid, tile).MoveNext(out _))
            .OrderByDescending(tile => (tile - markTile).LengthSquared)
            .Take(8)
            .ToList();
        if (floor.Count == 0)
            return;

        _steering.Unregister(ent);
        _transform.SetCoordinates(ent, new EntityCoordinates(ship, (Vector2) _random.Pick(floor) + new Vector2(0.5f)));
        _transform.AttachToGridOrMap(ent);
    }
}
