using System.Linq;
using System.Numerics;
using Content.Server._WF.NpcCrew.Components;
using Content.Server.NPC.HTN;
using Content.Shared._WF.NpcCrew;
using Content.Shared.Mobs.Systems;
using Content.Shared._Mono.Company;
using Content.Shared.NPC.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;

namespace Content.Server._WF.NpcCrew.Systems;

/// <summary>Runs editable objective queues for each ship and crew group, independently of player proximity.</summary>
public sealed partial class WFCrewObjectiveSystem : EntitySystem
{
    [Dependency] private WFPilotDutySystem _pilots = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private WFCrewWorkSystem _work = default!;
    [Dependency] private WFCaptainSystem _captains = default!;
    [Dependency] private WFCrewSystem _crew = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    /// <summary>Seconds a travel order may take beyond twice the straight flight at cruise speed.</summary>
    private const float TravelSlack = 120f;

    private readonly Dictionary<(EntityUid Grid, string Group), QueueState> _queues = new();
    private float _timer;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFPilotDockFailedEvent>(OnDockFailed);
    }

    private void OnDockFailed(ref WFPilotDockFailedEvent ev)
    {
        if (TryComp<WFCrewComponent>(ev.Mob, out var crew)
            && _queues.TryGetValue((ev.Grid, crew.Group), out var state))
        {
            state.Paused = true;
            state.Status = "dock-failed";
        }
    }

    /// <summary>Validates the entire replacement or append before altering the running queue.</summary>
    public bool SetQueue(EntityUid grid, string group, List<WFCrewObjective> objectives, bool append = false)
    {
        if (!HasComp<MapGridComponent>(grid) || Transform(grid).MapUid == null || group.Length > 64 || objectives.Count > 64
            || objectives.Any(item => !Valid(grid, item)))
            return false;
        var key = (grid, group);
        if (!_queues.TryGetValue(key, out var state))
            state = new QueueState();
        if (append && state.Items.Count + objectives.Count > 64)
            return false;
        if (!append)
        {
            HoldCrew(grid, group);
            state = new QueueState();
        }
        state.Items.AddRange(objectives);
        _queues[key] = state;
        UpdateEscort(grid, group, state);
        return true;
    }

    private bool Valid(EntityUid grid, WFCrewObjective item)
    {
        if (!Enum.IsDefined(item.Kind) || !float.IsFinite(item.Range) || item.Range is < 1 or > 5000
            || !float.IsFinite(item.Duration) || item.Duration is < 0 or > 86400
            || !float.IsFinite(item.Position.X) || !float.IsFinite(item.Position.Y))
            return false;
        if (item.Kind is WFCrewObjectiveKind.Hold or WFCrewObjectiveKind.GoTo or WFCrewObjectiveKind.Undock or WFCrewObjectiveKind.Repair)
            return true;
        return item.Target is { } net && TryGetEntity(net, out var target) && target is { } uid && uid != grid
            && HasComp<MapGridComponent>(uid) && Transform(uid).MapUid == Transform(grid).MapUid;
    }

    /// <summary>The raw status of a crew's queue, such as running, dock-failed or target-lost; null without a queue.</summary>
    public string? QueueStatus(EntityUid grid, string group)
    {
        return _queues.TryGetValue((grid, group), out var state) ? state.Status : null;
    }

    /// <summary>Pauses, resumes or skips the current task without changing later tasks.</summary>
    public void Control(EntityUid grid, string group, WFCrewSetupAction action)
    {
        if (!_queues.TryGetValue((grid, group), out var state))
            return;
        HoldCrew(grid, group);
        state.Started = false;
        state.Pilot = null;
        if (action == WFCrewSetupAction.Skip && state.Items.Count > 0)
        {
            state.Items.RemoveAt(0);
            state.Elapsed = 0;
        }
        state.Paused = action == WFCrewSetupAction.Pause;
        state.Status = state.Paused ? "paused" : "pending";
        UpdateEscort(grid, group, state);
    }

    /// <summary>Cancels queued orders when an admin issues an immediate mission or clears the crew.</summary>
    public void Cancel(EntityUid grid, string group)
    {
        _work.Cancel(grid, group);
        _queues.Remove((grid, group));
        EntityManager.System<WFCrewEscortSystem>().Clear(grid, group);
    }

    /// <summary>Whether an admin explicitly ordered this crew to engage a grid.</summary>
    public bool IsAttackTarget(EntityUid grid, string group, EntityUid target)
    {
        return _queues.TryGetValue((grid, group), out var state) && !state.Paused && state.Items.Count > 0
            && state.Items[0].Kind == WFCrewObjectiveKind.Attack && state.Items[0].Target == GetNetEntity(target);
    }

    /// <summary>Whether this crew's running order is a raid on the given grid.</summary>
    public bool IsRaiding(EntityUid grid, string group, EntityUid prey)
    {
        return _queues.TryGetValue((grid, group), out var state) && !state.Paused && state.Items.Count > 0
            && state.Items[0].Kind == WFCrewObjectiveKind.Loot && state.Items[0].Target == GetNetEntity(prey);
    }

    /// <summary>Returns the explicitly assigned attack target while its task is running.</summary>
    public EntityUid? AttackTarget(EntityUid grid, string group)
    {
        if (_queues.TryGetValue((grid, group), out var state) && !state.Paused && state.Items.Count > 0
            && state.Items[0].Kind == WFCrewObjectiveKind.Attack && state.Items[0].Target is { } net
            && TryGetEntity(net, out var target) && target is { } uid && !TerminatingOrDeleted(uid))
            return uid;
        return null;
    }

    private EntityUid? Pilot(EntityUid grid, string group)
    {
        var query = EntityQueryEnumerator<WFCrewComponent, WFPilotDutyComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var crew, out _, out var transform))
        {
            if (transform.GridUid == grid && crew.Group == group && crew.Duty == WFCrewDuties.Pilot
                && _mobs.IsAlive(uid) && !HasComp<ActorComponent>(uid))
                return uid;
        }
        return null;
    }

    private void HoldCrew(EntityUid grid, string group)
    {
        _work.Cancel(grid, group);
        EntityManager.System<WFCrewEscortSystem>().Clear(grid, group);
        if (Pilot(grid, group) is { } pilot)
            _pilots.Hold(pilot);
    }

    private void UpdateEscort(EntityUid grid, string group, QueueState state)
    {
        var escorts = EntityManager.System<WFCrewEscortSystem>();
        if (!state.Paused && state.Items.FirstOrDefault() is { Kind: WFCrewObjectiveKind.Escort, Target: { } net }
            && TryGetEntity(net, out var target) && target is { } leader)
            escorts.SetEscort(grid, group, leader);
        else
            escorts.Clear(grid, group);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        _timer += frameTime;
        if (_timer < 0.5f)
            return;
        var elapsed = _timer;
        _timer = 0;
        foreach (var (key, state) in _queues.ToArray())
        {
            var (grid, group) = key;
            if (TerminatingOrDeleted(grid))
            {
                _queues.Remove(key);
                continue;
            }
            if (state.Paused || state.Items.Count == 0)
                continue;
            if ((Pilot(grid, group) ?? Promote(grid, group)) is not { } pilot)
            {
                state.Status = CrewAway(grid, group) ? "pilot-away" : "no-pilot";
                state.Started = false;
                continue;
            }
            // The captain owns the orders until all-clear; a target lost meanwhile is handled afterwards.
            if (_captains.IsCourseSuspended(pilot))
            {
                state.Status = "evading";
                state.Evaded = true;
                continue;
            }
            var item = state.Items[0];
            if (!Valid(grid, item))
            {
                HoldCrew(grid, group);
                state.Paused = true;
                state.Status = "target-lost";
                continue;
            }
            if (state.Evaded)
            {
                state.Evaded = false;
                // The restored course doesn't redo an arrival or docking undone by the evasion; fly the task again.
                if (item.Kind is WFCrewObjectiveKind.GoTo or WFCrewObjectiveKind.Dock or WFCrewObjectiveKind.Undock
                    or WFCrewObjectiveKind.Retreat or WFCrewObjectiveKind.Resupply or WFCrewObjectiveKind.Salvage or WFCrewObjectiveKind.Loot)
                    state.Started = false;
            }
            var target = item.Target is { } net ? GetEntity(net) : EntityUid.Invalid;
            var travels = item.Kind is WFCrewObjectiveKind.GoTo or WFCrewObjectiveKind.Undock or WFCrewObjectiveKind.Retreat;
            if (!state.Started || state.Pilot != pilot)
            {
                Start(pilot, grid, target, item);
                state.Started = true;
                state.Pilot = pilot;
                state.Status = "running";
                if (travels)
                {
                    state.Elapsed = 0;
                    state.Budget = TravelBudget(pilot, grid, target, item);
                }
            }
            var duty = Comp<WFPilotDutyComponent>(pilot);
            if (!duty.AtHelm)
            {
                state.Status = "awaiting-helm";
                continue;
            }
            state.Status = "running";
            // Docked or without thrust, a travel order waits on crew, a visitor or repairs, not on the flight.
            if (!travels || !duty.Docked && !EntityManager.System<WFCrewShipStatusSystem>().IsAdrift(grid))
                state.Elapsed += elapsed;
            if (item.Kind is WFCrewObjectiveKind.Repair or WFCrewObjectiveKind.Resupply or WFCrewObjectiveKind.Salvage or WFCrewObjectiveKind.Loot)
            {
                if (item.Kind != WFCrewObjectiveKind.Repair && !duty.OrdersCompleted)
                    continue;
                state.Status = _work.Advance(grid, group, item.Kind, target);
                if (state.Status != "complete")
                    continue;
                state.Items.RemoveAt(0);
                state.Started = false;
                state.Elapsed = 0;
                continue;
            }
            var completesOnArrival = item.Kind is WFCrewObjectiveKind.GoTo or WFCrewObjectiveKind.Dock or WFCrewObjectiveKind.Undock or WFCrewObjectiveKind.Retreat;
            // An attack is over once its target can neither move nor shoot.
            var targetDisabled = item.Kind == WFCrewObjectiveKind.Attack
                && EntityManager.System<WFCrewShipStatusSystem>().ShouldDisengage(grid, target, range: false);
            if (targetDisabled || (completesOnArrival ? duty.OrdersCompleted : item.Duration > 0 && state.Elapsed >= item.Duration))
            {
                _pilots.Hold(pilot);
                state.Items.RemoveAt(0);
                state.Started = false;
                state.Elapsed = 0;
                state.Status = state.Items.Count == 0 ? "complete" : "pending";
            }
            else if (travels && state.Elapsed >= state.Budget)
            {
                // Long overdue: the point can't be reached, so whoever runs the queue decides what comes next.
                Log.Info($"{ToPrettyString(grid)} gave up a {item.Kind} after {state.Elapsed:0} s.");
                HoldCrew(grid, group);
                state.Paused = true;
                state.Status = "target-lost";
            }
        }
    }

    /// <summary>
    /// How long a travel order may run before its destination counts as unreachable: its duration when it has one,
    /// else twice the straight flight at cruise speed plus <see cref="TravelSlack"/>.
    /// </summary>
    private float TravelBudget(EntityUid pilot, EntityUid grid, EntityUid target, WFCrewObjective item)
    {
        if (item.Duration > 0)
            return item.Duration;

        var duty = Comp<WFPilotDutyComponent>(pilot);
        var from = _transform.GetWorldPosition(grid);
        var distance = item.Kind switch
        {
            WFCrewObjectiveKind.GoTo => (item.Position - from).Length(),
            WFCrewObjectiveKind.Retreat when TryComp<MapGridComponent>(target, out var targetGrid) =>
                (_transform.ToMapCoordinates(new EntityCoordinates(target, targetGrid.LocalAABB.Center)).Position - from).Length(),
            _ => duty.DockStandoff,
        };
        return TravelSlack + 2f * distance / MathF.Max(duty.CruiseSpeed, 1f);
    }

    /// <summary>
    /// Once every pilot of the crew is down, another living crewman aboard takes over: officers who can fly first,
    /// gunners last, and the fallen pilots stand down so two never share the helm. Null when nobody can, or while a
    /// pilot is up but away or possessed.
    /// </summary>
    private EntityUid? Promote(EntityUid grid, string group)
    {
        EntityUid? successor = null;
        var bestRank = int.MaxValue;
        var fallen = new List<EntityUid>();
        var query = EntityQueryEnumerator<WFCrewComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var crew, out var xform))
        {
            if (crew.Group != group)
                continue;
            if (crew.Duty == WFCrewDuties.Pilot)
            {
                if (_crew.HomeGrid(uid, crew) != grid)
                    continue;
                if (_mobs.IsAlive(uid))
                    return null;
                fallen.Add(uid);
                continue;
            }
            if (xform.GridUid != grid || !_mobs.IsAlive(uid) || HasComp<ActorComponent>(uid)
                || crew.Engagement == WFCrewEngagement.Never
                || !TryComp<HTNComponent>(uid, out var htn) || !htn.Enabled)
                continue;

            var rank = (crew.Duty == WFCrewDuties.Gunnery ? 4 : 0)
                       + (HasComp<WFPilotDutyComponent>(uid) ? 0 : 2)
                       + (_work.HasJob(uid) ? 1 : 0);
            if (rank >= bestRank)
                continue;
            bestRank = rank;
            successor = uid;
        }
        if (successor is not { } heir)
            return null;

        var duty = EnsureComp<WFPilotDutyComponent>(heir);
        var navigation = Comp<WFCrewComponent>(heir).Navigation;
        foreach (var old in fallen)
        {
            if (TryComp<WFPilotDutyComponent>(old, out var oldDuty))
            {
                navigation = oldDuty.Navigation;
                duty.ReactToAttacks = oldDuty.ReactToAttacks;
                duty.FaceThreats = oldDuty.FaceThreats;
                duty.AbsentCrewWait = oldDuty.AbsentCrewWait;
                duty.DockMaxAttempts = oldDuty.DockMaxAttempts;
            }
            if (Comp<WFCrewComponent>(old).Post is { } helm && helm.EntityId == grid)
                _crew.SetPost(heir, helm);
            _crew.SetDuty(old, WFCrewDuties.Guard);
        }
        // A post left on another ship, such as a recalled boarder's, must not pull the new pilot off this one.
        if (Comp<WFCrewComponent>(heir).Post is not { } post || post.EntityId != grid)
            _crew.SetPost(heir, Transform(heir).Coordinates);
        _pilots.SetNavigation(heir, navigation);
        _work.CancelWorker(heir);
        _crew.SetDuty(heir, WFCrewDuties.Pilot);
        Log.Info($"{ToPrettyString(heir)} takes over as pilot of {ToPrettyString(grid)}.");
        return heir;
    }

    /// <summary>Whether a living crewman of this ship is off it, so the helm may yet be manned when he is back.</summary>
    private bool CrewAway(EntityUid grid, string group)
    {
        var query = EntityQueryEnumerator<WFCrewComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var crew, out var xform))
        {
            if (crew.Group == group && xform.GridUid != grid && !HasComp<ActorComponent>(uid) && _mobs.IsAlive(uid)
                && _crew.HomeGrid(uid, crew) == grid)
                return true;
        }

        return false;
    }

    private void Start(EntityUid pilot, EntityUid grid, EntityUid target, WFCrewObjective item)
    {
        var duty = Comp<WFPilotDutyComponent>(pilot);
        duty.ArrivalRange = item.Range;
        var targetCenter = new EntityCoordinates(target,
            TryComp<MapGridComponent>(target, out var targetGrid) ? targetGrid.LocalAABB.Center : Vector2.Zero);
        switch (item.Kind)
        {
            case WFCrewObjectiveKind.Hold: _pilots.Hold(pilot); break;
            case WFCrewObjectiveKind.Repair: _pilots.Hold(pilot); break;
            case WFCrewObjectiveKind.Resupply:
            case WFCrewObjectiveKind.Salvage: _pilots.Dock(pilot, target); break;
            case WFCrewObjectiveKind.Loot: _pilots.Dock(pilot, target); break;
            case WFCrewObjectiveKind.GoTo:
                _pilots.GoTo(pilot, new List<EntityCoordinates> { new(Transform(grid).MapUid!.Value, item.Position) }); break;
            case WFCrewObjectiveKind.Dock: _pilots.Dock(pilot, target); break;
            case WFCrewObjectiveKind.Undock: _pilots.Undock(pilot); break;
            case WFCrewObjectiveKind.Follow: _pilots.Follow(pilot, target, item.Range); break;
            case WFCrewObjectiveKind.Escort: _pilots.Escort(pilot, target, item.Range); break;
            case WFCrewObjectiveKind.Circle:
                _pilots.Loiter(pilot, targetCenter, item.Range, objective: WFCrewObjectiveKind.Circle); break;
            case WFCrewObjectiveKind.Loiter:
                _pilots.Loiter(pilot, targetCenter, item.Range); break;
            case WFCrewObjectiveKind.Attack:
                _pilots.Loiter(pilot, targetCenter, item.Range, objective: WFCrewObjectiveKind.Attack); break;
            case WFCrewObjectiveKind.Retreat:
                // Crew pilots keep clear of the destination's hull, so arrival allows for both hulls.
                duty.ArrivalRange = item.Range + _pilots.HullClearance(pilot, target);
                _pilots.GoTo(pilot, new List<EntityCoordinates> { targetCenter }); break;
        }
    }

    /// <summary>
    /// Reports all living and downed crews, including crews without queued objectives. The living make the rows; a
    /// downed crewman only counts toward a row of his living crewmates, so a dead boarder lying aboard another ship
    /// is no crew of it. A crew with nobody left alive is still listed where its men lie.
    /// </summary>
    public List<WFCrewSetupCrew> Snapshot()
    {
        var result = new Dictionary<(EntityUid Grid, string Group), WFCrewSetupCrew>();
        var living = new HashSet<(string Group, string Battlegroup)>();
        var members = new List<(EntityUid Uid, WFCrewComponent Crew, EntityUid Grid, bool Alive)>();
        var query = EntityQueryEnumerator<WFCrewComponent, TransformComponent>();
        while (query.MoveNext(out var member, out var memberCrew, out var transform))
        {
            if ((_work.HomeGrid(member) ?? transform.GridUid) is not { } home)
                continue;
            var alive = _mobs.IsAlive(member);
            if (alive)
                living.Add((memberCrew.Group, memberCrew.Battlegroup));
            members.Add((member, memberCrew, home, alive));
        }

        // The living first, so their rows exist before the downed are counted.
        foreach (var (uid, crew, grid, alive) in members.Where(entry => entry.Alive).Concat(members.Where(entry => !entry.Alive)))
        {
            var key = (grid, crew.Group);
            if (!result.TryGetValue(key, out var row))
            {
                if (!alive && crew.Battlegroup.Length > 0 && living.Contains((crew.Group, crew.Battlegroup)))
                    continue;
                row = new WFCrewSetupCrew { Grid = GetNetEntity(grid), Group = crew.Group, Status = Loc.GetString("wf-crew-objective-status-manual") };
                row.Settings.Navigation = crew.Navigation.Clone();
                if (_queues.TryGetValue(key, out var state))
                {
                    row.Objectives = state.Items.ToList();
                    row.Status = Loc.GetString($"wf-crew-objective-status-{state.Status}");
                }
                result.Add(key, row);
            }
            row.Members++;
            row.Settings.Group = crew.Group;
            row.Settings.Battlegroup = crew.Battlegroup;
            row.Settings.Disengage = crew.Disengage;
            row.Settings.Skill = crew.Skill;
            row.Settings.DisengageRange = crew.DisengageRange;
            // Reported so a settings round trip through Crew Setup does not put a raider's troubles on the air.
            if (!crew.CallsForHelp)
                row.Settings.CallsForHelp = false;
            if (TryComp<WFPilotDutyComponent>(uid, out var pilot))
            {
                row.Settings.Navigation = pilot.Navigation.Clone();
                row.Settings.FaceThreats = pilot.FaceThreats;
                if (_mobs.IsAlive(uid))
                    SetActivity(row, pilot);
            }
            if (TryComp<CompanyComponent>(uid, out var company))
                row.Settings.Company = company.CompanyName.Id;
            if (TryComp<NpcFactionMemberComponent>(uid, out var faction))
                row.Settings.Faction = faction.Factions.FirstOrDefault().Id ?? "WFCrew";
            if (TryComp<WFCrewSecurityComponent>(uid, out var security))
            {
                row.Settings.BoardingResponse = security.Boarding;
                row.Settings.DockingResponse = security.Docking;
            }
            if (TryComp<WFCaptainComponent>(uid, out var captain))
                row.Settings.HeaveTo = captain.HeaveTo;
            if (TryComp<WFRadioOperatorComponent>(uid, out var radio))
            {
                row.Settings.Callsign = radio.Callsign ?? string.Empty;
                row.Settings.LocalChannel = radio.LocalChannel.Id;
                row.Settings.AlertChannel = radio.AlertChannel.Id;
                if (!radio.CallsForHelp)
                    row.Settings.CallsForHelp = false;
            }
            if (alive)
                row.Alive++;
        }
        foreach (var (key, row) in result)
        {
            if (!_queues.TryGetValue(key, out var state) || state.Items.Count == 0)
                continue;
            if (state.Status != "running")
            {
                row.Activity = row.Status;
                continue;
            }
            row.Activity = Loc.GetString($"wf-crew-activity-{state.Items[0].Kind.ToString().ToLowerInvariant()}");
            row.ActivityTarget = state.Items[0].Target ?? row.ActivityTarget;
        }
        return result.Values.ToList();
    }

    /// <summary>What the pilot is flying right now, for crews without a running queue.</summary>
    private void SetActivity(WFCrewSetupCrew row, WFPilotDutyComponent pilot)
    {
        var escort = pilot.Orders == WFPilotOrder.Follow && pilot.EscortOffset != null;
        row.Activity = Loc.GetString($"wf-crew-activity-{(escort ? "escort" : pilot.Orders.ToString().ToLowerInvariant())}");
        EntityUid? target = pilot.Orders switch
        {
            WFPilotOrder.Follow => pilot.FollowTarget,
            WFPilotOrder.Dock => pilot.DockTarget,
            WFPilotOrder.Loiter => pilot.LoiterCenter?.EntityId,
            _ => null,
        };
        row.ActivityTarget = target is { } uid && !TerminatingOrDeleted(uid) ? GetNetEntity(uid) : null;
    }

    private sealed class QueueState
    {
        public readonly List<WFCrewObjective> Items = new();
        public bool Started;
        public bool Paused;
        /// <summary>The captain suspended the pilot's course since the last tick that ran the task.</summary>
        public bool Evaded;
        public float Elapsed;
        /// <summary>Seconds the current travel order may run before it counts as unreachable.</summary>
        public float Budget;
        public EntityUid? Pilot;
        public string Status = "pending";
    }
}
