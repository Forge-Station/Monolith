using System.Linq;
using System.Numerics;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server._WF.NpcIntents;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Chat.Systems;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Shared._Onyx.Wounds;
using Content.Shared.Chat;
using Content.Shared.CombatMode;
using Content.Shared.Cuffs.Components;
using Content.Shared.Damage;
using Content.Shared.Doors.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Pulling.Events;
using Content.Shared.NPC.Systems;
using Content.Shared.Verbs;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.ShipSquatters;

/// <summary>
/// Squatters in landed ships nobody is minding: one moves in, sets up a bed, wanders his rooms making a mess and insists
/// the ship is his. He is not violent, but whoever forces him (a blow, a shot, a drag, cuffs or a shove) gets his
/// spear until they are down, gone or he is dead, and he calms down again a few minutes after. Where they come
/// from is in WFShipSquatterSystem.Spawn.cs, what he leaves about in .Mess.cs, how he is talked to in .Talk.cs.
/// </summary>
public sealed partial class WFShipSquatterSystem : EntitySystem
{
    [Dependency] private AtmosphereSystem _atmos = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private HTNSystem _htn = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private NPCSteeringSystem _steering = default!;
    [Dependency] private NpcFactionSystem _factions = default!;
    [Dependency] private SharedCombatModeSystem _combat = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WFCrewPlannerSystem _planner = default!;
    [Dependency] private WFCrewSystem _crew = default!;
    [Dependency] private WFNpcIntentSystem _intents = default!;
    [Dependency] private WoundDamageRoutingSystem _wounds = default!;

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(0.5);

    /// <summary>How long a leg of his wander may take before he gives up on it.</summary>
    private static readonly TimeSpan LegPatience = TimeSpan.FromSeconds(25);

    /// <summary>Tiles he walks off the end of a conversation from.</summary>
    private const float TalkRange = 4f;

    /// <summary>Tiles round the cuffed that count as whoever cuffed him.</summary>
    private const float CuffReach = 3f;

