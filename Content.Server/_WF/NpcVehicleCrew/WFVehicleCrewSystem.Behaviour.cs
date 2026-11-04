using System.Numerics;
using Content.Server._WF.Vehicles.Npc;
using Content.Shared._WF.NpcVehicleCrew;
using Content.Shared.Mobs.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._WF.NpcVehicleCrew;

/// <summary>The commander: Hold, Patrol, Hunt, Escort, Flee and Retreat each set where the driver goes.</summary>
public sealed partial class WFVehicleCrewSystem
{
    /// <summary>How far a holding vehicle may be pushed from home before it drives back.</summary>
    private const float HomeSlack = 3f;

    private const int PatrolPointsMin = 4;
    private const int PatrolPointsMax = 6;

    /// <summary>Patrol points are at least this share of the patrol radius from home.</summary>
    private const float PatrolInnerShare = 0.3f;

    private const int PatrolPointTries = 20;

    /// <summary>A patrol whose longest pause is no more than this rolls through its points without stopping at each.</summary>
    private static readonly TimeSpan RollOnPause = TimeSpan.FromSeconds(2);

    /// <summary>How near a point a rolling patrol counts as through it, wider than an arrival since it doesn't stop there.</summary>
    private const float RollOnRadius = 3f;

    /// <summary>How long a patrol that stops to fight waits after losing sight of the target.</summary>
    private static readonly TimeSpan ContactLoss = TimeSpan.FromSeconds(30);

    /// <summary>Out of sight this long, a hunter drives to where the target was last seen.</summary>
    private static readonly TimeSpan HuntChaseDelay = TimeSpan.FromSeconds(10);

    /// <summary>Out of sight this long, a hunter gives up and goes back to its orders.</summary>
    private static readonly TimeSpan HuntGiveUp = TimeSpan.FromSeconds(60);

    /// <summary>A hunter closes in beyond this multiple of its standoff and backs off inside the second.</summary>
    private const float AdvanceFactor = 1.3f;

    private const float BackOffFactor = 0.6f;

    /// <summary>How far one back-off goal lies behind the vehicle; inside the driver's reverse range.</summary>
    private const float BackOffStep = 2.5f;

    /// <summary>An escort keeps this far behind its lead, with a tile of slack either way.</summary>
    private const float EscortDistance = 5f;

    private const float EscortSlack = 1f;

    /// <summary>How far a fleeing vehicle runs when home lies toward the attacker.</summary>
    private const float FleeDistance = 40f;

    /// <summary>Unhit this long, a fleeing vehicle stops running.</summary>
    private static readonly TimeSpan FleeCalm = TimeSpan.FromSeconds(30);

    private readonly HashSet<EntityUid> _strangers = new();
    private readonly List<EntityUid> _gone = new();

    /// <summary>Retreats a damaged or dry vehicle home, and sends it back to its orders once repaired and rearmed.</summary>
    private void UpdateDamageState(EntityUid uid, WFVehicleCrewComponent crew, WFVehicleCrewPrototype proto)
    {
        var fraction = IntegrityFraction(uid);
        if (crew.Behaviour == WFVehicleBehaviour.Retreat)
        {
            if (fraction >= proto.RetreatIntegrity + RetreatRecoverMargin && !crew.OutOfAmmo)
                ChangeBehaviour(crew, crew.PreviousBehaviour);
            return;
        }

        if (crew.Behaviour is WFVehicleBehaviour.Flee or WFVehicleBehaviour.Bail)
            return;

        if (fraction < proto.RetreatIntegrity || crew.Gunner != null && crew.OutOfAmmo)
            ChangeBehaviour(crew, WFVehicleBehaviour.Retreat);
    }

