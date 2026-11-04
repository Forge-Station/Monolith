using System.Linq;
using System.Numerics;
using Content.Server._Mono.NPC.HTN;
using Content.Server._WF.NpcCrew.Components;
using Content.Server.Physics.Controllers;
using Content.Server.Shuttles.Components;
using Content.Shared._NF.Shuttles.Events;
using Content.Shared._WF.NpcCrew;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Player;

namespace Content.Server._WF.NpcCrew.Systems;

public sealed partial class WFPilotDutySystem
{
    [Dependency] private MoverController _navigationMover = default!;

    /// <summary>Applies independent scenario limits without restarting an objective or losing its hold anchor.</summary>
    public bool SetNavigation(Entity<WFPilotDutyComponent?> ent, WFCrewNavigationSettings settings)
    {
        if (!settings.IsValid() || !Resolve(ent, ref ent.Comp))
            return false;
        var replanDock = ent.Comp.Navigation.DockStandoff != settings.DockStandoff
            && ent.Comp.DockPhase is WFDockPhase.Approach or WFDockPhase.Settle;
        ent.Comp.Navigation = settings.Clone();
        if (TryComp<WFCrewComponent>(ent, out var crew))
            crew.Navigation = settings.Clone();
        UpdateOrbitLimits(ent.Comp);
        if (replanDock)
        {
            ent.Comp.DockPlan = null;
            ent.Comp.DockPhase = WFDockPhase.None;
            ent.Comp.DockPhaseTime = 0f;
        }
        if (ent.Comp.EscortOffset != null && ent.Comp.FollowTarget is { } target && !TerminatingOrDeleted(target)
            && Transform(ent).GridUid is { } grid)
        {
            ent.Comp.FollowRange = settings.EscortRange;
            UpdateEscortOffset(ent.Comp, grid, target);
        }
        if (ent.Comp.AtHelm)
            Steer((ent, ent.Comp));
        return true;
    }

    /// <summary>A speed setting as a limit: 0 sets none.</summary>
    private static float SpeedLimit(float setting) => setting > 0f ? setting : float.PositiveInfinity;

    /// <summary>The cruise speed a travel estimate may count on: the setting, or with none the hull's own top speed.</summary>
    public float TravelSpeed(WFPilotDutyComponent duty, EntityUid grid)
    {
        if (duty.CruiseSpeed > 0f)
            return duty.CruiseSpeed;

        return TryComp<ShuttleComponent>(grid, out var shuttle) ? MathF.Max(shuttle.BaseMaxLinearVelocity, 1f) : DefaultHullSpeed;
    }

    /// <summary>The top speed assumed for a grid with no shuttle component.</summary>
    private const float DefaultHullSpeed = 30f;

    private static void UpdateOrbitLimits(WFPilotDutyComponent duty)
    {
        duty.LoiterRadius = duty.OrbitKind == WFCrewObjectiveKind.Attack
            ? MathF.Max(duty.RequestedLoiterRadius, duty.Navigation.AttackRange) : duty.RequestedLoiterRadius;
        duty.LoiterSpeed = duty.LoiterSpeedOverride ?? (duty.OrbitKind switch
        {
            WFCrewObjectiveKind.Circle => duty.Navigation.CircleSpeed,
            WFCrewObjectiveKind.Attack => duty.Navigation.AttackSpeed,
            _ => duty.Navigation.LoiterSpeed,
        });
    }

    private void UpdateEscortOffset(WFPilotDutyComponent duty, EntityUid grid, EntityUid target)
    {
        var spacing = MathF.Max(duty.EscortSpacing, GridRadius(grid) + GridRadius(target) + duty.Navigation.NavigationClearance);
        var row = duty.EscortSlot / 2 + 1;
        var center = TryComp<MapGridComponent>(target, out var targetGrid) ? targetGrid.LocalAABB.Center : Vector2.Zero;
        duty.EscortOffset = center + GridForwardAngle(duty, target).RotateVec(new Vector2((duty.EscortSlot % 2 == 0 ? -1 : 1) * spacing, -row * spacing));
    }

    private void CaptureHold(Entity<WFPilotDutyComponent> ent)
    {
        if (Transform(ent).GridUid is not { } grid || Transform(grid).MapUid is not { } map)
            return;
        ent.Comp.HoldPosition = new EntityCoordinates(map, _transform.GetWorldPosition(grid));
        ent.Comp.HoldHeading = _transform.GetWorldRotation(grid) + new Angle(Math.PI);
        ent.Comp.CorrectingHold = false;
    }