    private TimeSpan _next;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WFShipSquatterComponent, MapInitEvent>(OnMapInit);
        // Before the wound system cancels the hit and loses who threw it.
        SubscribeLocalEvent<WFShipSquatterComponent, BeforeDamageChangedEvent>(OnBeforeDamage,
            before: new[] { typeof(WoundDamageRoutingSystem) });
        SubscribeLocalEvent<WFShipSquatterComponent, PullStartedMessage>(OnPullStarted);
        SubscribeLocalEvent<WFShipSquatterComponent, DisarmedEvent>(OnDisarmed);
        SubscribeLocalEvent<WFShipSquatterComponent, CuffedStateChangeEvent>(OnCuffed);
        SubscribeLocalEvent<WFShipSquatterComponent, MobStateChangedEvent>(OnMobState);
        InitializeTalk();
    }

    private void OnMapInit(Entity<WFShipSquatterComponent> ent, ref MapInitEvent args)
    {
        // He walks under his own steam until someone forces him.
        if (TryComp(ent, out HTNComponent? htn))
            _htn.SetHTNEnabled((ent, htn), false);
        var now = _timing.CurTime;
        ent.Comp.NextMess = now + Seconds(ent.Comp.MessMin, ent.Comp.MessMax);
        ent.Comp.NextWander = now + Seconds(ent.Comp.LingerMin, ent.Comp.LingerMax);
    }

    private TimeSpan Seconds(float min, float max) => TimeSpan.FromSeconds(_random.NextFloat(min, max));

    private void OnBeforeDamage(Entity<WFShipSquatterComponent> ent, ref BeforeDamageChangedEvent args)
    {
        // The wound system's routed pass raises this again for the same hit.
        if (args.Cancelled || _wounds.IsRouting(ent) || !args.Damage.AnyPositive() || !_mobs.IsAlive(ent))
            return;

        if (args.Origin is not { } origin || origin == ent.Owner || TerminatingOrDeleted(origin))
            return;

        var who = _crew.Wielder(origin);
        if (who != ent.Owner && HasComp<MobStateComponent>(who))
            Force(ent, who);
    }

    private void OnPullStarted(EntityUid uid, WFShipSquatterComponent comp, PullStartedMessage args)
    {
        if (args.PulledUid == uid && args.PullerUid != uid)
            Force((uid, comp), args.PullerUid);
    }

    private void OnDisarmed(Entity<WFShipSquatterComponent> ent, ref DisarmedEvent args)
    {
        if (args.Source != ent.Owner)
            Force(ent, args.Source);
    }

    /// <summary>Cuffs go on after a do-after, so whoever is at his side is who put them there.</summary>
    private void OnCuffed(Entity<WFShipSquatterComponent> ent, ref CuffedStateChangeEvent args)
    {
        if (!_mobs.IsAlive(ent))
            return;

        EntityUid? best = null;
        var bestDistance = float.MaxValue;
        var here = _transform.GetMapCoordinates(ent);
        foreach (var other in _lookup.GetEntitiesInRange<MobStateComponent>(here, CuffReach))
        {
            if (other.Owner == ent.Owner || !_mobs.IsAlive(other) || !HasComp<ActorComponent>(other))
                continue;

            var distance = Vector2.Distance(here.Position, _transform.GetWorldPosition(other));
            if (distance >= bestDistance)
                continue;

            best = other;
            bestDistance = distance;
        }

        if (best is { } cuffer)
            Force(ent, cuffer);
    }

    private void OnMobState(Entity<WFShipSquatterComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Alive)
            return;

        EndTalk(ent);
        ent.Comp.Forcers.Clear();
        _steering.Unregister(ent);
    }

    /// <summary>
    /// Someone forced him: he goes for them with his own brain until they are down, gone or he is dead. A target he had
    /// let off is fair game again.
    /// </summary>
    public void Force(Entity<WFShipSquatterComponent> ent, EntityUid who)
    {
        if (!_mobs.IsAlive(ent) || TerminatingOrDeleted(who))
            return;

        var comp = ent.Comp;
        comp.Forcers[who] = _timing.CurTime;
        comp.CalmAt = null;
        _intents.Provoked(ent, who);
        _factions.AggroEntity(ent.Owner, who);

        if (comp.Mood == WFSquatterMood.Angry)
        {
            Fight(ent);
            return;
        }

        EndTalk(ent);
        comp.Mood = WFSquatterMood.Angry;
        comp.WanderUntil = null;
        _steering.Unregister(ent);
        Say(ent, comp.AngryBarks);
        Fight(ent);
    }

    /// <summary>Wakes his own brain.</summary>
    private void Fight(Entity<WFShipSquatterComponent> ent)
    {
        if (TryComp(ent, out HTNComponent? htn))
            _htn.SetHTNEnabled((ent, htn), true);
    }

    /// <summary>Puts the brain to sleep and stops the fight.</summary>
    private void Stand(Entity<WFShipSquatterComponent> ent)
    {
        if (TryComp(ent, out HTNComponent? htn))
        {
            _htn.SetHTNEnabled((ent, htn), false);
            htn.Blackboard.Remove<EntityUid>("Target");
        }

        RemComp<NPCMeleeCombatComponent>(ent);
        RemComp<NPCRangedCombatComponent>(ent);
        _combat.SetInCombatMode(ent, false);
    }

    /// <inheritdoc/>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        UpdateSpawns();

        var now = _timing.CurTime;
        if (now < _next)
            return;

        _next = now + Interval;
        var query = EntityQueryEnumerator<WFShipSquatterComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var comp, out var xform))
        {
            if (!_mobs.IsAlive(uid) || xform.MapUid is not { } map || Paused(map))
                continue;

            Tick((uid, comp), xform, now);
        }
    }

    private void Tick(Entity<WFShipSquatterComponent> ent, TransformComponent xform, TimeSpan now)
    {
        var comp = ent.Comp;
        TickTalk(ent);

        if (comp.Mood == WFSquatterMood.Angry)
        {
            TickAngry(ent, xform, now);
            return;
        }

        Bark(ent, xform, now);
        if (xform.GridUid != comp.Ship || !TryComp(comp.Ship, out MapGridComponent? grid))
        {
            GoHome(ent, xform);
            return;
        }

        if (comp.Talking == null)
        {
            MakeMess(ent, xform, grid, now);
            Wander(ent, xform, grid, now);
        }
    }

    /// <summary>Drops forcers who are down, gone or dead; with none left he calms down after a while.</summary>
    private void TickAngry(Entity<WFShipSquatterComponent> ent, TransformComponent xform, TimeSpan now)
    {
        var comp = ent.Comp;
        var here = _transform.GetMapCoordinates(xform);
        foreach (var forcer in comp.Forcers.Keys.ToList())
        {
            if (!TerminatingOrDeleted(forcer) && !_intents.IsDowned(forcer) && !_intents.InCooldown(ent, forcer)
                && _transform.GetMapCoordinates(forcer) is var there && there.MapId == here.MapId
                && Vector2.Distance(here.Position, there.Position) <= comp.LeashRange)
                continue;

            comp.Forcers.Remove(forcer);
            if (!TerminatingOrDeleted(forcer))
                _factions.DeAggroEntity(ent.Owner, forcer);
        }

        if (comp.Forcers.Count > 0)
        {
            comp.CalmAt = null;
            return;
        }

        // Nobody to fight: he stands there glaring until he cools off.
        Stand(ent);
        comp.CalmAt ??= now + TimeSpan.FromSeconds(comp.CalmSeconds);
        if (now < comp.CalmAt)
            return;

        comp.Mood = WFSquatterMood.Sitting;
        comp.CalmAt = null;
        comp.NextWander = now + Seconds(comp.LingerMin, comp.LingerMax);
        Say(ent, comp.CalmBarks);
    }

    /// <summary>Carried or chased off the hull, he walks back to his bed if he can.</summary>
    private void GoHome(Entity<WFShipSquatterComponent> ent, TransformComponent xform)
    {
        var comp = ent.Comp;
        if (TerminatingOrDeleted(comp.Ship) || !comp.Bed.IsValid(EntityManager)
            || Transform(comp.Ship).MapID != xform.MapID)
            return;

        comp.WanderUntil = null;
        if (TryComp(ent, out NPCSteeringComponent? steering))
        {
            if (steering.Status == SteeringStatus.Moving && steering.Coordinates.Equals(comp.Bed))
                return;
            _steering.Unregister(ent, steering);
        }

        _steering.Register(ent, comp.Bed);
    }

    /// <summary>
    /// Walks a leg at a time to somewhere in his rooms, mostly a stand about, now and then back to the bed, and lingers
    /// there before the next.
    /// </summary>
    private void Wander(Entity<WFShipSquatterComponent> ent, TransformComponent xform, MapGridComponent grid, TimeSpan now)
    {
        var comp = ent.Comp;
        if (comp.WanderUntil is { } until)
        {
            if (now < until && TryComp(ent, out NPCSteeringComponent? steering) && steering.Status == SteeringStatus.Moving)
                return;

            comp.WanderUntil = null;
            _steering.Unregister(ent);
            comp.NextWander = now + Seconds(comp.LingerMin, comp.LingerMax);
            return;
        }

        if (now < comp.NextWander)
            return;

        comp.NextWander = now + Seconds(comp.LingerMin, comp.LingerMax);
        ScanRooms(ent, grid);
        var here = _maps.CoordinatesToTile(comp.Ship, grid, xform.Coordinates);
        var spots = comp.Rooms.Where(tile => Math.Max(Math.Abs(tile.X - here.X), Math.Abs(tile.Y - here.Y)) >= 2
                                              && _planner.IsStandable(comp.Ship, tile, grid)).ToList();
        if (spots.Count == 0)
            return;

        var to = _random.Prob(0.3f) ? comp.Bed : _maps.GridTileToLocal(comp.Ship, grid, _random.Pick(spots));
        if (TryComp(ent, out NPCSteeringComponent? old) && old.Status == SteeringStatus.NoPath)
            _steering.Unregister(ent, old);

        _steering.Register(ent, to);
        comp.WanderUntil = now + LegPatience;
    }

    /// <summary>Rebuilds his rooms: tiles joined to his bed with no wall or shut door between, and the doors by them.</summary>
    private void ScanRooms(Entity<WFShipSquatterComponent> ent, MapGridComponent grid)
    {
        var comp = ent.Comp;
        comp.Rooms.Clear();
        comp.Doors.Clear();
        var bed = _maps.CoordinatesToTile(comp.Ship, grid, comp.Bed);
        var queue = new Queue<Vector2i>();
        var seen = new HashSet<Vector2i> { bed };
        queue.Enqueue(bed);
        while (queue.Count > 0)
        {
            var tile = queue.Dequeue();
            if (!Indoors(comp.Ship, grid, tile))
                continue;

            comp.Rooms.Add(tile);
            foreach (var step in Steps)
            {
                var next = tile + step;
                if (Math.Max(Math.Abs(next.X - bed.X), Math.Abs(next.Y - bed.Y)) <= comp.RoomRadius && seen.Add(next))
                    queue.Enqueue(next);
            }
        }

        var doors = EntityQueryEnumerator<DoorComponent, TransformComponent>();
        while (doors.MoveNext(out _, out _, out var xform))
        {
            if (xform.GridUid == comp.Ship)
                comp.Doors.Add(_maps.CoordinatesToTile(comp.Ship, grid, xform.Coordinates));
        }
    }

    private static readonly Vector2i[] Steps = { new(1, 0), new(-1, 0), new(0, 1), new(0, -1) };

    /// <summary>A floor tile with air, shut in on all four sides by floor or something air cannot pass.</summary>
    private bool Indoors(EntityUid ship, MapGridComponent grid, Vector2i tile)
    {
        if (!_maps.TryGetTileRef(ship, grid, tile, out var turf) || turf.Tile.IsEmpty
            || _atmos.IsTileAirBlocked(ship, tile, mapGridComp: grid))
            return false;

        foreach (var step in Steps)
        {
            var next = tile + step;
            if (_maps.TryGetTileRef(ship, grid, next, out var beside) && !beside.Tile.IsEmpty)
                continue;
            if (!_atmos.IsTileAirBlocked(ship, next, mapGridComp: grid))
                return false;
        }

        return true;
    }

    /// <summary>Says something from a list.</summary>
    private void Say(EntityUid ent, List<LocId> lines)
    {
        if (lines.Count == 0)
            return;

        _chat.TrySendInGameICMessage(ent, Loc.GetString(_random.Pick(lines)), InGameICChatType.Speak, false);
    }

    /// <summary>Calls out at those who board or come close, one newcomer at a time and never the same person twice running.</summary>
    private void Bark(Entity<WFShipSquatterComponent> ent, TransformComponent xform, TimeSpan now)
    {
        var comp = ent.Comp;
        var here = _transform.GetMapCoordinates(xform);
        var current = new HashSet<EntityUid>();
        foreach (var other in _lookup.GetEntitiesInRange<MobStateComponent>(here, 12f))
        {
            if (other.Owner == ent.Owner || !_mobs.IsAlive(other) || !HasComp<ActorComponent>(other))
                continue;

            var aboard = Transform(other).GridUid == comp.Ship;
            if (aboard || Vector2.Distance(here.Position, _transform.GetWorldPosition(other)) <= comp.BarkRange)
                current.Add(other);
        }

        var spoke = now < comp.NextBark;
        foreach (var other in current.ToList())
        {
            if (comp.Near.Contains(other) || comp.Barked.TryGetValue(other, out var until) && now < until)
                continue;

            // Someone new waits for the gap since his last word.
            if (spoke)
            {
                current.Remove(other);
                continue;
            }

            spoke = true;
            comp.Barked[other] = now + TimeSpan.FromSeconds(comp.BarkPerPerson);
            comp.NextBark = now + TimeSpan.FromSeconds(comp.BarkGap);
            Say(ent, Transform(other).GridUid == comp.Ship ? comp.BoardBarks : comp.NearBarks);
        }

        comp.Near = current;
    }
}
