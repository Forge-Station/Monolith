using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.NPC.Pathfinding;
using Content.Shared._WF.NpcVehicleCrew;
using Content.Shared._WF.Vehicles.Core;
using Content.Shared._WF.Vehicles.Grid.Components;
using Content.Shared.Physics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;

namespace Content.Server._WF.NpcVehicleCrew;

/// <summary>The driver: follows a route to the goal through the autopilot, backs out of stuck spots and plans paths.</summary>
public sealed partial class WFVehicleCrewSystem
{
    /// <summary>How near a waypoint counts as reached.</summary>
    private const float ArriveDistance = 1.5f;

    /// <summary>Heading error at which the wheel is turned all the way.</summary>
    private const float SteerFullErrorDegrees = 30f;

    /// <summary>A waypoint behind the vehicle and this close is reversed to.</summary>
    private const float ReverseRange = 3f;

    /// <summary>How far down the route the driver aims: this many seconds of travel, within the bounds below.</summary>
    private const float LookAheadTime = 0.9f;

    private const float LookAheadMin = 4f;

    private const float LookAheadMax = 12f;

    /// <summary>Top speed on open ground, short of flat out, so there's time to see a bend coming.</summary>
    private const float CruiseSpeed = 8f;

    /// <summary>Lowest speed asked for while turning; slower and the wheels barely steer.</summary>
    private const float CrawlSpeed = 1.5f;

    /// <summary>The share of the chassis's turn rate the speed for a turn is worked out on, leaving some in hand.</summary>
    private const float TurnMargin = 0.75f;

    /// <summary>How far over the speed cap the vehicle may run before the driver brakes; between, it coasts.</summary>
    private const float SpeedSlack = 0.75f;

    /// <summary>
    /// The throttle is let go this share of a think's pickup short of the cap, so the speed settles in the coast band
    /// rather than overshooting into the brake.
    /// </summary>
    private const float ThrottleLead = 0.5f;

    /// <summary>Heading error a tracked chassis stops and pivots for rather than driving round.</summary>
    private const float TurnInPlaceDegrees = 70f;

    /// <summary>Heading error the driver lets be.</summary>
    private const float SteerDeadZoneDegrees = 1.5f;

    /// <summary>Turn rate asked for per degree of error, before the stopping limit.</summary>
    private const float SteerGain = 1.4f;

    /// <summary>The share of the chassis's turn acceleration the driver counts on to stop a turn in time.</summary>
    private const float BrakeShare = 0.6f;

    /// <summary>Seconds between the driver's looks: one think.</summary>
    private const float ThinkLatency = 0.2f;

    /// <summary>Half the width of road a vehicle needs clear, in tiles, checked in this many lanes each side.</summary>
    private const float ClearanceHalfWidth = 1.6f;

    private const int ClearanceLanes = 2;

    /// <summary>Tiles between checks along a straight run.</summary>
    private const float ClearanceStep = 0.5f;

    /// <summary>Furthest a path is searched ahead for a straight run, in path polygons.</summary>
    private const int MaxShortcut = 40;

    /// <summary>What a vehicle runs into: walls, rocks, trees, barriers and furniture.</summary>
    private const int VehicleObstacleMask = (int) (CollisionGroup.Impassable | CollisionGroup.MidImpassable | CollisionGroup.HighImpassable);

    private readonly HashSet<Vector2i> _clearanceChecked = new();

    /// <summary>Top speed over the last stretch to the goal, so the vehicle stops on it rather than past it.</summary>
    private const float ApproachSpeed = 3f;

    private const float ApproachDistance = 5f;

    /// <summary>Speed below which a vehicle with the throttle held is not getting anywhere.</summary>
    private const float StuckSpeed = 0.2f;

    /// <summary>Speed below which a braking vehicle lets go rather than start rolling the other way.</summary>
    private const float BrakeStopSpeed = 0.3f;

    private static readonly TimeSpan StuckTime = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan StuckReverseTime = TimeSpan.FromSeconds(1.5);

