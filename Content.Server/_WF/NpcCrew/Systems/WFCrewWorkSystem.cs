using System.Linq;
using System.Numerics;
using Content.Server._WF.NpcCrew.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Cargo.Systems;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Server.Shuttles.Components;
using Content.Shared._WF.NpcCrew;
using Content.Shared.Clothing.Components;
using Content.Shared.Damage;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Maps;
using Content.Shared.Mobs.Systems;
using Content.Shared.Mobs.Components;
using Content.Shared._Mono.ShipRepair.Components;
using Content.Shared.Physics;
using Content.Shared.Repairable;
using Content.Shared.Shuttles.Components;
using Content.Shared.Stacks;
using Content.Shared.Tag;
using Content.Shared.Tools.Components;
using Content.Shared.Tools.Systems;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.NpcCrew.Systems;

/// <summary>Assigns physical repair and cargo jobs to deck crew using normal hands, tools and walking.</summary>
public sealed partial class WFCrewWorkSystem : EntitySystem
{
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private SharedToolSystem _tools = default!;
    [Dependency] private WFCrewWeaponSystem _weapons = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private WFCrewEvaSystem _eva = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private Robust.Shared.Containers.SharedContainerSystem _containers = default!;
    [Dependency] private WFCrewPlannerSystem _planner = default!;
    [Dependency] private PricingSystem _pricing = default!;
    [Dependency] private AtmosphereSystem _atmos = default!;
    [Dependency] private TagSystem _tags = default!;
    [Dependency] private TurfSystem _turf = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    private readonly Dictionary<EntityUid, Job> _jobs = new();
    private readonly Dictionary<(EntityUid Grid, string Group), Skipped> _skipped = new();
    private readonly HashSet<Entity<DockingComponent>> _docks = new();
    private readonly List<EntityUid> _workers = new();
    private readonly List<(EntityUid Item, EntityUid Place)> _lootCandidates = new();
    private float _timer;

    /// <summary>How long a worker may be away from a home grid it is not docked to before the job is dropped.</summary>
    private static readonly TimeSpan StrandedAfter = TimeSpan.FromSeconds(10);

    /// <summary>How long each leg of a job, out to the target and back with the cargo, may take before it is given up.</summary>
    private static readonly TimeSpan LegTime = TimeSpan.FromSeconds(120);

    /// <summary>How long a worker free to work may get no nearer to where he is walking before the target is given up.</summary>
    private static readonly TimeSpan NoProgress = TimeSpan.FromSeconds(10);

    /// <summary>How close the HTN walks a worker to his destination; the work operator's MoveTo uses InteractRange.</summary>
    private const float Reach = SharedInteractionSystem.InteractionRange;

    /// <summary>Where a worker can stand to reach something: its own tile or one beside it.</summary>
    private static readonly Vector2i[] StandingTiles =
    {
        new(0, 0), new(1, 0), new(-1, 0), new(0, 1), new(0, -1),
    };

    /// <summary>Incapacitated or dead workers cannot continue their assigned interaction.</summary>
    public void CancelWorker(EntityUid worker)
    {
        if (_jobs.TryGetValue(worker, out var job))
            EndJob(worker, job);
    }

    /// <summary>Whether the crewman currently holds a work job.</summary>
    public bool HasJob(EntityUid worker) => _jobs.ContainsKey(worker);

    /// <summary>Workers collecting cargo remain listed with their ship while visiting the supply grid.</summary>
    public EntityUid? HomeGrid(EntityUid worker)
    {
        if (!_jobs.TryGetValue(worker, out var job))
            return null;
        if (TerminatingOrDeleted(job.Grid) || TerminatingOrDeleted(worker))
        {
            EndJob(worker, job);
            return null;
        }
        return job.Grid;
    }