    /// <summary>
    /// Re-issues orders left on the previous map by a jump: Hold re-anchors, GoTo drops points on other maps and
    /// completes once none remain, and a back-off point is planned again.
    /// </summary>
    private void OnMapChanged(Entity<WFPilotDutyComponent> ent, MapId map)
    {
        var duty = ent.Comp;
        duty.ResumeWaypoints = KeepOnMap(duty.ResumeWaypoints, 0, map);
        switch (duty.Orders)
        {
            case WFPilotOrder.Hold:
                CaptureHold(ent);
                break;
            case WFPilotOrder.GoTo:
                duty.Waypoints = KeepOnMap(duty.Waypoints, duty.WaypointIndex, map);
                duty.WaypointIndex = 0;
                if (duty.Waypoints.Count == 0)
                {
                    CompleteOrders(ent);
                    return;
                }
                break;
            case WFPilotOrder.Undock when duty.Waypoints.Count > 0 && !IsOnMap(duty.Waypoints[0], map):
                // Plans the back-off again, or finds nothing docked and moves on.
                duty.Waypoints = new List<EntityCoordinates>();
                duty.WaypointIndex = 0;
                break;
        }
        if (duty.AtHelm)
            Steer(ent);
    }

    /// <summary>Whether the grid is starting, flying or finishing an FTL jump.</summary>
    private bool InTransit(EntityUid? grid)
    {
        return grid is { } uid && (HasComp<Content.Server._WF.Planets.Hop.WFPlanetHopComponent>(uid)
               || TryComp<FTLComponent>(uid, out var ftl)
               && ftl.State is FTLState.Starting or FTLState.Travelling or FTLState.Arriving);
    }

    /// <summary>Whether the grid the current order flies to or around is on another map.</summary>
    private bool TargetElsewhere(WFPilotDutyComponent duty, MapId map)
    {
        EntityUid? target = duty.Orders switch
        {
            WFPilotOrder.Follow => duty.FollowTarget,
            WFPilotOrder.Dock => duty.DockTarget,
            WFPilotOrder.Loiter => duty.LoiterCenter?.EntityId,
            _ => null,
        };
        return target is { } uid && !TerminatingOrDeleted(uid) && Transform(uid).MapID != map;
    }

    /// <summary>
    /// Orders flown as a wait while the ship is docked: holding, waiting to cast off, or a flight held by a visitor
    /// docked to us.
    /// </summary>
    private static bool WaitsWhileDocked(WFPilotDutyComponent duty)
    {
        return duty.Orders is WFPilotOrder.Hold or WFPilotOrder.GoTo or WFPilotOrder.Follow or WFPilotOrder.Loiter
               || duty.Orders == WFPilotOrder.Undock && duty.Waypoints.Count == 0;
    }

    /// <summary>Whether the ship is docked and its orders wait in place, braked and anchored.</summary>
    private static bool DockedWait(WFPilotDutyComponent duty) => duty.Docked && WaitsWhileDocked(duty);

    /// <summary>
    /// The speed a docking run may make: a creep at the port, the approach speed inside the alignment range, and farther
    /// out only what the ship can still brake down from to the approach speed by the time it gets there. A raider's
    /// cruise is set past anything a hull can stop from; without the curve it would carry through the standoff and into
    /// the ship it means to board.
    /// </summary>
    private float DockSpeed(WFPilotDutyComponent duty, ShipSteererComponent steerer, ShuttleComponent shuttle, PhysicsComponent body,
        EntityUid grid, Vector2 position, WFCrewNavigationSettings limits)
    {
        if (duty.DockPhase == WFDockPhase.Creep)
            return duty.DockCreepSpeed;
        if (!FarFromDock(duty, steerer, position, limits))
            return duty.DockApproachSpeed;

        var toWaypoint = _transform.ToMapCoordinates(steerer.Coordinates).Position - position;
        var braking = _navigationMover.GetWorldDirectionAccel(-toWaypoint, shuttle, body, Transform(grid)).Length();
        if (shuttle.AccelerationMultiplier > 0f)
            braking /= shuttle.AccelerationMultiplier;
        braking *= ShuttleComponent.BrakeCoefficient;
        if (braking <= 0f)
            return duty.DockApproachSpeed;

        // What can be shed over the run left before the alignment range, with margin for a hull that brakes unevenly.
        var run = MathF.Max(0f, toWaypoint.Length() - limits.DockAlignmentRange * 1.5f);
        var reachable = MathF.Sqrt(duty.DockApproachSpeed * duty.DockApproachSpeed + 2f * braking * DockBrakingMargin * run);
        return MathF.Min(SpeedLimit(duty.CruiseSpeed), reachable);
    }