    /// <summary>Stucks on one leg after which it is skipped.</summary>
    private const int MaxStucks = 3;

    /// <summary>A goal this close is driven to straight, without a path.</summary>
    private const float DirectRange = 4f;

    /// <summary>How far a goal may move before its route is planned again.</summary>
    private const float RepathDrift = 2f;

    private static readonly TimeSpan RepathInterval = TimeSpan.FromSeconds(5);

    /// <summary>How near the path's end the pathfinder must get.</summary>
    private const float PathRange = 1.5f;

    /// <summary>Every this many path polygons is kept as a waypoint.</summary>
    private const int PathStride = 3;

    /// <summary>Hull bearing error a stopped vehicle turning to face a target accepts.</summary>
    private const float FaceToleranceDegrees = 5f;

    /// <summary>Steers the vehicle one tick toward <see cref="WFVehicleCrewComponent.Goal"/>, or holds it still.</summary>
    private void Drive(EntityUid uid, WFVehicleCrewComponent crew, TimeSpan now)
    {
        if (!TryComp<WFVehicleAutopilotComponent>(uid, out var autopilot) ||
            !TryComp<GridVehicleMoverComponent>(uid, out var mover))
        {
            return;
        }

        autopilot.Active = true;
        var here = _transform.GetMapCoordinates(uid);
        if (crew.Goal is not { } goal || goal.MapId != here.MapId)
        {
            crew.StuckSince = null;
            crew.Route.Clear();
            crew.RouteGoal = null;
            crew.NextRoute.Clear();
            crew.NextRouteGoal = null;
            if (TryBrake(autopilot, mover, 0f))
                return;

            if (crew.Face is { } face && face.MapId == here.MapId)
                Turn(uid, mover, autopilot, here, face);
            else
                SetInput(autopilot, mover, 0f, 0f, Vector2i.Zero);
            return;
        }

        if (TryComp<WFVehicleEngineComponent>(uid, out var engine) && (!engine.Running || engine.ParkingBrake))
            _engine.TryStartForNpc((uid, engine));

        UpdateRoute(uid, crew, here, goal, now);
        UpdateNextRoute(uid, crew, goal);

        // Drop the route points already reached or driven past.
        while (crew.Route.Count > 0)
        {
            var next = crew.Route.Count > 1 ? crew.Route[1] : goal;
            var toPoint = crew.Route[0].Position - here.Position;
            var passed = Vector2.Dot(toPoint, next.Position - crew.Route[0].Position) < 0f && toPoint.Length() < LookAheadMin;
            if (toPoint.Length() > ArriveDistance && !passed)
                break;

            crew.Route.RemoveAt(0);
            crew.StuckCount = 0;
        }

        var finalDistance = Distance(here, goal);
        if (crew.Route.Count == 0 && finalDistance <= ArriveDistance && crew.NextGoal == null)
        {
            crew.StuckSince = null;
            crew.StuckCount = 0;
            if (!TryBrake(autopilot, mover, 0f))
                SetInput(autopilot, mover, 0f, 0f, Vector2i.Zero);
            return;
        }

        if (now < crew.ReverseUntil)
        {
            SetInput(autopilot, mover, -1f, crew.ReverseSteering, -mover.CurrentDirection);
            return;
        }

        // Aim a little way down the route rather than at its next point: the line stays smooth and never asks for a
        // turn tighter than the chassis can make.
        var lookAhead = Math.Clamp(MathF.Abs(mover.CurrentSpeed) * LookAheadTime, LookAheadMin, LookAheadMax);
        var point = LookAheadPoint(here, crew.Route, goal, lookAhead, crew.NextGoal, crew.NextRoute);
        var delta = point.Position - here.Position;
        var heading = _transform.GetWorldRotation(uid);
        var bearing = delta.ToWorldAngle();
        var error = Angle.ShortestDistance(heading, bearing).Degrees;
        var behind = Math.Abs(error) > 110;
        float throttle;
        float steering;
        if (!mover.FreeSteering)
        {
            throttle = 1f;
            steering = 0f;
        }
        else if (behind && delta.Length() <= ReverseRange)
        {
            // Close behind: back up to it, swinging the tail toward it. Steering flips in reverse, like a car's.
            var rearError = Angle.ShortestDistance(heading + new Angle(Math.PI), bearing).Degrees;
            throttle = -1f;
            steering = Math.Clamp(-(float) rearError / SteerFullErrorDegrees, -1f, 1f);
        }
        else if (mover.TurnInPlace && Math.Abs(error) > TurnInPlaceDegrees)
        {
            // A tracked chassis stops and pivots to face a sharp turn.
            crew.StuckSince = null;
            if (!TryBrake(autopilot, mover, 0f))
                SetInput(autopilot, mover, 0f, Math.Sign(error), Vector2i.Zero);
            return;
        }
        else
        {
            steering = Steer(mover, (float) error);

            // Slow enough that the point lies outside the turning circle, or the vehicle orbits it.
            var cap = MathF.Min(mover.MaxSpeed, CruiseSpeed);
            var turnRate = MathF.Max(mover.MaxRotationSpeedDegrees, 1f) * MathF.PI / 180f * TurnMargin;
            var off = MathF.Min(MathF.Abs((float) error), 90f) * MathF.PI / 180f;
            if (off > 0.05f)
                cap = MathF.Min(cap, MathF.Max(CrawlSpeed, turnRate * delta.Length() / (2f * MathF.Sin(off))));
            if (behind)
                cap = CrawlSpeed;
            if (crew.Route.Count == 0 && finalDistance < ApproachDistance && crew.NextGoal == null)
                cap = MathF.Min(cap, ApproachSpeed);

            // The mover takes any throttle as full: speed is held by braking above the cap and coasting near it.
            if (mover.CurrentSpeed > cap + SpeedSlack)
            {
                crew.StuckSince = null;
                TryBrake(autopilot, mover, steering);
                return;
            }

            var lead = MathF.Min(mover.Acceleration * ThinkLatency * ThrottleLead, cap * 0.1f);
            throttle = mover.CurrentSpeed < cap - lead ? 1f : 0f;
        }

        UpdateStuck(crew, mover, throttle, steering, now);
        SetInput(autopilot, mover, throttle, steering, GridCardinal(uid, delta));
    }