    /// <summary>Stops assigning work when the corresponding order is cancelled or paused.</summary>
    public void Cancel(EntityUid grid, string group)
    {
        foreach (var (worker, job) in _jobs.ToArray())
        {
            if (job.Grid == grid && job.Group == group)
                EndJob(worker, job);
        }
        _skipped.Remove((grid, group));
        _looted.Remove((grid, group));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        _timer += frameTime;
        if (_timer < 1f)
            return;
        _timer = 0;
        TendPower();
        foreach (var (worker, job) in _jobs.ToArray())
        {
            if (TerminatingOrDeleted(worker) || TerminatingOrDeleted(job.Grid) || !_mobs.IsAlive(worker)
                || HasComp<ActorComponent>(worker))
                EndJob(worker, job);
            else if (job.Failed)
                Release(worker, job);
            else if (_timing.CurTime - job.Started > LegTime)
            {
                // Overdue while the worker was kept from it, say by a fight: the order moves on without it.
                Fail(job, true);
                Release(worker, job);
            }
            else
            {
                CheckStranded(worker, job);
                if (_jobs.ContainsKey(worker))
                    CheckProgress(worker, job);
            }
        }
        foreach (var (key, skipped) in _skipped.ToArray())
        {
            if (TerminatingOrDeleted(key.Grid))
                _skipped.Remove(key);
            else
                skipped.Entities.RemoveWhere(uid => TerminatingOrDeleted(uid));
        }
        foreach (var key in _looted.Keys.ToArray())
        {
            if (TerminatingOrDeleted(key.Grid))
                _looted.Remove(key);
        }
    }

    /// <summary>Assigns the next job, returning a localized status key suffix or complete.</summary>
    public string Advance(EntityUid grid, string group, WFCrewObjectiveKind kind, EntityUid source)
    {
        foreach (var (worker, job) in _jobs.ToArray())
        {
            if (job.Grid != grid || job.Group != group)
                continue;
            if (TerminatingOrDeleted(worker) || !_mobs.IsAlive(worker) || HasComp<ActorComponent>(worker))
            {
                EndJob(worker, job);
                continue;
            }
            if (!job.Failed)
                return "working";
            Release(worker, job);
        }

        // A raid that has taken enough, run long enough or lost its prey takes what it has and goes.
        LootState? raid = null;
        if (kind == WFCrewObjectiveKind.Loot)
        {
            raid = RaidFor(grid, group);
            if (TerminatingOrDeleted(source) || raid.Taken >= LootLimit || _timing.CurTime - raid.Started >= LootTime)
                return Complete(grid, group);
        }

        _workers.Clear();
        var handsLeft = false;
        var crewQuery = EntityQueryEnumerator<WFCrewComponent, TransformComponent>();
        while (crewQuery.MoveNext(out var uid, out var crew, out var xform))
        {
            if (crew.Group != group || crew.Role != WFCrewRoles.Deckhand || !_mobs.IsAlive(uid) || HasComp<ActorComponent>(uid))
                continue;
            handsLeft = true;
            if (xform.GridUid == grid && (crew.Post?.EntityId ?? xform.GridUid) == grid
                && crew.Duty == WFCrewDuties.Guard && !_weapons.HasLiveThreat(uid)
                && !EntityManager.System<WFCrewShelterSystem>().IsSheltering(uid))
                _workers.Add(uid);
        }
        if (_workers.Count == 0)
        {
            if (raid == null)
                return "no-worker";
            // Hands still on the prey are waited for a while; with none left alive the raid is over.
            raid.IdleSince ??= _timing.CurTime;
            return !handsLeft || _timing.CurTime - raid.IdleSince.Value >= LootIdleLimit ? Complete(grid, group) : "no-worker";
        }
        if (raid != null)
            raid.IdleSince = null;

        // Looters go through a docked port in whatever they are wearing; other work needs a hand ready for EVA.
        EntityUid? ready = null;
        if (kind == WFCrewObjectiveKind.Loot)
            ready = _workers[0];
        else
        {
            foreach (var worker in _workers)
            {
                if (!_eva.Prepare(worker))
                    continue;
                ready = worker;
                break;
            }
        }
        var mob = ready ?? _workers[0];

        var home = Comp<WFCrewComponent>(mob).Post ?? Transform(mob).Coordinates;
        var skipped = SkippedFor(grid, group, kind);
        var blocked = false;
        if (kind == WFCrewObjectiveKind.Repair)
        {
            foreach (var missing in MissingStructure(grid, mob))
            {
                if (missing is not { } at)
                    continue;
                if (skipped.Coordinates.Contains(at))
                {
                    blocked = true;
                    continue;
                }
                if (ready == null)
                    return NoEva(mob);
                _jobs[mob] = new Job(grid, group, kind, grid, home, null, _timing.CurTime) { SrdCoordinates = at };
                return "working";
            }
            var noTool = false;
            var targets = EntityQueryEnumerator<RepairableComponent, DamageableComponent, TransformComponent>();
            while (targets.MoveNext(out var target, out var repair, out var damage, out var xform))
            {
                if (xform.GridUid != grid || damage.TotalDamage <= 0 || HasComp<MobStateComponent>(target))
                    continue;
                if (skipped.Entities.Contains(target))
                {
                    blocked = true;
                    continue;
                }
                var tool = FindTool(mob, repair);
                if (tool == null)
                {
                    noTool = true;
                    continue;
                }
                if (ready == null)
                    return NoEva(mob);
                _jobs[mob] = new Job(grid, group, kind, target, home, tool, _timing.CurTime);
                return "working";
            }
            return noTool ? "no-tool" : blocked ? "work-blocked" : Complete(grid, group);
        }

        if (TerminatingOrDeleted(source))
            return Complete(grid, group);
        // A raid takes a few things worth having and goes; it does not wait on what it gave up on.
        if (raid != null)
        {
            if (FindLoot(source, mob, skipped) is not { } loot)
                return Complete(grid, group);

            raid.Stash ??= Stash(grid);
            if (raid.Stash.Count > 0)
                home = raid.Stash[raid.Assigned % raid.Stash.Count];
            raid.Assigned++;
            _jobs[mob] = new Job(grid, group, kind, loot, home, null, _timing.CurTime);
            return "working";
        }
        var items = Transform(source).ChildEnumerator;
        while (items.MoveNext(out var item))
        {
            if (Transform(item).Anchored)
                continue;
            var wanted = kind switch
            {
                WFCrewObjectiveKind.Salvage => HasComp<StackComponent>(item),
                WFCrewObjectiveKind.Loot => HasComp<Content.Shared.Item.ItemComponent>(item),
                _ => HasComp<BallisticAmmoProviderComponent>(item) && !HasComp<GunComponent>(item) && _weapons.AmmoCount(item) > 0
                     || TryComp<Content.Shared.Atmos.Components.GasTankComponent>(item, out var tank) && _eva.IsUsableSpare(item, tank),
            };
            if (!wanted || !HasStandingRoom(item))
                continue;
            if (skipped.Entities.Contains(item))
            {
                blocked = true;
                continue;
            }
            if (ready == null)
                return NoEva(mob);
            _jobs[mob] = new Job(grid, group, kind, item, home, null, _timing.CurTime);
            return "working";
        }
        return blocked ? "work-blocked" : Complete(grid, group);
    }

