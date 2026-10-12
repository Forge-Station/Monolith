using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using Content.Server._Mono.NPC.HTN;
using Content.Server._WF.NpcCrew.Components;
using Content.Server.Physics.Controllers;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Events;
using Content.Server.Shuttles.Systems;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.NpcCrew;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;

namespace Content.Server._WF.NpcCrew.Systems;

/// <summary>
/// A hand-flown docking: the dock pair, our grid's pose with the docks mated and the standoff the creep starts from.
/// Positions are on the target grid and the angle is relative to it, so the plan moves with the target.
/// </summary>
public readonly record struct WFDockPlan(
    EntityUid OwnDock,
    EntityUid TargetDock,
    EntityCoordinates Final,
    Angle FinalAngle,
    EntityCoordinates Standoff);

public sealed partial class WFPilotDutySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IMapManager _mapManager = default!;
    [Dependency] private DockingSystem _docking = default!;
    [Dependency] private ShuttleSystem _shuttle = default!;

    /// <summary>Seconds before planning again after an attempt ended without a usable pair.</summary>
    private const float PlanRetryDelay = 5f;

    /// <summary>Touching the target's hull with the docks this close is the docks mating, not a collision.</summary>
    private const float PortContactRange = 2.5f;

    private List<Entity<MapGridComponent>> _laneGrids = new();

    /// <summary>A dock or undock happened since the last update; pilots' docked caches refresh then.</summary>
    private bool _docksChanged;

    /// <summary>
    /// Picks a free dock pair whose approach lane touches no grid but the two, nearest standoff first. The lane is the
    /// box our grid sweeps from its final pose out <paramref name="standoff"/> metres along the target dock's normal.
    /// </summary>
    public bool TryPlanDock(EntityUid grid, EntityUid target, float standoff, out WFDockPlan plan)
    {
        plan = default;
        var gridXform = Transform(grid);
        if (grid == target
            || !TryComp<MapGridComponent>(grid, out var gridComp)
            || !HasComp<MapGridComponent>(target)
            || gridXform.MapUid == null
            || gridXform.MapID != Transform(target).MapID)
        {
            return false;
        }

        var position = _transform.GetWorldPosition(gridXform);
        var targetMatrix = _transform.GetWorldMatrix(target);
        var targetRotation = _transform.GetWorldRotation(target);
        var targetDocks = _docking.GetDocks(target);
        var best = float.MaxValue;
        var found = false;

        foreach (var own in _docking.GetDocks(grid))
        {
            if (own.Comp.Docked || own.Comp.ReceiveOnly)
                continue;

            foreach (var theirs in targetDocks)
            {
                if (theirs.Comp.Docked
                    || _docking.GetDockingConfig(grid, target, own, own.Comp, theirs, theirs.Comp) is not { } config)
                {
                    continue;
                }

                // The target dock's outward normal, on the target grid.
                var normal = Transform(theirs).LocalRotation.RotateVec(new Vector2(0f, -1f));
                var final = config.Coordinates.Position;
                var standoffPoint = final + normal * standoff;
                var distance = (Vector2.Transform(standoffPoint, targetMatrix) - position).LengthSquared();
                if (distance >= best
                    || !LaneClear(grid,
                        target,
                        gridXform.MapID,
                        gridComp.LocalAABB,
                        Vector2.Transform(final, targetMatrix),
                        targetRotation + config.Angle,
                        targetRotation.RotateVec(normal),
                        standoff))
                {
                    continue;
                }

                best = distance;
                found = true;
                plan = new WFDockPlan(own,
                    theirs,
                    new EntityCoordinates(target, final),
                    config.Angle,
                    new EntityCoordinates(target, standoffPoint));
            }
        }

        return found;
    }

    /// <summary>
    /// Whether the box our grid's bounds sweep from the final pose out along the normal touches no grid but ours and
    /// the target. World space.
    /// </summary>
    private bool LaneClear(EntityUid grid,
        EntityUid target,
        MapId map,
        Box2 localBounds,
        Vector2 finalPosition,
        Angle finalRotation,
        Vector2 normal,
        float standoff)
    {
        var across = new Vector2(-normal.Y, normal.X);
        var xAxis = finalRotation.RotateVec(Vector2.UnitX);
        var yAxis = finalRotation.RotateVec(Vector2.UnitY);
        var half = localBounds.Size / 2f;
        var halfAlong = MathF.Abs(Vector2.Dot(normal, xAxis)) * half.X
                        + MathF.Abs(Vector2.Dot(normal, yAxis)) * half.Y;
        var halfAcross = MathF.Abs(Vector2.Dot(across, xAxis)) * half.X
                         + MathF.Abs(Vector2.Dot(across, yAxis)) * half.Y;
        var centre = finalPosition + finalRotation.RotateVec(localBounds.Center);

        // Laid out along +Y from the centre, then turned so +Y is the normal.
        var box = new Box2(-halfAcross, -halfAlong, halfAcross, standoff + halfAlong).Translated(centre);
        var lane = new Box2Rotated(box, new Angle(normal) - new Angle(Vector2.UnitY), centre);

        _laneGrids.Clear();
        _mapManager.FindGridsIntersecting(map, lane, ref _laneGrids, includeMap: false);
        return _laneGrids.All(other => other.Owner == grid || other.Owner == target);
    }

    /// <summary>One update of the Dock order: plan, approach the standoff, settle there, creep in and dock.</summary>
    private void AdvanceDock(Entity<WFPilotDutyComponent> ent, ShipSteererComponent steerer, float frameTime)
    {
        var duty = ent.Comp;
        var xform = Transform(ent);
        if (xform.GridUid is not { } grid || xform.MapUid is not { } map)
            return;

        if (duty.DockTarget is not { } target
            || TerminatingOrDeleted(target)
            || target == grid
            || Transform(target).MapUid != map)
        {
            FailDock(ent, grid, duty.DockTarget ?? EntityUid.Invalid);
            return;
        }

        duty.DockPhaseTime += frameTime;
        if (duty.Docked && _docking.AreGridsDocked(grid, target))
        {
            Docked(ent, grid, target);
            return;
        }
        var collision = duty.DockCollision;
        duty.DockCollision = null;

        if (duty.DockPhase == WFDockPhase.None || duty.DockPlan is not { } plan)
        {
            PlanDock(ent, grid, target);
            return;
        }

        // Docked at the chosen pair by someone else.
        if (TryComp<DockingComponent>(plan.OwnDock, out var docked) && docked.DockedWith == plan.TargetDock)
        {
            Docked(ent, grid, target);
            return;
        }

        if (!TryFreeDock(plan.OwnDock, out var ownDock) || !TryFreeDock(plan.TargetDock, out var targetDock))
        {
            duty.DockPlan = null;
            AbortDock(ent, WFDockPhase.None);
            return;
        }

        steerer.InRangeRotation = DockHeading(plan, target);
        steerer.AlwaysFaceTarget = true;
        var cruising = duty.DockPhase == WFDockPhase.Approach
            && (_transform.ToMapCoordinates(plan.Standoff).Position - _transform.GetWorldPosition(grid)).Length() > MathF.Max(duty.Navigation.DockAlignmentRange, duty.DockStandoff * 2f);
        steerer.TargetRotation = cruising
            ? TravelHeadingOffset(ent) : CreepHeadingOffset(steerer, DockHeading(plan, target), grid);
        if (cruising)
            steerer.InRangeRotation = null;
        switch (duty.DockPhase)
        {
            case WFDockPhase.Approach:
                var standoff = _transform.ToMapCoordinates(plan.Standoff).Position;
                var shipPosition = _transform.GetWorldPosition(grid);
                // Map coordinates, so the target's hull is avoided on the way; kept up with a moving target. Out in
                // front of the port with a straight run in, the target's own coordinates leave its hull out of the
                // avoidance, so a standoff inside its avoidance circle can still be reached; other grids still count.
                steerer.Coordinates = InDockCorridor(grid, target, plan, xform.MapID, shipPosition, standoff, duty.DockStandoff)
                    ? plan.Standoff
                    : StandoffOnMap(plan, map);
                // The cleared docking corridor intentionally enters the destination's avoidance buffer.
                var toStandoff = standoff - shipPosition;
                if (toStandoff.Length() <= duty.DockStandoff)
                    steerer.AvoidCollisions = false;
                if (duty.DockPhaseTime >= MathF.Max(duty.DockApproachBudget, duty.Navigation.DockApproachTimeout))
                {
                    AbortDock(ent, WFDockPhase.None);
                    return;
                }
                if (steerer.Status != ShipSteeringStatus.InRange)
                    break;

                // Out of attempts: give up here, at the standoff.
                if (duty.DockAttempts >= duty.DockMaxAttempts)
                {
                    GiveUpDock(ent, grid, target);
                    return;
                }

                SetDockPhase(ent, WFDockPhase.Settle);
                break;
            case WFDockPhase.Settle:
                steerer.Coordinates = StandoffOnMap(plan, map);
                steerer.AvoidCollisions = false;
                if (Settled(grid, target, duty.Navigation))
                    SetDockPhase(ent, WFDockPhase.Creep);
                else if (duty.DockPhaseTime >= duty.Navigation.DockSettleTimeout)
                    AbortDock(ent, WFDockPhase.None);
                break;
            case WFDockPhase.Creep:
                steerer.TargetRotation = CreepHeadingOffset(steerer, DockHeading(plan, target), grid);
                if (_docking.CanDock((plan.OwnDock, ownDock), (plan.TargetDock, targetDock)))
                {
                    _docking.Dock((plan.OwnDock, ownDock), (plan.TargetDock, targetDock));
                    Docked(ent, grid, target);
                    return;
                }

                var hitElsewhere = collision is { } hit
                                   && (hit != target || PortDistance(plan) > PortContactRange);
                if (hitElsewhere || duty.DockPhaseTime >= duty.DockCreepTimeout)
                    AbortDock(ent, WFDockPhase.Approach);
                break;
        }
    }

    /// <summary>
    /// Plans an attempt, or gives up once the attempts are spent. A plan that finds no pair is a failed attempt.
    /// </summary>
    private void PlanDock(Entity<WFPilotDutyComponent> ent, EntityUid grid, EntityUid target)
    {
        var duty = ent.Comp;
        if (duty.DockAttempts >= duty.DockMaxAttempts)
        {
            GiveUpDock(ent, grid, target);
            return;
        }

        if (duty.DockAttempts > 0 && duty.DockPhaseTime < PlanRetryDelay)
            return;

        if (!TryPlanDock(grid, target, duty.DockStandoff, out var plan))
        {
            duty.DockAttempts++;
            duty.DockPhaseTime = 0f;
            return;
        }

        duty.DockPlan = plan;
        SetDockPhase(ent, WFDockPhase.Approach);
    }

    /// <summary>Counts a failed attempt and goes back: Approach retries the pair, None plans another.</summary>
    private void AbortDock(Entity<WFPilotDutyComponent> ent, WFDockPhase phase)
    {
        ent.Comp.DockAttempts++;
        SetDockPhase(ent, phase);
    }

    private void SetDockPhase(Entity<WFPilotDutyComponent> ent, WFDockPhase phase)
    {
        ent.Comp.DockPhase = phase;
        ent.Comp.DockPhaseTime = 0f;
        if (phase == WFDockPhase.Approach)
            ent.Comp.DockApproachBudget = ApproachBudget(ent);
        Steer(ent);
    }

    /// <summary>
    /// The approach timeout on top of the flight to the standoff at cruise speed, with a margin, so a long leg doesn't
    /// spend attempts in transit.
    /// </summary>
    private float ApproachBudget(Entity<WFPilotDutyComponent> ent)
    {
        var duty = ent.Comp;
        var budget = duty.Navigation.DockApproachTimeout;
        if (duty.DockPlan is not { } plan || Transform(ent).GridUid is not { } grid || TerminatingOrDeleted(plan.Standoff.EntityId))
            return budget;

        var distance = (_transform.ToMapCoordinates(plan.Standoff).Position - _transform.GetWorldPosition(grid)).Length();
        return budget + distance / MathF.Max(duty.CruiseSpeed, 1f) * 1.5f;
    }

    /// <summary>
    /// Whether the ship is out in front of the target's port, near the standoff, with a straight run to the standoff
    /// that clears the target's hull.
    /// </summary>
    private bool InDockCorridor(EntityUid grid,
        EntityUid target,
        WFDockPlan plan,
        MapId map,
        Vector2 shipPosition,
        Vector2 standoff,
        float standoffDistance)
    {
        var outward = standoff - _transform.ToMapCoordinates(plan.Final).Position;
        var run = shipPosition - standoff;
        var length = run.Length();
        var clearance = GridRadius(grid);
        if (Vector2.Dot(run, outward) <= 0f || length > standoffDistance + GridRadius(target) + clearance)
            return false;

        // Laid out along +Y from the standoff to the ship, then turned onto the run.
        var box = new Box2(-clearance, -clearance, clearance, length + clearance).Translated(standoff);
        var path = new Box2Rotated(box, new Angle(run) - new Angle(Vector2.UnitY), standoff);
        _laneGrids.Clear();
        _mapManager.FindGridsIntersecting(map, path, ref _laneGrids, includeMap: false);
        return _laneGrids.All(other => other.Owner != target);
    }

    /// <summary>Out of attempts: jump onto a port if the server allows it, else hold where the ship is.</summary>
    private void GiveUpDock(Entity<WFPilotDutyComponent> ent, EntityUid grid, EntityUid target)
    {
        // Not TryFTLDock: with no pair that fits it hops the ship next to the target, which would hide the failure.
        if (_cfg.GetCVar(NpcCrewCVars.DockFtlFallback) && _docking.GetDockingConfig(grid, target) is { } config)
        {
            _shuttle.FTLDock((grid, Transform(grid)), config);
            Docked(ent, grid, target);
            return;
        }

        FailDock(ent, grid, target);
    }

    private void FailDock(Entity<WFPilotDutyComponent> ent, EntityUid grid, EntityUid target)
    {
        SetOrders(ent, WFPilotOrder.Hold);
        var ev = new WFPilotDockFailedEvent(ent, grid, target);
        RaiseLocalEvent(ent, ref ev, true);
    }

    private void Docked(Entity<WFPilotDutyComponent> ent, EntityUid grid, EntityUid target)
    {
        // A fresh steerer for Hold, without the creep's settings.
        _steering.Stop(ent.Owner);
        SetOrders(ent, WFPilotOrder.Hold);
        ent.Comp.OrdersCompleted = true;
        var ev = new WFPilotDockedEvent(ent, grid, target);
        RaiseLocalEvent(ent, ref ev, true);
    }

    private static void ResetDock(WFPilotDutyComponent duty)
    {
        duty.DockPhase = WFDockPhase.None;
        duty.DockAttempts = 0;
        duty.DockPhaseTime = 0f;
        duty.DockPlan = null;
        duty.DockCollision = null;
        duty.AbsentCrewWaited = 0f;
        duty.NextAbsentCheck = TimeSpan.Zero;
    }

    /// <summary>
    /// Re-reads whether any dock on the pilot's grid is docked, caching the answer. A wait that starts or ends with it
    /// is steered afresh.
    /// </summary>
    private bool RefreshDocked(Entity<WFPilotDutyComponent> ent)
    {
        var wasDocked = ent.Comp.Docked;
        ent.Comp.NextDockCheck = _timing.CurTime + DockCheckInterval;
        ent.Comp.Docked = false;
        if (Transform(ent).GridUid is { } grid)
        {
            foreach (var dock in _docking.GetDocks(grid))
            {
                if (!dock.Comp.Docked || ent.Comp.FaceThreats && ShareFlag(grid, dock.Comp.DockedWith))
                    continue;
                ent.Comp.Docked = true;
                break;
            }
        }
        if (ent.Comp.Docked != wasDocked && ent.Comp.AtHelm && WaitsWhileDocked(ent.Comp))
            Steer(ent);
        return ent.Comp.Docked;
    }

    /// <summary>Whether the grid a dock is docked to flies the same faction's flag, so a garrison berthed on a facing hull is not a berth.</summary>
    private bool ShareFlag(EntityUid grid, EntityUid? other)
    {
        return other is { } dock && Transform(dock).GridUid is { } alongside
               && WFCompanyFamily.Same(CompOrNull<Content.Shared._Mono.Company.CompanyComponent>(grid)?.CompanyName.Id,
                   CompOrNull<Content.Shared._Mono.Company.CompanyComponent>(alongside)?.CompanyName.Id);
    }

    // GetDocks must not run inside a dock handler; the caches refresh on the next update.
    private void OnDockChanged(DockEvent args) => _docksChanged = true;

    private void OnUndockChanged(UndockEvent args) => _docksChanged = true;

    /// <summary>A dock that still exists, is anchored and is docked with nothing.</summary>
    private bool TryFreeDock(EntityUid uid, [NotNullWhen(true)] out DockingComponent? dock)
    {
        dock = null;
        return !TerminatingOrDeleted(uid)
               && Transform(uid).Anchored
               && TryComp(uid, out dock)
               && dock.DockedWith == null;
    }

    /// <summary>Whether the ship has stopped moving relative to the target and stopped turning.</summary>
    private bool Settled(EntityUid grid, EntityUid target, WFCrewNavigationSettings limits)
    {
        if (!TryComp<PhysicsComponent>(grid, out var body))
            return true;

        var targetVelocity = TryComp<PhysicsComponent>(target, out var targetBody)
            ? targetBody.LinearVelocity
            : Vector2.Zero;

        return (body.LinearVelocity - targetVelocity).Length() <= limits.DockSettleSpeed
               && MathF.Abs(body.AngularVelocity) <= limits.DockSettleTurnRate;
    }

    /// <summary>
    /// The steerer's heading for our grid at the final pose. The steerer measures heading as world rotation plus a
    /// half turn.
    /// </summary>
    private Angle DockHeading(WFDockPlan plan, EntityUid target)
    {
        return _transform.GetWorldRotation(target) + plan.FinalAngle + new Angle(Math.PI);
    }

    /// <summary>
    /// The creep's <see cref="ShipSteererComponent.TargetRotation"/>, in degrees. The steerer only turns to
    /// <c>InRangeRotation</c> once in range; on the way in it faces the target plus this offset, so the offset that
    /// keeps the final heading is worked out every update. It is added in range too, so there it is zero.
    /// </summary>
    private float CreepHeadingOffset(ShipSteererComponent steerer, Angle heading, EntityUid grid)
    {
        var toTarget = _transform.ToMapCoordinates(steerer.Coordinates).Position - _transform.GetWorldPosition(grid);
        if (toTarget.Length() <= steerer.Range)
            return 0f;

        return (float) ShipSteeringSystem.ShortestAngleDistance(toTarget.ToWorldAngle(), heading).Degrees;
    }

    private EntityCoordinates StandoffOnMap(WFDockPlan plan, EntityUid map)
    {
        return new EntityCoordinates(map, _transform.ToMapCoordinates(plan.Standoff).Position);
    }

    private float PortDistance(WFDockPlan plan)
    {
        return (_transform.GetWorldPosition(plan.OwnDock) - _transform.GetWorldPosition(plan.TargetDock)).Length();
    }

    /// <summary>
    /// Undock: lets go of every docked port and backs off away from them. Nothing docked: hold. Waits a while for
    /// crew posted aboard who are off the ship, but not during an evasion.
    /// </summary>
    private void ReleaseDocks(Entity<WFPilotDutyComponent> ent, float frameTime)
    {
        var xform = Transform(ent);
        if (xform.GridUid is not { } grid || xform.MapUid is not { } map)
            return;

        // Nothing to cast off from, so nobody to wait for.
        if (!ent.Comp.Docked && !RefreshDocked(ent))
        {
            CompleteOrders(ent);
            return;
        }

        if (ent.Comp.AbsentCrewWaited < ent.Comp.AbsentCrewWait
            && !EntityManager.System<WFCaptainSystem>().IsCourseSuspended(ent)
            && CrewAbsent(ent, grid))
        {
            ent.Comp.AbsentCrewWaited += frameTime;
            return;
        }

        var away = Vector2.Zero;
        foreach (var dock in _docking.GetDocks(grid).ToArray())
        {
            if (!_docking.CanUndock((dock.Owner, dock.Comp)))
                continue;

            // Our dock faces what we leave; back off the other way.
            away -= _transform.GetWorldRotation(dock).RotateVec(new Vector2(0f, -1f));
            _docking.Undock(dock);
        }

        // Nothing was docked, or ports on opposite sides cancel out.
        if (away.LengthSquared() < 0.01f)
        {
            CompleteOrders(ent);
            return;
        }

        var point = _transform.GetWorldPosition(grid) + away.Normalized() * ent.Comp.DockStandoff;
        ent.Comp.Waypoints = new List<EntityCoordinates> { new(map, point) };
        ent.Comp.WaypointIndex = 0;
        Steer(ent);
    }

    /// <summary>Whether a living NPC crewman posted to the grid is off it, re-read at most once a second.</summary>
    private bool CrewAbsent(Entity<WFPilotDutyComponent> ent, EntityUid grid)
    {
        var now = _timing.CurTime;
        if (now < ent.Comp.NextAbsentCheck)
            return ent.Comp.CrewAway;

        ent.Comp.NextAbsentCheck = now + DockCheckInterval;
        ent.Comp.CrewAway = false;
        var crewQuery = EntityQueryEnumerator<WFCrewComponent, TransformComponent>();
        while (crewQuery.MoveNext(out var member, out var crew, out var location))
        {
            if (crew.Post is { } post && post.EntityId == grid && location.GridUid != grid
                && _mobState.IsAlive(member) && !HasComp<Robust.Shared.Player.ActorComponent>(member))
            {
                ent.Comp.CrewAway = true;
                break;
            }
        }
        return ent.Comp.CrewAway;
    }

    /// <summary>Remembers a grid hit during the creep; the next update decides whether it ends the attempt.</summary>
    private void OnShuttleCollide(Entity<WFPilotDutyComponent> ent,
        ref PilotedShuttleRelayedEvent<StartCollideEvent> args)
    {
        var other = args.Args.OtherEntity;
        if (ent.Comp.Orders != WFPilotOrder.Dock
            || ent.Comp.DockPhase != WFDockPhase.Creep
            || !HasComp<MapGridComponent>(other))
        {
            return;
        }

        // A hit on anything but the target outweighs touching the target.
        if (ent.Comp.DockCollision == null || ent.Comp.DockCollision == ent.Comp.DockTarget)
            ent.Comp.DockCollision = other;
    }
}