    /// <summary>The share of a hull's braking the docking curve counts on.</summary>
    private const float DockBrakingMargin = 0.6f;

    /// <summary>Whether a docking run is still far enough out to fly at cruise speed.</summary>
    private bool FarFromDock(WFPilotDutyComponent duty, ShipSteererComponent steerer, Vector2 position, WFCrewNavigationSettings limits)
    {
        if (duty.DockPhase is not (WFDockPhase.None or WFDockPhase.Approach) || !steerer.Coordinates.IsValid(EntityManager))
            return false;

        var distance = (_transform.ToMapCoordinates(steerer.Coordinates).Position - position).Length();
        return distance > limits.DockAlignmentRange * 1.5f;
    }

    private bool IsOnMap(EntityCoordinates point, MapId map)
    {
        return point.IsValid(EntityManager) && _transform.ToMapCoordinates(point).MapId == map;
    }

    /// <summary>The points from <paramref name="start"/> on that lie on the map.</summary>
    private List<EntityCoordinates> KeepOnMap(List<EntityCoordinates> points, int start, MapId map)
    {
        var kept = new List<EntityCoordinates>();
        for (var i = Math.Max(start, 0); i < points.Count; i++)
        {
            if (IsOnMap(points[i], map))
                kept.Add(points[i]);
        }
        return kept;
    }

    private float SafeOrbitRange(EntityUid grid, EntityCoordinates center, float requested, float clearance)
    {
        var radius = GridRadius(grid) + clearance;
        if (TryComp<MapGridComponent>(center.EntityId, out var target))
            radius += target.LocalAABB.Size.Length() / 2f + (center.Position - target.LocalAABB.Center).Length();
        return MathF.Max(requested, radius);
    }