    /// <summary>How many things a looting crew carries off before it has enough.</summary>
    private const int LootLimit = 4;

    /// <summary>How long a raid keeps sending hands out, and how long it waits for one to be free.</summary>
    private static readonly TimeSpan LootTime = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan LootIdleLimit = TimeSpan.FromSeconds(30);

    /// <summary>Most things a looter weighs up per trip, since pricing a full locker is not free.</summary>
    private const int LootCandidates = 128;

    /// <summary>Below this nothing is worth a trip; clothing has to be worth a good deal more.</summary>
    private const double LootFloor = 50;
    private const double ClothingFloor = 250;

    /// <summary>How many tiles clear of every docking port loot is put down.</summary>
    private const int StashClearance = 3;

    private static readonly ProtoId<TagPrototype> BedsheetTag = "Bedsheet";
    private static readonly ProtoId<TagPrototype> TrashTag = "Trash";

    private readonly Dictionary<(EntityUid Grid, string Group), LootState> _looted = new();

    private string Complete(EntityUid grid, string group)
    {
        _looted.Remove((grid, group));
        _skipped.Remove((grid, group));
        return "complete";
    }

    /// <summary>Work is waiting but no hand is ready for EVA: the first goes looking for a spare tank.</summary>
    private string NoEva(EntityUid mob)
    {
        _eva.SeekSpare(mob);
        return "no-eva";
    }

    private LootState RaidFor(EntityUid grid, string group)
    {
        if (!_looted.TryGetValue((grid, group), out var raid))
            _looted[(grid, group)] = raid = new LootState(_timing.CurTime);
        return raid;
    }

    private Skipped SkippedFor(EntityUid grid, string group, WFCrewObjectiveKind kind)
    {
        if (!_skipped.TryGetValue((grid, group), out var skipped) || skipped.Kind != kind)
            _skipped[(grid, group)] = skipped = new Skipped(kind);
        return skipped;
    }