    /// <summary>
    /// The wheel for a heading error: asks for the turn rate that closes the error without overshooting, given that the
    /// chassis takes time to start and stop turning and the driver only looks again a think later.
    /// </summary>
    private static float Steer(GridVehicleMoverComponent mover, float errorDegrees)
    {
        var maxRate = MathF.Max(mover.MaxRotationSpeedDegrees, 1f);
        var accel = MathF.Max(mover.RotationAccelerationDegrees, 1f);
        // Where the heading will be by the next look, at the rate it is turning now.
        var error = errorDegrees - mover.AngularVelocityDegrees * ThinkLatency;
        var size = MathF.Abs(error);
        if (size < SteerDeadZoneDegrees)
            return 0f;

        var rate = MathF.Min(maxRate, MathF.Min(MathF.Sqrt(2f * accel * BrakeShare * size), SteerGain * size));
        return Math.Clamp(MathF.Sign(error) * rate / maxRate, -1f, 1f);
    }

    /// <summary>
    /// The point <paramref name="distance"/> along the route from here, or its end if it is shorter. A vehicle rolling on
    /// through the goal carries on along <paramref name="nextRoute"/> to <paramref name="next"/>, so the line stays smooth.
    /// </summary>
    private static MapCoordinates LookAheadPoint(MapCoordinates here, List<MapCoordinates> route, MapCoordinates goal, float distance,
        MapCoordinates? next, List<MapCoordinates> nextRoute)
    {
        var previous = here.Position;
        var travelled = 0f;
        var count = route.Count + 1 + (next != null ? nextRoute.Count + 1 : 0);
        for (var i = 0; i < count; i++)
        {
            var node = i < route.Count
                ? route[i].Position
                : i == route.Count
                    ? goal.Position
                    : i - route.Count - 1 < nextRoute.Count
                        ? nextRoute[i - route.Count - 1].Position
                        : next!.Value.Position;
            var leg = node - previous;
            var length = leg.Length();
            if (length > 0f && travelled + length >= distance)
                return new MapCoordinates(previous + leg * ((distance - travelled) / length), here.MapId);

            travelled += length;
            previous = node;
        }

        return next ?? goal;
    }

