using System.Linq;
using Content.Server._WF.NpcCrew.Components;
using Content.Shared._WF.NpcCrew;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WF.NpcCrew.Systems;

/// <summary>Captains suspend and restore their own crew's orders without overwriting later commands.</summary>
public sealed partial class WFCaptainSystem : EntitySystem
{
    [Dependency] private WFPilotDutySystem _pilots = default!;
    [Dependency] private WFCrewAlertSystem _alerts = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private IGameTiming _timing = default!;

    /// <summary>How often an evasion is re-aimed at the current threats.</summary>
    private static readonly TimeSpan RetargetInterval = TimeSpan.FromSeconds(1);

    /// <summary>A new threat must be this much nearer (squared) than the one orbited to switch to it.</summary>
    private const float RetargetHysteresis = 2.25f;

    private readonly Dictionary<EntityUid, SavedCourse> _courses = new();
    private readonly HashSet<(EntityUid Grid, string Group, EntityUid Pilot)> _overridden = new();
    /// <summary>How far a run from an attacker takes the ship.</summary>
    private const float FleeLeg = 2500f;

    /// <summary>A run is flown again once the ship is this close to its end.</summary>
    private const float FleeRenew = 600f;

    /// <summary>Cosine of how far the bearing from the attacker may drift before the run is turned.</summary>
    private const float FleeTurn = 0.8f;

    /// <summary>Where each fleeing pilot's run ends.</summary>
    private readonly Dictionary<EntityUid, System.Numerics.Vector2> _fleeGoals = new();

    /// <summary>Pilots whose current orders are an evasion this system issued.</summary>
    private readonly HashSet<EntityUid> _evading = new();
    private bool _changingOrders;
    private TimeSpan _nextRetarget;

    /// <summary>Automatic evasion and restoration preserve the crew's escort assignment.</summary>
    public bool IsChangingOrders => _changingOrders;

    /// <summary>Whether a captain temporarily owns this pilot's flight orders.</summary>
    public bool IsCourseSuspended(EntityUid pilot) => _courses.ContainsKey(pilot);

    /// <summary>The escort leader and slot a suspended course returns to, if it is an escort.</summary>
    public bool TryGetSavedEscort(EntityUid pilot, out EntityUid leader, out int slot)
    {
        leader = default;
        slot = 0;
        if (!_courses.TryGetValue(pilot, out var course) || course.Order != WFPilotOrder.Follow
            || course.Follow is not { } follow || course.EscortOffset == null)
            return false;
        leader = follow;
        slot = course.EscortSlot;
        return true;
    }

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFCrewAlertEvent>(OnAlert);
        SubscribeLocalEvent<WFCrewAlertClearedEvent>(OnClear);
        SubscribeLocalEvent<WFPilotOrdersChangedEvent>(OnOrdersChanged);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var retarget = _timing.CurTime >= _nextRetarget;
        if (retarget)
        {
            _nextRetarget = _timing.CurTime + RetargetInterval;
            _evading.RemoveWhere(uid => TerminatingOrDeleted(uid));
            foreach (var pilot in _fleeGoals.Keys.Where(uid => !_evading.Contains(uid)).ToArray())
                _fleeGoals.Remove(pilot);
        }
        if (_courses.Count == 0)
            return;