    /// <summary>Ends a job, blacklisting its target for the order when the job failed through no fault of the worker's kit.</summary>
    private void Release(EntityUid worker, Job job)
    {
        if (job.Blame)
        {
            var skipped = SkippedFor(job.Grid, job.Group, job.Kind);
            if (job.SrdCoordinates is { } at)
                skipped.Coordinates.Add(at);
            else
                skipped.Entities.Add(job.Target);
        }
        EndJob(worker, job);
    }

    /// <summary>The single exit for a job: drops carried cargo and switches the welder off.</summary>
    private void EndJob(EntityUid worker, Job job)
    {
        _jobs.Remove(worker);
        if (TerminatingOrDeleted(worker))
            return;
        if (job.Returning && !TerminatingOrDeleted(job.Target))
            _hands.TryDrop(worker, job.Target);
        if (job.Tool is { } tool && !TerminatingOrDeleted(tool) && TryComp<WelderComponent>(tool, out var welder) && welder.Enabled)
            _tools.TurnOff((tool, welder), worker);
    }

    /// <summary>Drops a job whose worker has been off the home grid, with that grid undocked from where they stand, for too long.</summary>
    private void CheckStranded(EntityUid worker, Job job)
    {
        var here = Transform(worker).GridUid;
        if (here == job.Grid || here is { } grid && DockedTo(job.Grid, grid))
        {
            job.StrandedSince = null;
            return;
        }
        job.StrandedSince ??= _timing.CurTime;
        if (_timing.CurTime - job.StrandedSince.Value >= StrandedAfter)
            EndJob(worker, job);
    }

    /// <summary>
    /// Gives a job up when its worker, free to get on with it, has come no nearer to where he is walking for a while:
    /// stuck against a railing or a table, or with no way there at all. The target is skipped for the next pick.
    /// </summary>
    private void CheckProgress(EntityUid worker, Job job)
    {
        var now = _timing.CurTime;
        // Fighting, fetching air or asleep is not being stuck.
        if (job.Failed || _weapons.HasLiveThreat(worker) || _eva.TryGetSafetyDestination(worker, out _)
            || TryComp<HTNComponent>(worker, out var htn) && !htn.Enabled
            || LegTarget(worker, job) is not { } leg)
        {
            job.Waypoint = null;
            job.ProgressAt = now;
            return;
        }

        var waypoint = Waypoint(worker, leg, Reach);
        var at = _transform.GetMapCoordinates(worker);
        var to = _transform.ToMapCoordinates(waypoint);
        var distance = at.MapId == to.MapId ? (to.Position - at.Position).Length() : float.MaxValue;
        // Path nodes used up count only while he is steering for this waypoint, not back to his post.
        var steps = TryComp<NPCSteeringComponent>(worker, out var steering) && SamePlace(steering.Coordinates, waypoint)
            ? steering.CurrentPath.Count
            : -1;
        var newWaypoint = job.Waypoint is not { } last || !SamePlace(last, waypoint);
        // At the leg's end and able to act on it: working, not stuck.
        var working = waypoint.Equals(leg) && distance <= Reach + 0.1f && CanAct(worker, job, leg);
        if (newWaypoint || working || distance < job.Best - 0.5f
            || steps >= 0 && job.Steps >= 0 && steps < job.Steps)
        {
            job.ProgressAt = now;
            job.Best = newWaypoint ? distance : MathF.Min(job.Best, distance);
        }

        job.Waypoint = waypoint;
        job.Steps = steps;
        if (now - job.ProgressAt >= NoProgress)
            Fail(job, true);
    }

    /// <summary>Two points on the same parent within half a metre of each other.</summary>
    private static bool SamePlace(EntityCoordinates a, EntityCoordinates b)
    {
        return a.EntityId == b.EntityId && (a.Position - b.Position).LengthSquared() <= 0.25f;
    }

    /// <summary>Where the job's current leg ends, without failing it; null once its target is gone.</summary>
    private EntityCoordinates? LegTarget(EntityUid worker, Job job)
    {
        if (job.Returning)
            return job.Home;
        if (job.SrdCoordinates is { } repair)
            return repair;
        var target = job.Tool is { } tool && tool != worker && !_hands.IsHolding(worker, tool, out _) ? tool : job.Target;
        return TerminatingOrDeleted(target) ? null : Transform(Holder(target, worker) ?? target).Coordinates;
    }