    /// <summary>Brakes a rolling vehicle by asking for the opposite direction, which the chassis takes as a stop; false once it is as good as stopped.</summary>
    private static bool TryBrake(WFVehicleAutopilotComponent autopilot, GridVehicleMoverComponent mover, float steering)
    {
        if (MathF.Abs(mover.CurrentSpeed) <= BrakeStopSpeed)
            return false;

        // The wheel stays where it was, or the vehicle runs wide of the turn it slowed for.
        var against = mover.CurrentSpeed > 0f ? -1f : 1f;
        SetInput(autopilot, mover, against, steering, mover.CurrentSpeed > 0f ? -mover.CurrentDirection : mover.CurrentDirection);
        return true;
    }

    /// <summary>Turns a stopped vehicle's hull toward a point, for a gun that cannot traverse; only a chassis that turns on the spot can.</summary>
    private void Turn(EntityUid uid, GridVehicleMoverComponent mover, WFVehicleAutopilotComponent autopilot,
        MapCoordinates here, MapCoordinates face)
    {
        var delta = face.Position - here.Position;
        if (!mover.FreeSteering || !mover.TurnInPlace || delta.LengthSquared() < 0.01f)
        {
            SetInput(autopilot, mover, 0f, 0f, Vector2i.Zero);
            return;
        }

        var error = Angle.ShortestDistance(_transform.GetWorldRotation(uid), delta.ToWorldAngle()).Degrees;
        var steering = Math.Abs(error) <= FaceToleranceDegrees ? 0f : Math.Clamp((float) error / SteerFullErrorDegrees, -1f, 1f);
        SetInput(autopilot, mover, 0f, steering, Vector2i.Zero);
    }

    /// <summary>Starts backing out once the throttle has been held for <see cref="StuckTime"/> without moving; three times skips the leg.</summary>
    private void UpdateStuck(WFVehicleCrewComponent crew, GridVehicleMoverComponent mover, float throttle, float steering, TimeSpan now)
    {
        if (throttle == 0f || MathF.Abs(mover.CurrentSpeed) >= StuckSpeed)
        {
            crew.StuckSince = null;
            return;
        }

        crew.StuckSince ??= now;
        if (now - crew.StuckSince.Value < StuckTime)
            return;

        crew.StuckSince = null;
        crew.ReverseUntil = now + StuckReverseTime;
        crew.ReverseSteering = steering > 0f ? -1f : 1f;
        if (++crew.StuckCount < MaxStucks)
        {
            crew.RouteGoal = null;
            return;
        }

        crew.StuckCount = 0;
        if (crew.Route.Count > 0)
            crew.Route.RemoveAt(0);
        else if (crew.Behaviour == WFVehicleBehaviour.Patrol && crew.Waypoints.Count > 0)
            crew.WaypointIndex = (crew.WaypointIndex + 1) % crew.Waypoints.Count;
    }

    /// <summary>Writes the controls the vehicle's chassis reads: throttle and wheel when it steers freely, a grid cardinal otherwise.</summary>
    private static void SetInput(WFVehicleAutopilotComponent autopilot, GridVehicleMoverComponent mover, float throttle,
        float steering, Vector2i direction)
    {
        if (mover.FreeSteering)
        {
            autopilot.Throttle = throttle;
            autopilot.Steering = steering;
            autopilot.Direction = Vector2i.Zero;
            return;
        }

        autopilot.Throttle = 0f;
        autopilot.Steering = 0f;
        autopilot.Direction = throttle == 0f ? Vector2i.Zero : direction;
    }