    /// <summary>
    /// A holding vehicle with a guard radius sounds its horn at each stranger who comes inside it, and takes him for an
    /// attacker once he has stayed the warning time.
    /// </summary>
    private void UpdateGuard(EntityUid uid, WFVehicleCrewComponent crew, WFVehicleCrewPrototype proto, TimeSpan now)
    {
        if (crew.Behaviour != WFVehicleBehaviour.Hold || proto.GuardRadius <= 0f)
        {
            crew.Warned.Clear();
            return;
        }

        var here = _transform.GetMapCoordinates(uid);
        _strangers.Clear();
        foreach (var mob in _lookup.GetEntitiesInRange<MobStateComponent>(here, proto.GuardRadius))
        {
            if (IsStranger(uid, crew, mob.Owner))
                _strangers.Add(mob.Owner);
        }

        foreach (var vehicle in _lookup.GetEntitiesInRange<WFOccupiedVehicleComponent>(here, proto.GuardRadius))
        {
            if (IsStranger(uid, crew, vehicle.Owner))
                _strangers.Add(vehicle.Owner);
        }

        _gone.Clear();
        foreach (var warned in crew.Warned.Keys)
        {
            if (!_strangers.Contains(warned))
                _gone.Add(warned);
        }

        foreach (var gone in _gone)
            crew.Warned.Remove(gone);

        var warnTime = TimeSpan.FromSeconds(proto.GuardWarnTime);
        foreach (var stranger in _strangers)
        {
            if (!crew.Warned.TryGetValue(stranger, out var warnedAt))
            {
                crew.Warned[stranger] = now;
                _horn.TrySoundHorn(uid);
                continue;
            }

            if (now - warnedAt >= warnTime)
                crew.Attackers[stranger] = now;
        }
    }

    /// <summary>Someone a guard warns off: up and about, not the crew, not of a friendly faction.</summary>
    private bool IsStranger(EntityUid uid, WFVehicleCrewComponent crew, EntityUid other)
    {
        if (other == uid || IsOwnCrew(crew, other) || TerminatingOrDeleted(other))
            return false;

        if (TryComp<MobStateComponent>(other, out var mobState) && _mobState.IsIncapacitated(other, mobState))
            return false;

        return !_factions.IsEntityFriendly(uid, other);
    }

    /// <summary>Sets the driver's goal from the crew's current orders; a target sends a pursuing crew hunting.</summary>
    private void UpdateBehaviour(EntityUid uid, WFVehicleCrewComponent crew, WFVehicleCrewPrototype proto, TimeSpan now)
    {
        var here = _transform.GetMapCoordinates(uid);
        crew.NextGoal = null;
        if (crew.Target != null && crew.TargetVisible && crew.Pursues &&
            crew.Behaviour is WFVehicleBehaviour.Hold or WFVehicleBehaviour.Patrol)
        {
            ChangeBehaviour(crew, WFVehicleBehaviour.Hunt);
        }

        switch (crew.Behaviour)
        {
            case WFVehicleBehaviour.Hold:
                GoHome(crew, here, HomeSlack);
                break;
            case WFVehicleBehaviour.Patrol:
                UpdatePatrol(uid, crew, proto, here, now);
                break;
            case WFVehicleBehaviour.Hunt:
                UpdateHunt(crew, proto, here, now);
                break;
            case WFVehicleBehaviour.Escort:
                UpdateEscort(crew, here);
                break;
            case WFVehicleBehaviour.Flee:
                UpdateFlee(crew, here, now);
                break;
            case WFVehicleBehaviour.Retreat:
                GoHome(crew, here, ArriveDistance);
                break;
            default:
                crew.Goal = null;
                break;
        }
    }

    /// <summary>Drives home once further than <paramref name="slack"/> from it, and keeps going until there.</summary>
    private static void GoHome(WFVehicleCrewComponent crew, MapCoordinates here, float slack)
    {
        var away = Distance(here, crew.Home);
        crew.Goal = away > slack || crew.Goal != null && away > ArriveDistance ? crew.Home : null;
    }