    /// <summary>Limits real flight speed and turning while keeping full braking authority.</summary>
    private void OnCrewGetInputs(Entity<WFPilotDutyComponent> ent, ref GetShuttleInputsEvent args)
    {
        if (!ent.Comp.AtHelm || HasComp<ActorComponent>(ent) || !args.GotInput
            || !TryComp<ShipSteererComponent>(ent, out var steerer)
            || !TryComp<ShuttleComponent>(args.ShuttleUid, out var shuttle)
            || !TryComp<PhysicsComponent>(args.ShuttleUid, out var body))
            return;

        var duty = ent.Comp;
        var limits = duty.Navigation;
        var grid = args.ShuttleUid;
        var position = _transform.GetWorldPosition(grid);
        var input = args.Input ?? new ShuttleInput(Vector2.Zero, 0f, 0f);
        if (DockedWait(duty))
        {
            // The dock's soft weld alone lets a pair wander; brake it and anchor our hull until the orders move on.
            args.Input = new ShuttleInput(Vector2.Zero, 0f, 1f);
            _shuttle.SetInertiaDampening(grid, body, shuttle, Transform(grid), InertiaDampeningMode.Anchor);
            steerer.Status = ShipSteeringStatus.InRange;
            return;
        }
        if (duty.Orders == WFPilotOrder.Hold && duty.HoldPosition is { } anchor && anchor.IsValid(EntityManager))
        {
            var held = _transform.ToMapCoordinates(anchor);
            // An anchor left on another map is re-captured on the next update; brake until then.
            if (held.MapId != Transform(grid).MapID)
            {
                args.Input = new ShuttleInput(Vector2.Zero, 0f, 1f);
                steerer.Status = ShipSteeringStatus.InRange;
                return;
            }
            var distance = (held.Position - position).Length();
            if (distance > limits.HoldRange)
                duty.CorrectingHold = true;
            else if (distance <= limits.HoldReturnRange)
                duty.CorrectingHold = false;
            steerer.Range = duty.CorrectingHold ? limits.HoldReturnRange : limits.HoldRange;
            steerer.TargetRotation = CreepHeadingOffset(steerer, duty.HoldHeading, grid);
            if (!duty.CorrectingHold && !TurningToThreat(duty, steerer, grid))
            {
                // Brake tiny residual movement without steering toward a changing zero-length direction.
                args.Input = new ShuttleInput(Vector2.Zero, 0f, 1f);
                steerer.Status = body.LinearVelocity.Length() <= limits.ArrivalSpeed && MathF.Abs(body.AngularVelocity) <= limits.HoldAngularSpeed
                    ? ShipSteeringStatus.InRange : ShipSteeringStatus.Moving;
                return;
            }
        }

        var speed = duty.Orders switch
        {
            WFPilotOrder.Hold => limits.HoldCorrectionSpeed,
            WFPilotOrder.Loiter => duty.LoiterSpeed,
            WFPilotOrder.Dock => DockSpeed(duty, steerer, shuttle, body, grid, position, limits),
            WFPilotOrder.Undock => limits.UndockSpeed,
            WFPilotOrder.Follow => limits.FollowSpeed,
            _ => duty.CruiseSpeed,
        };
        // A speed of 0 sets no limit: the hull's thrust and its soft velocity cap are all that hold it back.
        speed = SpeedLimit(speed);
        var target = duty.Orders switch
        {
            WFPilotOrder.Follow => duty.FollowTarget,
            WFPilotOrder.Loiter => duty.LoiterCenter?.EntityId,
            WFPilotOrder.Dock => duty.DockTarget,
            _ => null,
        };
        var targetVelocity = Vector2.Zero;
        if (target is { } leader && !duty.AwaitingTarget && !TerminatingOrDeleted(leader) && TryComp<MapGridComponent>(leader, out var targetGrid))
        {
            if (TryComp<PhysicsComponent>(leader, out var targetBody))
                targetVelocity = targetBody.LinearVelocity;
            // Speeds are relative to the target, so one under way is caught and closed on. A docking run's standoff and
            // final pose already keep clear of the target's hull, so it is not braked for on the way in.
            if (duty.Orders != WFPilotOrder.Dock)
            {
                var center = Vector2.Transform(targetGrid.LocalAABB.Center, _transform.GetWorldMatrix(leader));
                var toTarget = center - position;
                var gap = toTarget.Length() - GridRadius(grid) - targetGrid.LocalAABB.Size.Length() / 2f;
                var relativeVelocity = body.LinearVelocity - targetVelocity;
                var closing = Vector2.Dot(relativeVelocity, ShipSteeringSystem.NormalizedOrZero(toTarget));
                var brakeAcceleration = _navigationMover.GetWorldDirectionAccel(-toTarget, shuttle, body, Transform(grid)).Length();
                if (shuttle.AccelerationMultiplier > 0f)
                    brakeAcceleration /= shuttle.AccelerationMultiplier;
                brakeAcceleration *= ShuttleComponent.BrakeCoefficient;
                var stoppingDistance = brakeAcceleration > 0f ? closing * closing / (2f * brakeAcceleration) : float.PositiveInfinity;
                if (gap < limits.NavigationClearance + stoppingDistance + MathF.Max(closing, 0f) * limits.BrakingLookahead && closing > limits.SpeedTolerance)
                    input = new ShuttleInput(Vector2.Zero, 0f, 1f);
                if (gap < limits.NavigationClearance * 2f && limits.NearTargetSpeed > 0f)
                    speed = MathF.Min(speed, limits.NearTargetSpeed);
            }
        }

        // Native arrival thresholds do not limit transit speed. Feed the mover its actual cruise ceiling.
        var maximumSpeed = speed + targetVelocity.Length();
        if (body.LinearVelocity.Length() > maximumSpeed + limits.SpeedTolerance)
            input = new ShuttleInput(Vector2.Zero, input.Rotation, 1f);
        args.SetMaxVelocity = float.IsFinite(maximumSpeed) ? maximumSpeed : null;
        var handling = WFCrewSkills.Of(CompOrNull<WFCrewComponent>(ent)?.Skill ?? WFCrewSkill.Veteran).Handling;
        args.AccelMul *= input.Brakes > 0f ? 1f : limits.ThrustMultiplier * handling;
        args.AngularMul *= input.Brakes > 0f ? 1f : limits.AngularThrustMultiplier * handling;

        var acceleration = _navigationMover.GetAngularAcceleration(shuttle, body);
        if (shuttle.AngularMultiplier > 0f)
            acceleration = acceleration / shuttle.AngularMultiplier * args.AngularMul;
        if (acceleration > 0f && args.FrameTime > 0f)
        {
            var delta = acceleration * args.FrameTime;
            var desiredRate = Math.Clamp(body.AngularVelocity - input.Rotation * delta, -limits.MaximumTurnRate, limits.MaximumTurnRate);
            input.Rotation = Math.Clamp((body.AngularVelocity - desiredRate) / delta, -1f, 1f);
        }
        args.Input = input;
    }
}