    /// <summary>The cardinal of the vehicle's grid nearest a world-space direction.</summary>
    private Vector2i GridCardinal(EntityUid uid, Vector2 worldDelta)
    {
        if (worldDelta.LengthSquared() < 0.0001f)
            return Vector2i.Zero;

        var gridRotation = Transform(uid).GridUid is { } grid ? _transform.GetWorldRotation(grid) : Angle.Zero;
        var local = (-gridRotation).RotateVec(worldDelta);
        return MathF.Abs(local.X) >= MathF.Abs(local.Y)
            ? new Vector2i(MathF.Sign(local.X), 0)
            : new Vector2i(0, MathF.Sign(local.Y));
    }

    /// <summary>Plans a route when the goal is new or has moved, at most every <see cref="RepathInterval"/> for a moving one.</summary>
    private void UpdateRoute(EntityUid uid, WFVehicleCrewComponent crew, MapCoordinates here, MapCoordinates goal, TimeSpan now)
    {
        if (crew.RouteGoal is { } planned && Distance(planned, goal) <= RepathDrift)
            return;

        // A leg planned on the way here is taken up as it is: the route carries straight on, with no stall to plan it.
        if (crew.NextRouteGoal is { } preplanned && crew.NextPathCancel == null && Distance(preplanned, goal) <= RepathDrift)
        {
            crew.PathCancel?.Cancel();
            crew.PathCancel = null;
            (crew.Route, crew.NextRoute) = (crew.NextRoute, crew.Route);
            crew.NextRoute.Clear();
            crew.NextRouteGoal = null;
            crew.RouteGoal = goal;
            crew.NextRepath = now + RepathInterval;
            return;
        }

        if (crew.RouteGoal != null && now < crew.NextRepath)
            return;

        crew.RouteGoal = goal;
        crew.NextRepath = now + RepathInterval;
        crew.Route.Clear();
        crew.PathCancel?.Cancel();
        crew.PathCancel = null;

        // Without a path the driver heads straight for the goal: a short hop, or a clear run such as down a road.
        if (Distance(here, goal) > DirectRange && !ClearRun(uid, here.Position, goal.Position))
            RequestPath(uid, crew, goal);
    }

    /// <summary>Plans the leg on from the goal to <see cref="WFVehicleCrewComponent.NextGoal"/> ahead of getting there.</summary>
    private void UpdateNextRoute(EntityUid uid, WFVehicleCrewComponent crew, MapCoordinates goal)
    {
        if (crew.NextGoal is not { } next)
            return;

        if (crew.NextRouteGoal is { } planned && Distance(planned, next) <= RepathDrift)
            return;

        crew.NextRouteGoal = next;
        crew.NextRoute.Clear();
        crew.NextPathCancel?.Cancel();
        crew.NextPathCancel = null;
        if (Distance(goal, next) > DirectRange && !ClearRun(uid, goal.Position, next.Position))
            RequestPath(uid, crew, next, goal);
    }

    /// <summary>
    /// Whether the vehicle fits along a straight line: floor all the way, and nothing solid within its half-width of
    /// it. A path's walkway is a person's width, so a vehicle checks for itself.
    /// </summary>
    private bool ClearRun(EntityUid uid, Vector2 from, Vector2 to)
    {
        if (Transform(uid).GridUid is not { } grid || !TryComp<MapGridComponent>(grid, out var gridComp))
            return false;

        var inverse = _transform.GetInvWorldMatrix(grid);
        var start = Vector2.Transform(from, inverse);
        var end = Vector2.Transform(to, inverse);
        var line = end - start;
        var length = line.Length();
        if (length < 0.01f)
            return true;

        var side = new Vector2(-line.Y, line.X) / length;
        var steps = (int) MathF.Ceiling(length / ClearanceStep);
        _clearanceChecked.Clear();
        for (var i = 0; i <= steps; i++)
        {
            var centre = start + line * (i / (float) steps);
            for (var lane = -ClearanceLanes; lane <= ClearanceLanes; lane++)
            {
                var tile = (centre + side * (ClearanceHalfWidth * lane / ClearanceLanes)).Floored();
                if (!_clearanceChecked.Add(tile))
                    continue;
                if (!_map.TryGetTileRef(grid, gridComp, tile, out var tileRef) || tileRef.Tile.IsEmpty || Blocked(uid, grid, gridComp, tile))
                    return false;
            }
        }

        return true;
    }