    /// <summary>
    /// Loops the patrol points; a crew that does not fire on the move stops while in contact. A road patrol (short
    /// pauses) rolls through each point on to the next, and stops only at a turnaround; any other pauses at each point.
    /// </summary>
    private void UpdatePatrol(EntityUid uid, WFVehicleCrewComponent crew, WFVehicleCrewPrototype proto, MapCoordinates here, TimeSpan now)
    {
        EnsurePatrolPoints(uid, crew, proto);
        if (crew.Target != null && !proto.FireOnTheMove && now - crew.LastSeenAt < ContactLoss)
        {
            crew.Goal = null;
            return;
        }

        if (now < crew.PauseUntil || crew.Waypoints.Count == 0)
        {
            crew.Goal = null;
            return;
        }

        crew.WaypointIndex %= crew.Waypoints.Count;
        var point = crew.Waypoints[crew.WaypointIndex];
        var rollOn = RollOnPoint(crew, proto, here);
        var reached = Distance(here, point) <= ArriveDistance ||
                      rollOn != null && Distance(here, point) <= RollOnRadius ||
                      rollOn is { } following && PassedPoint(here, point, following);
        if (!reached)
        {
            crew.Goal = point;
            crew.NextGoal = rollOn;
            return;
        }

        crew.WaypointIndex = (crew.WaypointIndex + 1) % crew.Waypoints.Count;
        crew.PatrolFrom = point;
        if (rollOn is { } through)
        {
            crew.Goal = through;
            crew.NextGoal = RollOnPoint(crew, proto, here);
            return;
        }

        crew.PauseUntil = now + (proto.PatrolPauseMax > proto.PatrolPauseMin
            ? _random.Next(proto.PatrolPauseMin, proto.PatrolPauseMax)
            : proto.PatrolPauseMin);
        crew.Goal = null;
    }

    /// <summary>
    /// The point after the current one, when the patrol should roll through the current one to it: a road patrol with
    /// another point to go to that doesn't double back on the way it came. Null means stop at the current point.
    /// </summary>
    private static MapCoordinates? RollOnPoint(WFVehicleCrewComponent crew, WFVehicleCrewPrototype proto, MapCoordinates here)
    {
        var count = crew.Waypoints.Count;
        if (count < 2 || proto.PatrolPauseMax > RollOnPause)
            return null;

        var index = crew.WaypointIndex % count;
        var previous = crew.PatrolFrom ?? here;
        var point = crew.Waypoints[index];
        var next = crew.Waypoints[(index + 1) % count];
        if (point.MapId != here.MapId || next.MapId != here.MapId || previous.MapId != here.MapId)
            return null;

        // Reversing on itself: a turnaround is a stop.
        return Vector2.Dot(point.Position - previous.Position, next.Position - point.Position) < 0f ? null : next;
    }

    /// <summary>Whether the vehicle has driven past a point on its way to the one after it.</summary>
    private static bool PassedPoint(MapCoordinates here, MapCoordinates point, MapCoordinates next)
    {
        var toPoint = point.Position - here.Position;
        return toPoint.Length() < LookAheadMin && Vector2.Dot(toPoint, next.Position - point.Position) < 0f;
    }

    /// <summary>Rolls patrol points round home on floor of the vehicle's grid, when none were given.</summary>
    private void EnsurePatrolPoints(EntityUid uid, WFVehicleCrewComponent crew, WFVehicleCrewPrototype proto)
    {
        if (crew.Waypoints.Count > 0)
            return;

        var gridUid = Transform(uid).GridUid;
        TryComp<MapGridComponent>(gridUid, out var grid);
        var count = _random.Next(PatrolPointsMin, PatrolPointsMax + 1);
        for (var i = 0; i < count; i++)
        {
            for (var attempt = 0; attempt < PatrolPointTries; attempt++)
            {
                var angle = new Angle(_random.NextFloat(0f, MathF.Tau));
                var distance = _random.NextFloat(PatrolInnerShare, 1f) * proto.PatrolRadius;
                var point = new MapCoordinates(crew.Home.Position + angle.ToVec() * distance, crew.Home.MapId);
                if (gridUid is { } gridEnt && grid != null &&
                    (!_map.TryGetTileRef(gridEnt, grid, _map.WorldToTile(gridEnt, grid, point.Position), out var tile) ||
                     tile.Tile.IsEmpty))
                {
                    continue;
                }

                crew.Waypoints.Add(point);
                break;
            }
        }

        if (crew.Waypoints.Count == 0)
            crew.Waypoints.Add(crew.Home);
    }