    /// <summary>Whether a worker at the end of a leg can act there, as <see cref="Perform"/> requires.</summary>
    private bool CanAct(EntityUid worker, Job job, EntityCoordinates leg)
    {
        if (job.SrdCoordinates != null || job.Returning)
            return _interaction.InRangeUnobstructed(worker, leg, range: Reach);
        var target = job.Tool is { } tool && tool != worker && !_hands.IsHolding(worker, tool, out _) ? tool : job.Target;
        return !TerminatingOrDeleted(target) && _interaction.InRangeUnobstructed(worker, Holder(target, worker) ?? target, range: Reach);
    }

    /// <summary>Whether a worker could stand beside something to take it: its own tile or one next to it is clear deck.</summary>
    private bool HasStandingRoom(EntityUid place)
    {
        var xform = Transform(place);
        if (xform.GridUid is not { } grid || !TryComp<MapGridComponent>(grid, out var map))
            return false;

        var tile = _maps.TileIndicesFor(grid, map, xform.Coordinates);
        foreach (var offset in StandingTiles)
        {
            var at = tile + offset;
            if (_maps.TryGetTileRef(grid, map, at, out var tileRef) && !tileRef.Tile.IsEmpty
                && !_turf.IsTileBlocked(grid, at, CollisionGroup.MobMask, map))
                return true;
        }

        return false;
    }

    private bool DockedTo(EntityUid grid, EntityUid other)
    {
        _docks.Clear();
        _lookup.GetChildEntities(grid, _docks);
        foreach (var dock in _docks)
        {
            if (dock.Comp.DockedWith is { } with && Transform(with).GridUid == other)
                return true;
        }
        return false;
    }

    /// <summary>Only a tool the crewman carries or has built in counts; loose tools on deck belong to whoever left them.</summary>
    private EntityUid? FindTool(EntityUid mob, RepairableComponent repair)
    {
        if (repair.Qualities.Any(quality => _tools.HasQuality(mob, quality)))
            return mob;
        foreach (var held in _hands.EnumerateHeld(mob))
        {
            if (repair.Qualities.Any(quality => _tools.HasQuality(held, quality)))
                return held;
        }
        return null;
    }

    /// <summary>
    /// Provides the next place to walk to without moving the worker or the cargo: the leg's destination, or on the
    /// way to another grid docked to his, the next step through the docking ports.
    /// </summary>
    public bool Destination(EntityUid mob, out EntityCoordinates coordinates)
    {
        if (!LegDestination(mob, out coordinates))
            return false;
        coordinates = Waypoint(mob, coordinates, Reach);
        return true;
    }

    /// <summary>
    /// The next place to walk to on the way to a destination on another grid docked to this one: this grid's port,
    /// then a point just inside the other grid past its port, so each leg is pathed on one grid instead of walked in
    /// a straight line into whatever stands between. Anywhere else the destination itself.
    /// </summary>
    public EntityCoordinates Waypoint(EntityUid mob, EntityCoordinates destination, float reach)
    {
        if (Transform(mob).GridUid is not { } here || _transform.GetGrid(destination) is not { } there || here == there)
            return destination;

        var position = _transform.GetWorldPosition(mob);
        EntityUid? ours = null;
        EntityUid? theirs = null;
        var best = float.MaxValue;
        _docks.Clear();
        _lookup.GetChildEntities(here, _docks);
        foreach (var dock in _docks)
        {
            if (dock.Comp.DockedWith is not { } mate || TerminatingOrDeleted(mate) || Transform(mate).GridUid != there)
                continue;
            var distance = (_transform.GetWorldPosition(dock.Owner) - position).LengthSquared();
            if (distance >= best)
                continue;
            best = distance;
            ours = dock.Owner;
            theirs = mate;
        }

        if (ours is not { } port || theirs is not { } other)
            return destination;
        // Walk to this side's port first.
        if (best > (reach + 0.5f) * (reach + 0.5f))
            return Transform(port).Coordinates;

        // Then across: far enough past the other port that arriving there means standing on the other grid.
        var from = _transform.GetWorldPosition(port);
        var to = _transform.GetWorldPosition(other);
        var axis = to - from;
        if (axis.LengthSquared() < 0.01f)
            return Transform(other).Coordinates;
        var inside = to + Vector2.Normalize(axis) * reach;
        return _transform.ToCoordinates(there, new MapCoordinates(inside, Transform(there).MapID));
    }