    /// <summary>Something anchored on a tile that a vehicle would hit: a wall, tree, rock, barrier or the like.</summary>
    private bool Blocked(EntityUid vehicle, EntityUid grid, MapGridComponent gridComp, Vector2i tile)
    {
        var anchored = _map.GetAnchoredEntitiesEnumerator(grid, gridComp, tile);
        while (anchored.MoveNext(out var other))
        {
            if (other != vehicle && TryComp<PhysicsComponent>(other, out var body) && body.CanCollide && body.Hard
                && (body.CollisionLayer & VehicleObstacleMask) != 0)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Asks the pathfinder for a route across the vehicle's grid; nothing happens off a grid with a nav graph. With
    /// <paramref name="from"/> the route is the leg on from there, for after the goal.
    /// </summary>
    private void RequestPath(EntityUid uid, WFVehicleCrewComponent crew, MapCoordinates goal, MapCoordinates? from = null)
    {
        var xform = Transform(uid);
        if (xform.GridUid is not { } grid || !HasComp<GridPathfindingComponent>(grid))
            return;

        var cancel = new CancellationTokenSource();
        if (from != null)
            crew.NextPathCancel = cancel;
        else
            crew.PathCancel = cancel;

        var start = from is { } origin ? _transform.ToCoordinates(grid, origin) : xform.Coordinates;
        _ = FollowPath(uid, cancel, start, _transform.ToCoordinates(grid, goal), from);
    }

    /// <summary>Waits for a path and makes it the route, or the leg after it, unless it was superseded meanwhile.</summary>
    private async Task FollowPath(EntityUid uid, CancellationTokenSource cancel, EntityCoordinates start, EntityCoordinates end,
        MapCoordinates? origin)
    {
        PathResultEvent result;
        try
        {
            result = await _pathfinding.GetPathSafe(uid, start, end, PathRange, cancel.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        var onward = origin != null;
        if (cancel.IsCancellationRequested ||
            !TryComp<WFVehicleCrewComponent>(uid, out var crew) ||
            (onward ? crew.NextPathCancel : crew.PathCancel) != cancel)
        {
            return;
        }

        if (onward)
            crew.NextPathCancel = null;
        else
            crew.PathCancel = null;
        if (result.Result is not (PathResult.Path or PathResult.PartialPath) || result.Path.Count == 0)
            return;

        // Pull the path straight: from each point, on to the furthest one further along that the vehicle has room to
        // drive to directly. What's left are the corners, with the vehicle's width clear between them.
        var points = new List<MapCoordinates>(result.Path.Count);
        foreach (var poly in result.Path)
            points.Add(_transform.ToMapCoordinates(poly.Coordinates));

        var route = onward ? crew.NextRoute : crew.Route;
        route.Clear();
        var from = origin?.Position ?? _transform.GetMapCoordinates(uid).Position;
        var index = 0;
        while (index < points.Count)
        {
            var reach = index;
            for (var ahead = Math.Min(points.Count - 1, index + MaxShortcut); ahead > index; ahead--)
            {
                if (!ClearRun(uid, from, points[ahead].Position))
                    continue;
                reach = ahead;
                break;
            }

            // Nothing ahead in a clear line: follow the path a few polygons on, as before.
            if (reach == index)
                reach = Math.Min(points.Count - 1, index + PathStride - 1);
            route.Add(points[reach]);
            from = points[reach].Position;
            if (reach == points.Count - 1)
                break;
            index = reach + 1;
        }
    }
}