    /// <summary>
    /// Closes on the target to the standoff, backs off inside it and holds between; out of sight, drives to where it was
    /// last seen and in the end gives up.
    /// </summary>
    private void UpdateHunt(WFVehicleCrewComponent crew, WFVehicleCrewPrototype proto, MapCoordinates here, TimeSpan now)
    {
        if (crew.Target != null && crew.TargetVisible && crew.LastSeenTarget is { } seen)
        {
            var distance = Distance(here, seen);
            if (distance > proto.Standoff * AdvanceFactor)
            {
                crew.Goal = seen;
            }
            else if (distance < proto.Standoff * BackOffFactor)
            {
                var away = here.Position - seen.Position;
                away = away.LengthSquared() > 0.0001f ? Vector2.Normalize(away) : Vector2.UnitX;
                crew.Goal = new MapCoordinates(here.Position + away * BackOffStep, here.MapId);
            }
            else
            {
                crew.Goal = null;
            }

            return;
        }

        var unseen = now - crew.LastSeenAt;
        if (crew.LastSeenTarget is not { } last || unseen > HuntGiveUp)
        {
            crew.Target = null;
            crew.LastSeenTarget = null;
            ChangeBehaviour(crew, crew.PreviousBehaviour);
            return;
        }

        crew.Goal = unseen > HuntChaseDelay ? last : null;
    }

    /// <summary>Keeps station behind the lead; without one, holds where it is.</summary>
    private void UpdateEscort(WFVehicleCrewComponent crew, MapCoordinates here)
    {
        if (crew.Lead is not { } lead || TerminatingOrDeleted(lead))
        {
            crew.Lead = null;
            crew.Home = here;
            crew.Behaviour = WFVehicleBehaviour.Hold;
            crew.Goal = null;
            return;
        }

        var leadPosition = _transform.GetMapCoordinates(lead);
        if (leadPosition.MapId != here.MapId)
        {
            crew.Goal = null;
            return;
        }

        var gap = Distance(here, leadPosition);
        if (gap >= EscortDistance - EscortSlack && gap <= EscortDistance + EscortSlack)
        {
            crew.Goal = null;
            return;
        }

        var behind = leadPosition.Position - _transform.GetWorldRotation(lead).ToWorldVec() * EscortDistance;
        crew.Goal = new MapCoordinates(behind, leadPosition.MapId);
    }

    /// <summary>Runs from the latest attacker, toward home when home lies away from him, until left alone a while.</summary>
    private void UpdateFlee(WFVehicleCrewComponent crew, MapCoordinates here, TimeSpan now)
    {
        if (now - crew.LastHit > FleeCalm)
        {
            ChangeBehaviour(crew, crew.PreviousBehaviour);
            return;
        }

        EntityUid? threat = null;
        var latest = TimeSpan.MinValue;
        foreach (var (attacker, at) in crew.Attackers)
        {
            if (at <= latest || TerminatingOrDeleted(attacker))
                continue;

            threat = attacker;
            latest = at;
        }

        var threatPosition = threat is { } found ? _transform.GetMapCoordinates(found) : MapCoordinates.Nullspace;
        if (threatPosition.MapId != here.MapId)
        {
            GoHome(crew, here, ArriveDistance);
            return;
        }

        var away = here.Position - threatPosition.Position;
        away = away.LengthSquared() > 0.0001f ? Vector2.Normalize(away) : Vector2.UnitX;
        var toHome = crew.Home.Position - here.Position;
        if (crew.Home.MapId == here.MapId && toHome.Length() > ArriveDistance && Vector2.Dot(toHome, away) > 0f)
        {
            crew.Goal = crew.Home;
            return;
        }

        if (crew.Goal is not { } goal || Distance(here, goal) <= ArriveDistance || Distance(goal, crew.Home) < 0.01f)
            crew.Goal = new MapCoordinates(here.Position + away * FleeDistance, here.MapId);
    }
}