    /// <summary>The current leg's destination, failing the job when its time or the worker's air runs out.</summary>
    private bool LegDestination(EntityUid mob, out EntityCoordinates coordinates)
    {
        if (_eva.TryGetSafetyDestination(mob, out coordinates))
            return true;
        coordinates = default;
        if (!_jobs.TryGetValue(mob, out var job) || job.Failed || HasComp<ActorComponent>(mob) || !_mobs.IsAlive(mob))
            return false;
        // Cargo carried home through breathable air needs no suit; anywhere else the worker must stay ready for EVA.
        if (job.Kind != WFCrewObjectiveKind.Loot && job.Deliver == null && !(job.Returning && InSafeAir(mob)) && !_eva.Prepare(mob))
        {
            Fail(job, false);
            return false;
        }
        if (_timing.CurTime - job.Started > LegTime)
        {
            Fail(job, true);
            return false;
        }
        if (job.Returning)
        {
            if (job.Deliver is { } machine)
            {
                if (TerminatingOrDeleted(machine))
                {
                    _jobs.Remove(mob);
                    return false;
                }

                coordinates = Transform(machine).Coordinates;
                return true;
            }

            coordinates = job.Home;
            return true;
        }
        if (job.SrdCoordinates is { } repairCoordinates)
        {
            if (!MissingStructure(job.Grid, mob, repairCoordinates).Any())
            {
                _jobs.Remove(mob);
                return false;
            }
            coordinates = repairCoordinates;
            return true;
        }
        var target = job.Tool is { } tool && tool != mob && !_hands.IsHolding(mob, tool, out _) ? tool : job.Target;
        if (TerminatingOrDeleted(target))
        {
            _jobs.Remove(mob);
            return false;
        }
        // Something in a locker or crate is fetched from where the locker stands.
        coordinates = Transform(Holder(target, mob) ?? target).Coordinates;
        return true;
    }

    /// <summary>Performs a job step only after ordinary navigation reaches its interaction range.</summary>
    public bool Perform(EntityUid mob)
    {
        // A waypoint on the way is only walked to; the job is done at the leg's own destination.
        if (!_jobs.TryGetValue(mob, out var job) || !LegDestination(mob, out var destination))
            return true;
        EntityManager.System<WFCrewSpeechSystem>().Say(mob,
            job.SrdCoordinates != null || job.Tool != null ? "repair" : job.Returning ? "return-cargo" : "collect");
        if (!Transform(mob).Coordinates.InRange(EntityManager, destination, 1.5f))
            return true;
        if (job.SrdCoordinates != null || job.Returning)
        {
            if (!_interaction.InRangeUnobstructed(mob, destination, range: 1.5f))
                return true;
        }
        else
        {
            var target = job.Tool is { } needed && needed != mob && !_hands.IsHolding(mob, needed, out _) ? needed : job.Target;
            if (!_interaction.InRangeUnobstructed(mob, Holder(target, mob) ?? target, range: 1.5f))
                return true;
        }
        if (job.Deliver is { } machine && job.Returning)
            return Tend(mob, job, machine);
        if (job.SrdCoordinates is { } repairCoordinates)
        {
            if (_timing.CurTime >= job.NextUse)
            {
                RaiseLocalEvent(mob, new AfterInteractEvent(mob, mob, null, repairCoordinates, true));
                job.NextUse = _timing.CurTime + TimeSpan.FromSeconds(2);
            }
            return false;
        }
        if (job.Returning)
        {
            if (_hands.TryDrop(mob, job.Target))
                _jobs.Remove(mob);
            else
                Fail(job, true);
            return true;
        }
        if (job.Tool is { } tool)
        {
            if (tool != mob && !_hands.IsHolding(mob, tool, out _))
            {
                if (!_hands.TryPickupAnyHand(mob, tool))
                    Fail(job, true);
                return true;
            }
            if (!TryComp<DamageableComponent>(job.Target, out var damage) || damage.TotalDamage <= 0)
            {
                if (TryComp<WelderComponent>(tool, out var doneWelder))
                    _tools.TurnOff((tool, doneWelder), mob);
                _jobs.Remove(mob);
                return true;
            }
            if (_timing.CurTime < job.NextUse)
                return false;
            if (TryComp<WelderComponent>(tool, out var welder) && !welder.Enabled)
                _tools.TurnOn((tool, welder), mob);
            if (tool == mob)
            {
                var use = new InteractUsingEvent(mob, mob, job.Target, Transform(job.Target).Coordinates);
                RaiseLocalEvent(job.Target, use);
            }
            else
            {
                _hands.TrySelect(mob, tool);
                _interaction.InteractUsing(mob, tool, job.Target, Transform(job.Target).Coordinates);
            }
            job.NextUse = _timing.CurTime + TimeSpan.FromSeconds(Comp<RepairableComponent>(job.Target).DoAfterDelay + 2);
            return false;
        }
        // A looter rifles lockers and crates: the thing is pulled out before it is picked up.
        if ((job.Kind == WFCrewObjectiveKind.Loot || job.Deliver != null) && Holder(job.Target, mob) != null
            && _containers.TryGetContainingContainer(job.Target, out var holding))
            _containers.Remove(job.Target, holding);
        if (!_hands.TryPickupAnyHand(mob, job.Target))
        {
            Fail(job, true);
            return true;
        }
        job.Returning = true;
        // The way home has its own time limit.
        job.Started = _timing.CurTime;
        if (job.Kind == WFCrewObjectiveKind.Loot && _looted.TryGetValue((job.Grid, job.Group), out var raid))
            raid.Taken++;
        return true;
    }