        foreach (var (pilot, course) in _courses.ToArray())
        {
            if (!Eligible(pilot, course.Grid, course.Group))
            {
                _courses.Remove(pilot);
                continue;
            }
            // The pilot stands in for a fallen captain and still brings the ship back on course.
            var current = course;
            if (current.Captain != pilot && !Eligible(current.Captain, current.Grid, current.Group))
                _courses[pilot] = current = current with { Captain = pilot };
            if (retarget)
                Evade(pilot, current);
        }
    }

    private bool Eligible(EntityUid uid, EntityUid grid, string group)
    {
        return !TerminatingOrDeleted(uid) && _mobs.IsAlive(uid) && !HasComp<ActorComponent>(uid)
            && TryComp<WFCrewComponent>(uid, out var crew) && crew.Group == group && Transform(uid).GridUid == grid;
    }

    private void OnOrdersChanged(ref WFPilotOrdersChangedEvent args)
    {
        if (_changingOrders || args.Continuation)
            return;
        _evading.Remove(args.Mob);
        if (_courses.Remove(args.Mob, out var course))
            _overridden.Add((course.Grid, course.Group, args.Mob));
    }

    private void OnAlert(ref WFCrewAlertEvent args)
    {
        // A patrol zone's warning is not an attack: the guns answer it and the ship keeps its course. A vessel
        // inside the attack zone is fought like any attacker.
        if (_alerts.InZoneReport && !_alerts.InZoneEngagement)
            return;

        var commanded = false;
        var captains = EntityQueryEnumerator<WFCaptainComponent>();
        while (captains.MoveNext(out var captain, out var component))
        {
            if (!Eligible(captain, args.Grid, args.Group))
                continue;
            commanded = true;
            if (component.HeaveTo)
                React(captain, ref args);
        }

        // With no captain aboard the pilot decides for the ship.
        if (!commanded)
            React(null, ref args);
    }

    /// <summary>
    /// Suspends each pilot's course and has them evade the nearest attacker, or hold for a threat aboard. A course
    /// already suspended is kept and only its evasion is re-aimed.
    /// </summary>
    private void React(EntityUid? commander, ref WFCrewAlertEvent args)
    {
        var pilots = EntityQueryEnumerator<WFPilotDutyComponent, WFCrewComponent>();
        while (pilots.MoveNext(out var pilot, out var duty, out var crew))
        {
            if (crew.Duty != WFCrewDuties.Pilot || !Eligible(pilot, args.Grid, args.Group)
                || _overridden.Contains((args.Grid, args.Group, pilot)))
                continue;
            if (_courses.TryGetValue(pilot, out var suspended))
            {
                Evade(pilot, suspended);
                continue;
            }
            if (commander == null && !duty.ReactToAttacks)
                continue;
            var captain = commander ?? pilot;
            SavedCourse course;
            if (_evading.Contains(pilot))
            {
                // Its orders are the evasion of a course lost earlier; never save those, come back to a halt instead.
                course = new SavedCourse(captain, args.Grid, args.Group, WFPilotOrder.Hold,
                    new List<EntityCoordinates>(), null, 0f, null, WFCrewObjectiveKind.Loiter, null,
                    0f, null, null, 0, 0f, null, Angle.Zero);
            }
            else
            {
                // Preserve the destination beyond automatic undocking, not its temporary back-off course.
                var order = duty.ResumeOrder ?? duty.Orders;
                var waypoints = duty.ResumeOrder != null
                    ? duty.ResumeWaypoints.ToList()
                    : duty.Waypoints.Skip(duty.WaypointIndex).ToList();
                course = new SavedCourse(captain, args.Grid, args.Group, order,
                    waypoints, duty.LoiterCenter, duty.RequestedLoiterRadius, duty.LoiterSpeedOverride, duty.OrbitKind,
                    duty.FollowTarget, duty.FollowRange, duty.DockTarget, duty.EscortOffset, duty.EscortSlot, duty.EscortSpacing,
                    duty.HoldPosition, duty.HoldHeading);
            }
            _courses[pilot] = course;
            Evade(pilot, course);
        }
    }

    /// <summary>Orbits the nearest hostile ship, or holds when none is left; leaves orders that already match.</summary>
    private void Evade(EntityUid pilot, SavedCourse course)
    {
        if (!TryComp<WFPilotDutyComponent>(pilot, out var duty))
            return;
        if (duty.FleeAttackers)
        {
            Flee(pilot, duty, course);
            return;
        }

        // While undocking for the evasion, the evasion is the order resumed afterwards.
        var current = duty.ResumeOrder ?? duty.Orders;
        var orbiting = current == WFPilotOrder.Loiter && duty.OrbitKind == WFCrewObjectiveKind.Attack
            ? duty.LoiterCenter?.EntityId : null;
        var threat = PickThreat(course.Grid, course.Group, orbiting);
        if (threat is { } ship ? orbiting == ship : current == WFPilotOrder.Hold)
            return;

        _changingOrders = true;
        try
        {
            if (threat is { } target)
            {
                var center = TryComp<Robust.Shared.Map.Components.MapGridComponent>(target, out var targetGrid)
                    ? targetGrid.LocalAABB.Center : System.Numerics.Vector2.Zero;
                _pilots.Loiter(pilot, new EntityCoordinates(target, center), 0f,
                    objective: WFCrewObjectiveKind.Attack);
            }
            else
                _pilots.Hold(pilot);
            _evading.Add(pilot);
        }
        finally { _changingOrders = false; }
    }

    /// <summary>
    /// Runs directly away from the nearest hostile ship, or holds when none is left. A run is kept until it nears its
    /// end or the attacker comes round to another side.
    /// </summary>
    private void Flee(EntityUid pilot, WFPilotDutyComponent duty, SavedCourse course)
    {
        var current = duty.ResumeOrder ?? duty.Orders;
        if (PickThreat(course.Grid, course.Group, null) is not { } threat)
        {
            if (current == WFPilotOrder.Hold)
                return;

            _changingOrders = true;
            try
            {
                _pilots.Hold(pilot);
                _evading.Add(pilot);
                _fleeGoals.Remove(pilot);
            }
            finally { _changingOrders = false; }
            return;
        }

        var here = _transform.GetWorldPosition(course.Grid);
        var away = here - _transform.GetWorldPosition(threat);
        var heading = away.LengthSquared() > 1f ? System.Numerics.Vector2.Normalize(away) : System.Numerics.Vector2.UnitX;
        if (current == WFPilotOrder.GoTo && _fleeGoals.TryGetValue(pilot, out var goal)
            && (goal - here).LengthSquared() > FleeRenew * FleeRenew
            && System.Numerics.Vector2.Dot(System.Numerics.Vector2.Normalize(goal - here), heading) >= FleeTurn)
            return;

        if (Transform(course.Grid).MapUid is not { } map)
            return;

        goal = here + heading * FleeLeg;
        _changingOrders = true;
        try
        {
            _pilots.GoTo(pilot, new List<EntityCoordinates> { new(map, goal) });
            _evading.Add(pilot);
            _fleeGoals[pilot] = goal;
        }
        finally { _changingOrders = false; }
    }

    /// <summary>The nearest hostile ship on the crew's map, keeping the one orbited unless another is much nearer.</summary>
    private EntityUid? PickThreat(EntityUid grid, string group, EntityUid? orbiting)
    {
        var position = _transform.GetWorldPosition(grid);
        EntityUid? nearest = null;
        var best = float.MaxValue;
        var orbitedDistance = float.MaxValue;
        foreach (var ship in _alerts.GetHostileShips(grid, group))
        {
            var distance = (_transform.GetWorldPosition(ship) - position).LengthSquared();
            if (ship == orbiting)
                orbitedDistance = distance;
            if (distance >= best)
                continue;
            best = distance;
            nearest = ship;
        }
        if (orbiting != null && orbitedDistance < float.MaxValue && orbitedDistance <= best * RetargetHysteresis)
            return orbiting;
        return nearest;
    }

    private void OnClear(ref WFCrewAlertClearedEvent args)
    {
        if (_alerts.IsAlerted(args.Grid, args.Group))
            return;
        var grid = args.Grid;
        var group = args.Group;
        _overridden.RemoveWhere(entry => entry.Grid == grid && entry.Group == group);
        foreach (var (pilot, course) in _courses.ToArray())
        {
            if (course.Grid != args.Grid || course.Group != args.Group)
                continue;
            _courses.Remove(pilot);
            // A fallen captain's course is still restored by the pilot.
            if (!Eligible(pilot, course.Grid, course.Group))
                continue;
            _evading.Remove(pilot);
            _changingOrders = true;
            try
            {
                RestoreCourse(pilot, course);
            }
            finally { _changingOrders = false; }
        }

        // Pilots still flying an evasion whose course was lost come to a halt.
        foreach (var pilot in _evading.ToArray())
        {
            if (!Eligible(pilot, grid, group))
                continue;
            _evading.Remove(pilot);
            _changingOrders = true;
            try
            {
                _pilots.Hold(pilot);
            }
            finally { _changingOrders = false; }
        }
    }

    /// <summary>Puts the saved course back; a target that is gone falls back to holding where the ship is.</summary>
    private void RestoreCourse(EntityUid pilot, SavedCourse course)
    {
        switch (course.Order)
        {
            case WFPilotOrder.Hold:
                _pilots.Hold(pilot);
                if (course.HoldPosition is { } anchor && anchor.IsValid(EntityManager)
                    && _transform.ToMapCoordinates(anchor).MapId == Transform(pilot).MapID
                    && TryComp<WFPilotDutyComponent>(pilot, out var holding))
                {
                    holding.HoldPosition = anchor;
                    holding.HoldHeading = course.HoldHeading;
                    _pilots.SetNavigation(pilot, holding.Navigation);
                }
                break;
            case WFPilotOrder.GoTo:
                _pilots.GoTo(pilot, course.Waypoints.Where(point => point.IsValid(EntityManager)).ToList());
                break;
            case WFPilotOrder.Loiter when course.Center is { } center && center.IsValid(EntityManager):
                _pilots.Loiter(pilot, center, course.Radius, course.OrbitSpeed, course.OrbitKind);
                break;
            case WFPilotOrder.Follow when course.Follow is { } follow && !TerminatingOrDeleted(follow):
                _pilots.Follow(pilot, follow, course.Range);
                if (TryComp<WFPilotDutyComponent>(pilot, out var formation))
                {
                    formation.EscortOffset = course.EscortOffset;
                    formation.EscortSlot = course.EscortSlot;
                    formation.EscortSpacing = course.EscortSpacing;
                    _pilots.SetNavigation(pilot, formation.Navigation);
                }
                break;
            case WFPilotOrder.Dock when course.Dock is { } dock && !TerminatingOrDeleted(dock):
                _pilots.Dock(pilot, dock);
                break;
            case WFPilotOrder.Undock:
                _pilots.Undock(pilot);
                break;
            default:
                _pilots.Hold(pilot);
                break;
        }
    }

    private sealed record SavedCourse(EntityUid Captain, EntityUid Grid, string Group, WFPilotOrder Order,
        List<EntityCoordinates> Waypoints, EntityCoordinates? Center, float Radius, float? OrbitSpeed, WFCrewObjectiveKind OrbitKind, EntityUid? Follow,
        float Range, EntityUid? Dock, System.Numerics.Vector2? EscortOffset, int EscortSlot, float EscortSpacing,
        EntityCoordinates? HoldPosition, Angle HoldHeading);
}