    /// <summary>The locker, crate or other container an item lies in, unless the worker himself holds it.</summary>
    private EntityUid? Holder(EntityUid item, EntityUid worker)
    {
        return _containers.TryGetContainingContainer(item, out var container) && container.Owner != worker
            ? container.Owner
            : null;
    }

    private bool Claimed(EntityUid item)
    {
        foreach (var job in _jobs.Values)
        {
            if (job.Target == item)
                return true;
        }

        return false;
    }

    /// <summary>
    /// The most valuable thing aboard a ship worth stealing: loose on the deck or in a locker or crate, not given up
    /// on, and not standing in bad air when the looter is in good air.
    /// </summary>
    private EntityUid? FindLoot(EntityUid source, EntityUid looter, Skipped skipped)
    {
        _lootCandidates.Clear();
        var children = Transform(source).ChildEnumerator;
        while (children.MoveNext(out var child) && _lootCandidates.Count < LootCandidates)
        {
            if (HasComp<Content.Shared.Item.ItemComponent>(child))
            {
                if (!Transform(child).Anchored)
                    _lootCandidates.Add((child, child));
                continue;
            }

            if (!TryComp<Content.Server.Storage.Components.EntityStorageComponent>(child, out var storage))
                continue;

            // A locked or welded crate keeps what is in it.
            if (TryComp<Content.Shared.Lock.LockComponent>(child, out var crateLock) && crateLock.Locked
                || TryComp<WeldableComponent>(child, out var weld) && weld.IsWelded)
                continue;

            foreach (var inside in storage.Contents.ContainedEntities)
            {
                if (_lootCandidates.Count >= LootCandidates)
                    break;
                if (HasComp<Content.Shared.Item.ItemComponent>(inside))
                    _lootCandidates.Add((inside, child));
            }
        }

        var inAir = InSafeAir(looter);
        var worth = new List<(EntityUid Item, EntityUid Place, double Price)>();
        foreach (var (item, place) in _lootCandidates)
        {
            if (Claimed(item) || skipped.Entities.Contains(item) || inAir && !InSafeAir(place))
                continue;
            var price = _pricing.GetPrice(item);
            if (IsClutter(item, price))
                continue;
            worth.Add((item, place, price));
        }

        // The dearest thing he can get to: something walled in or crammed in where nobody can stand is passed over.
        worth.Sort((a, b) => b.Price.CompareTo(a.Price));
        foreach (var (item, place, _) in worth)
        {
            if (HasStandingRoom(place))
                return item;
        }

        return null;
    }

    /// <summary>Bedding, trash, cheap clothes and anything else not worth carrying off.</summary>
    private bool IsClutter(EntityUid item, double price)
    {
        return price < LootFloor || _tags.HasAnyTag(item, BedsheetTag, TrashTag)
            || price < ClothingFloor && HasComp<ClothingComponent>(item);
    }

    /// <summary>Whether the air where something stands can be breathed without a suit, or he is immune to bad air.</summary>
    private bool InSafeAir(EntityUid uid)
    {
        if (_eva.IsImmune(uid))
            return true;
        var air = _atmos.GetTileMixture((uid, Transform(uid)));
        return air != null && _atmos.IsMixtureProbablySafe(air) && WFCrewPlannerSystem.IsBreathable(air, air.Pressure);
    }

    /// <summary>
    /// Where a raid puts its loot down aboard its own ship: the hold, clear of the airlocks, or failing that the open
    /// deck tile farthest from every docking port. Empty on a ship with neither, which keeps the worker's post.
    /// </summary>
    private List<EntityCoordinates> Stash(EntityUid grid)
    {
        var stash = new List<EntityCoordinates>();
        if (!TryComp<MapGridComponent>(grid, out var map))
            return stash;

        var ports = new List<Vector2i>();
        _docks.Clear();
        _lookup.GetChildEntities(grid, _docks);
        foreach (var dock in _docks)
        {
            ports.Add(_maps.TileIndicesFor(grid, map, Transform(dock).Coordinates));
        }

        foreach (var tile in _planner.HoldTiles(grid, LootLimit))
        {
            var at = _maps.TileIndicesFor(grid, map, tile);
            if (ports.TrueForAll(port => Chebyshev(port, at) >= StashClearance))
                stash.Add(tile);
        }

        if (stash.Count > 0 || ports.Count == 0)
            return stash;

        // A small ship's hold is by its airlock: take the deck farthest from every port, breathable deck first.
        Vector2i? best = null;
        var bestDistance = -1;
        var bestSafe = false;
        var tiles = _maps.GetAllTilesEnumerator(grid, map);
        while (tiles.MoveNext(out var tile))
        {
            var at = tile.Value.GridIndices;
            if (_turf.IsTileBlocked(grid, at, CollisionGroup.MobMask, map))
                continue;
            var safe = _planner.IsSafePost(grid, at, map);
            var distance = int.MaxValue;
            foreach (var port in ports)
            {
                distance = Math.Min(distance, Chebyshev(port, at));
            }
            if (bestSafe && !safe || safe == bestSafe && distance <= bestDistance)
                continue;
            best = at;
            bestDistance = distance;
            bestSafe = safe;
        }

        if (best is { } far)
            stash.Add(_maps.GridTileToLocal(grid, map, far));
        return stash;
    }

    private static int Chebyshev(Vector2i a, Vector2i b)
    {
        return Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
    }

    /// <summary>Marks a job failed; a blamed failure also keeps the order from picking the same target again.</summary>
    private static void Fail(Job job, bool blame)
    {
        job.Failed = true;
        job.Blame |= blame;
    }

    /// <summary>A raid under way: what it has taken, where it puts things down, and how long it has run.</summary>
    private sealed class LootState(TimeSpan started)
    {
        public readonly TimeSpan Started = started;
        public int Taken;
        public int Assigned;
        public TimeSpan? IdleSince;
        public List<EntityCoordinates>? Stash;
    }

    /// <summary>Targets an order gave up on, so the next pick moves on instead of repeating them.</summary>
    private sealed class Skipped(WFCrewObjectiveKind kind)
    {
        public readonly WFCrewObjectiveKind Kind = kind;
        public readonly HashSet<EntityCoordinates> Coordinates = new();
        public readonly HashSet<EntityUid> Entities = new();
    }

    private sealed class Job(EntityUid grid, string group, WFCrewObjectiveKind kind, EntityUid target,
        EntityCoordinates home, EntityUid? tool, TimeSpan started)
    {
        public readonly EntityUid Grid = grid;
        public readonly string Group = group;
        public readonly WFCrewObjectiveKind Kind = kind;
        public readonly EntityUid Target = target;
        public readonly EntityUid? Tool = tool;
        public readonly EntityCoordinates Home = home;
        /// <summary>When the current leg began: out to the target, then home with the cargo.</summary>
        public TimeSpan Started = started;
        public TimeSpan NextUse;
        public TimeSpan? StrandedSince;
        public EntityCoordinates? SrdCoordinates;
        public bool Returning;
        public bool Failed;
        public bool Blame;

        /// <summary>A machine the cargo is fed to instead of being carried home: the engineer's fuel runs.</summary>
        public EntityUid? Deliver;

        /// <summary>Progress toward the place being walked to: it, the nearest the worker got, his path's length and when he last gained.</summary>
        public EntityCoordinates? Waypoint;
        public float Best = float.MaxValue;
        public int Steps = -1;
        public TimeSpan ProgressAt;
    }
}
