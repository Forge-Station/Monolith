using System;
using System.Collections.Generic;
using System.Numerics;
using Content.Shared._WF.Vehicles.Base;
using Content.Shared._WF.Planets.Parachute;
using Content.Shared._WF.Vehicles.Core;
using Content.Shared._WF.Vehicles.Grid.Components;
using Content.Shared._WF.Vehicles.Hardpoint;
using Content.Shared.Throwing;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;

namespace Content.Shared._WF.Vehicles.Grid;

/// <summary>One fixed step of driving: facing, turning, lane steering, speed and the transform write.</summary>
public sealed partial class GridVehicleMoverSystem : EntitySystem
{
    private const float MinVehicleSpeed = 0.01f;
    private const float MinMoveDistance = 0.0001f;
    private const float MinTurnRate = 0.001f;

    /// <summary>The least share of a blocked step that is worth sliding with; below it the contact is a square hit.</summary>
    private const float MinSlideShare = 0.2f;

    /// <summary>The networked part of the mover's state, compared before and after a step so an idle vehicle sends nothing.</summary>
    private readonly record struct MoverSnapshot(
        Vector2i CurrentTile,
        Vector2i TargetTile,
        Vector2 Position,
        Vector2 TargetPosition,
        Vector2i CurrentDirection,
        float CurrentSpeed,
        Angle Heading,
        float AngularVelocityDegrees,
        TimeSpan NextTurnTime,
        TimeSpan InPlaceTurnBlockUntil,
        bool IsCommittedToMove,
        bool IsMoving,
        float SmashSlowdownMultiplier,
        TimeSpan SmashSlowdownUntil)
    {
        public static MoverSnapshot Of(GridVehicleMoverComponent mover)
        {
            return new MoverSnapshot(
                mover.CurrentTile,
                mover.TargetTile,
                mover.Position,
                mover.TargetPosition,
                mover.CurrentDirection,
                mover.CurrentSpeed,
                mover.Heading,
                mover.AngularVelocityDegrees,
                mover.NextTurnTime,
                mover.InPlaceTurnBlockUntil,
                mover.IsCommittedToMove,
                mover.IsMoving,
                mover.SmashSlowdownMultiplier,
                mover.SmashSlowdownUntil);
        }
    }

    private void UpdateMovement(
        EntityUid uid,
        GridVehicleMoverComponent mover,
        RMCVehicleComponent vehicle,
        EntityUid grid,
        MapGridComponent gridComp,
        VehicleControlInput input,
        float frameTime)
    {
        var before = MoverSnapshot.Of(mover);
        _driveProbe = false;

        StepMovement(uid, mover, vehicle, grid, gridComp, input, frameTime);
        AdvanceImpact(uid, mover, grid, frameTime);
        StabiliseBody(uid);

        if (MoverSnapshot.Of(mover) != before || mover.TravelVelocity != Vector2.Zero)
            Dirty(uid, mover);
    }

    /// <summary>Advances collision momentum without allowing the hull to tunnel through blockers.</summary>
    private void AdvanceImpact(EntityUid uid, GridVehicleMoverComponent mover, EntityUid grid, float frameTime)
    {
        if (mover.ImpactVelocity.LengthSquared() < 0.0001f && MathF.Abs(mover.ImpactSpin) < 0.01f)
            return;

        var rotation = GetCollisionLocalRotation(uid, grid, null);
        TryMoveContinuous(uid, mover, grid, mover.Position + mover.ImpactVelocity * frameTime, rotation,
            out var blocked, applyBlockEffects: false, allowVehiclePush: false);
        mover.ImpactVelocity = blocked ? Vector2.Zero : mover.ImpactVelocity * MathF.Exp(-2f * frameTime);
        var desired = rotation + Angle.FromDegrees(mover.ImpactSpin * frameTime);
        if (CanOccupyTransform(uid, mover, grid, mover.Position, desired, Clearance, applyEffects: false, allowVehiclePush: false))
        {
            mover.Heading = desired;
            mover.CurrentDirection = GridVehicleGeometry.CardinalFromRotation(desired);
            _transform.SetLocalRotation(uid, desired);
        }
        else
        {
            mover.ImpactSpin = 0f;
        }

        mover.ImpactSpin *= MathF.Exp(-3f * frameTime);
        SetGridPosition(uid, grid, mover.Position);
        Dirty(uid, mover);
    }

    private void StabiliseBody(EntityUid uid)
    {
        if (!physicsQ.TryComp(uid, out var body))
            return;

        if (body.BodyStatus != BodyStatus.OnGround)
            _physics.SetBodyStatus(uid, body, BodyStatus.OnGround);

        if (body.LinearVelocity != Vector2.Zero)
            _physics.SetLinearVelocity(uid, Vector2.Zero, body: body);

        if (body.AngularVelocity != 0f)
            _physics.SetAngularVelocity(uid, 0f, body: body);
    }

    private void StepMovement(
        EntityUid uid,
        GridVehicleMoverComponent mover,
        RMCVehicleComponent vehicle,
        EntityUid grid,
        MapGridComponent gridComp,
        VehicleControlInput input,
        float frameTime)
    {
        if (vehicle.Operator != null)
        {
            var canRunEvent = new VehicleCanRunEvent((uid, vehicle));
            RaiseLocalEvent(uid, ref canRunEvent);
            if (!canRunEvent.CanRun)
            {
                StopMover(mover);
                SetGridPosition(uid, grid, mover.Position);
                return;
            }
        }

        if (TryComp(uid, out WFVehicleEngineComponent? engine) && (!engine.Running || engine.ParkingBrake))
            input = default;

        if (TryComp(uid, out WFParachutedComponent? chute) && chute.Deployed)
            input = default;

        // Smash slowdowns expire independently of input, so check this before speed selection.
        GetSmashSlowdownMultiplier(mover);

        mover.IsCommittedToMove = false;

        var moved = mover.FreeSteering
            ? UpdateSteeredMovement(uid, mover, grid, gridComp, input.Throttle, input.Steering, frameTime)
            : UpdateDriveMovement(uid, mover, grid, gridComp, input.Direction, frameTime);

        UpdateDerivedTileState(grid, gridComp, mover);
        mover.IsMoving = MathF.Abs(mover.CurrentSpeed) > MinVehicleSpeed ||
                         (mover.FreeSteering && mover.TurnInPlace && MathF.Abs(mover.AngularVelocityDegrees) > MinTurnRate);

        if (mover.IsMoving)
            PlayRunningSound(uid);

        SetGridPosition(uid, grid, mover.Position);

        if (moved || mover.IsMoving)
            _physics.WakeBody(uid);
    }

    /// <summary>
    /// One step of car-style driving: the chassis turns at its own rate while it rolls (or on the spot, for a vehicle
    /// that can), and moves along whatever heading it has.
    /// </summary>
    private bool UpdateSteeredMovement(
        EntityUid uid,
        GridVehicleMoverComponent mover,
        EntityUid grid,
        MapGridComponent gridComp,
        float throttle,
        float steering,
        float frameTime)
    {
        if (mover.CurrentDirection == Vector2i.Zero)
        {
            if (throttle == 0f && steering == 0f)
            {
                mover.CurrentSpeed = 0f;
                mover.AngularVelocityDegrees = 0f;
                return false;
            }

            // First touch of the controls: the heading is wherever the vehicle was left pointing.
            mover.Heading = GetCollisionLocalRotation(uid, grid, null).Reduced();
            mover.CurrentDirection = GridVehicleGeometry.CardinalFromRotation(mover.Heading);
        }

        var rotation = mover.Heading;
        if (!GridVehicleMotionSimulator.CanSteer(mover.TurnInPlace, mover.CurrentSpeed))
        {
            // Wheels cannot build or keep a turn while the vehicle stands still.
            mover.AngularVelocityDegrees = 0f;
        }
        else
        {
            var effectiveSteering = GridVehicleMotionSimulator.GetEffectiveSteering(steering, mover.CurrentSpeed, throttle);
            var targetTurnRate = effectiveSteering * MathF.Max(0f, mover.MaxRotationSpeedDegrees);
            var turnAcceleration = steering == 0f
                ? mover.RotationDecelerationDegrees
                : mover.RotationAccelerationDegrees;
            mover.AngularVelocityDegrees = StepTowards(
                mover.AngularVelocityDegrees,
                targetTurnRate,
                MathF.Max(0f, turnAcceleration) * frameTime);
        }

        // Finishes a nearly square heading for the driver at parking speed, through the same checks as steering.
        if (steering == 0f && throttle != 0f && mover.AlignmentAssistDegrees > 0f &&
            GridVehicleMotionSimulator.CanSteer(mover.TurnInPlace, mover.CurrentSpeed) &&
            MathF.Abs(mover.CurrentSpeed) <= mover.AlignmentAssistMaxSpeed)
        {
            var aligned = DirectionToVehicleRotation(GridVehicleGeometry.CardinalFromRotation(rotation));
            var delta = Angle.ShortestDistance(rotation, aligned).Degrees;
            if (Math.Abs(delta) <= mover.AlignmentAssistDegrees)
                mover.AngularVelocityDegrees = (float) Math.Clamp(delta / Math.Max(frameTime, 0.001f), -30f, 30f);
        }

        var turned = false;
        if (MathF.Abs(mover.AngularVelocityDegrees) > MinTurnRate)
        {
            var desired = (rotation + Angle.FromDegrees(mover.AngularVelocityDegrees * frameTime)).Reduced();
            if (CanOccupyTransform(uid, mover, grid, mover.Position, desired, Clearance, applyEffects: true))
            {
                rotation = desired;
                mover.Heading = desired;
                _transform.SetLocalRotation(uid, desired);
                turned = true;
            }
            else
            {
                mover.AngularVelocityDegrees = 0f;
            }
        }

        mover.CurrentDirection = GridVehicleGeometry.CardinalFromRotation(rotation);

        var hasThrottle = throttle != 0f;
        var profile = GetDriveProfile(uid, mover);
        var forwardAxis = new Vector2i(0, 1);
        var speedResult = hasThrottle
            ? GridVehicleMotionSimulator.StepDriveSpeed(
                mover.CurrentSpeed,
                profile,
                forwardAxis,
                throttle < 0f ? -forwardAxis : forwardAxis,
                true,
                isCommittedToMove: false,
                frameTime)
            : new GridVehicleMotionSimulator.DriveSpeedResult(
                GridVehicleMotionSimulator.StepIdleSpeed(mover.CurrentSpeed, GetCoastDeceleration(uid, mover), frameTime),
                false,
                false);

        mover.CurrentSpeed = speedResult.CurrentSpeed;

        var travel = MathF.Abs(mover.CurrentSpeed) * frameTime;
        if (travel <= MinMoveDistance)
        {
            mover.TravelVelocity = Vector2.Zero;
            return turned;
        }

        var forward = rotation.ToWorldVec();
        if (mover.CurrentSpeed < 0f)
            forward = -forward;

        var speedBeforeMove = mover.CurrentSpeed;
        bool blocked;
        bool moved;
        var cardinal = GridVehicleGeometry.CardinalFromRotation(rotation);
        if (steering == 0f && mover.AlignmentAssistDegrees > 0f &&
            MathF.Abs(mover.CurrentSpeed) <= mover.AlignmentAssistMaxSpeed &&
            Math.Abs(Angle.ShortestDistance(rotation, DirectionToVehicleRotation(cardinal)).Degrees) < 0.1)
        {
            // Square to the tiles at parking speed: the lane steering of the four-way drive helps through doorways.
            mover.TravelVelocity = forward * MathF.Abs(mover.CurrentSpeed);
            var direction = mover.CurrentSpeed < 0f ? -cardinal : cardinal;
            moved = TryMoveWithLaneGuidance(uid, mover, grid, gridComp, direction, rotation, travel, frameTime, out blocked);
        }
        else
        {
            _driveProbe = true;
            var desiredVelocity = forward * MathF.Abs(mover.CurrentSpeed);
            var longitudinal = Vector2.Dot(mover.TravelVelocity, forward);
            mover.TravelVelocity += forward * (MathF.Abs(mover.CurrentSpeed) - longitudinal);
            var change = desiredVelocity - mover.TravelVelocity;
            var gripStep = MathF.Max(0f, mover.Grip) * frameTime;
            mover.TravelVelocity = change.LengthSquared() <= gripStep * gripStep
                ? desiredVelocity
                : mover.TravelVelocity + Vector2.Normalize(change) * gripStep;
            var target = mover.Position + mover.TravelVelocity * frameTime;
            moved = TryMoveContinuous(uid, mover, grid, target, rotation, out blocked);

            // Caught on something: what is left of the step is tried along the deck's axes, so a glancing contact slides
            // along the wall. While it scrapes, the vehicle goes no faster than the share of its top speed that points
            // along the wall. A square hit has nothing to keep.
            if (blocked && TrySlide(uid, mover, grid, target - mover.Position, rotation, out var kept))
            {
                blocked = false;
                moved = true;

                var top = mover.CurrentSpeed >= 0f ? GetModifiedMaxSpeed(uid, mover) : GetModifiedMaxReverseSpeed(uid, mover);
                var cap = MathF.Max(0f, top) * kept;
                if (MathF.Abs(mover.CurrentSpeed) > cap)
                    mover.CurrentSpeed = MathF.Sign(mover.CurrentSpeed) * cap;
            }

            _driveProbe = false;
        }

        if (blocked)
        {
            mover.TravelVelocity = Vector2.Zero;
            mover.CurrentSpeed = 0f;
            RaiseVehicleCollision(uid, mover, VehicleCollisionKind.Crash, speedBeforeMove);
        }

        return moved || turned;
    }

    /// <summary>
    /// Moves a blocked hull along one of the grid's axes by that axis' share of the step it could not make, the larger
    /// share first. <paramref name="kept"/> is that share of the step's length.
    /// </summary>
    private bool TrySlide(
        EntityUid uid,
        GridVehicleMoverComponent mover,
        EntityUid grid,
        Vector2 remaining,
        Angle rotation,
        out float kept)
    {
        kept = 0f;
        var length = remaining.Length();
        if (length <= MinMoveDistance)
            return false;

        var alongX = new Vector2(remaining.X, 0f);
        var alongY = new Vector2(0f, remaining.Y);
        var first = MathF.Abs(remaining.X) >= MathF.Abs(remaining.Y) ? alongX : alongY;
        var second = first == alongX ? alongY : alongX;

        foreach (var slide in new[] { first, second })
        {
            var share = slide.Length() / length;
            if (share < MinSlideShare)
                continue;

            if (!TryMoveContinuous(uid, mover, grid, mover.Position + slide, rotation, out var stopped, applyBlockEffects: false, debugProbes: false) ||
                stopped)
            {
                continue;
            }

            kept = share;
            return true;
        }

        return false;
    }

    private static float StepTowards(float current, float target, float maximumDelta)
    {
        if (current < target)
            return MathF.Min(current + maximumDelta, target);

        return current > target
            ? MathF.Max(current - maximumDelta, target)
            : current;
    }

    private bool UpdateDriveMovement(
        EntityUid uid,
        GridVehicleMoverComponent mover,
        EntityUid grid,
        MapGridComponent gridComp,
        Vector2i inputDir,
        float frameTime)
    {
        var hasInput = inputDir != Vector2i.Zero;
        var facing = mover.CurrentDirection;
        var hadFacing = facing != Vector2i.Zero;

        if (hasInput && !hadFacing)
        {
            if (!TryApplyFacing(uid, mover, grid, gridComp, inputDir, startDelay: false, blockAfterTurn: false, allowMoveClearance: false))
            {
                mover.CurrentSpeed = 0f;
                return false;
            }

            facing = mover.CurrentDirection;
            hadFacing = true;
        }

        var reversing = hasInput && hadFacing && inputDir == -facing;
        var turnRequested = hasInput && hadFacing && !reversing && inputDir != facing;
        var turnInPlaceMaxSpeed = MathF.Max(0f, mover.TurnInPlaceMaxSpeed);
        var atTurnSpeed = MathF.Abs(mover.CurrentSpeed) <= turnInPlaceMaxSpeed;

        if (turnRequested)
        {
            if (!CanApplyTurn(mover))
            {
                if (MathF.Abs(mover.CurrentSpeed) <= MinVehicleSpeed)
                {
                    mover.CurrentSpeed = 0f;
                    return false;
                }
            }
            else if (mover.TurnInPlace && atTurnSpeed)
            {
                var turned = TryApplyFacing(uid, mover, grid, gridComp, inputDir, startDelay: true, blockAfterTurn: true, allowMoveClearance: false);
                mover.CurrentSpeed = 0f;
                return turned;
            }
            else if (TryApplyFacing(uid, mover, grid, gridComp, inputDir, startDelay: true, blockAfterTurn: false, allowMoveClearance: true))
            {
            }
            else if (MathF.Abs(mover.CurrentSpeed) <= MinVehicleSpeed)
            {
                mover.CurrentSpeed = 0f;
                return false;
            }
        }

        if (mover.TurnInPlace &&
            hasInput &&
            !reversing &&
            atTurnSpeed &&
            mover.InPlaceTurnBlockUntil > _timing.CurTime)
        {
            mover.CurrentSpeed = GridVehicleMotionSimulator.StepIdleSpeed(
                mover.CurrentSpeed,
                mover.Deceleration,
                frameTime);
            return false;
        }

        if (mover.CurrentDirection == Vector2i.Zero)
        {
            mover.CurrentSpeed = 0f;
            return false;
        }

        var profile = GetDriveProfile(uid, mover);
        var speedResult = hasInput
            ? GridVehicleMotionSimulator.StepDriveSpeed(
                mover.CurrentSpeed,
                profile,
                mover.CurrentDirection,
                inputDir,
                hasInput,
                isCommittedToMove: false,
                frameTime)
            : new GridVehicleMotionSimulator.DriveSpeedResult(
                GridVehicleMotionSimulator.StepIdleSpeed(
                    mover.CurrentSpeed,
                    GetCoastDeceleration(uid, mover),
                    frameTime),
                false,
                false);

        mover.CurrentSpeed = speedResult.CurrentSpeed;
        if (!mover.FreeSteering)
            mover.Heading = DirectionToVehicleRotation(mover.CurrentDirection);

        var travel = MathF.Abs(mover.CurrentSpeed) * frameTime;
        if (travel <= MinMoveDistance)
            return false;

        var moveDir = mover.CurrentSpeed >= 0f
            ? mover.CurrentDirection
            : -mover.CurrentDirection;
        var rotation = DirectionToVehicleRotation(mover.CurrentDirection);
        var speedBeforeMove = mover.CurrentSpeed;
        var moved = TryMoveWithLaneGuidance(
            uid,
            mover,
            grid,
            gridComp,
            moveDir,
            rotation,
            travel,
            frameTime,
            out var blocked);
        if (blocked)
        {
            mover.TravelVelocity = Vector2.Zero;
            mover.CurrentSpeed = 0f;
            RaiseVehicleCollision(uid, mover, VehicleCollisionKind.Crash, speedBeforeMove);
        }

        return moved;
    }

    private bool TryApplyFacing(
        EntityUid uid,
        GridVehicleMoverComponent mover,
        EntityUid grid,
        MapGridComponent gridComp,
        Vector2i desiredFacing,
        bool startDelay,
        bool blockAfterTurn,
        bool allowMoveClearance)
    {
        if (desiredFacing == Vector2i.Zero)
            return false;

        var desiredRot = DirectionToVehicleRotation(desiredFacing);

        Vector2 turnPosition;
        if (mover.TurnNudgeSkipSteps > 0 && mover.TurnNudgeCacheDir == desiredFacing)
        {
            mover.TurnNudgeSkipSteps--;
            return false;
        }

        var immediateClear = TryFindTurnPosition(uid, mover, grid, gridComp, desiredRot, out turnPosition);
        if (!immediateClear &&
            (!allowMoveClearance ||
             !TryFindTransientTurnClearance(uid, mover, grid, desiredFacing, desiredRot, out turnPosition)))
        {
            mover.TurnNudgeCacheDir = desiredFacing;
            mover.TurnNudgeSkipSteps = 2;
            return false;
        }

        mover.TurnNudgeSkipSteps = 0;
        mover.TurnNudgeCacheDir = Vector2i.Zero;

        if (!CanOccupyTransform(uid, mover, grid, turnPosition, desiredRot, Clearance, applyEffects: true))
            return false;

        var turned = mover.CurrentDirection != desiredFacing;
        var moved = turnPosition != mover.Position;
        mover.Position = turnPosition;
        mover.CurrentTile = GetTile(grid, gridComp, mover.Position);
        mover.CurrentDirection = desiredFacing;
        _transform.SetLocalRotation(uid, desiredRot);

        if (turned && startDelay)
        {
            StartTurnDelay(mover);
            if (blockAfterTurn && mover.TurnDelay > 0f)
                mover.InPlaceTurnBlockUntil = _timing.CurTime + TimeSpan.FromSeconds(mover.TurnDelay);
        }

        return turned || moved;
    }

    private bool TryFindTransientTurnClearance(
        EntityUid uid,
        GridVehicleMoverComponent mover,
        EntityUid grid,
        Vector2i desiredFacing,
        Angle desiredRot,
        out Vector2 clearPosition)
    {
        clearPosition = mover.Position;

        var maxDistance = MathF.Max(0f, mover.TurnCollisionGraceDistance);
        if (maxDistance <= 0f)
            return false;

        var forward = new Vector2(desiredFacing.X, desiredFacing.Y);
        if (forward.LengthSquared() <= 0f)
            return false;

        var step = Math.Clamp(mover.MovementProbeStep, 0.02f, 0.5f);
        var steps = Math.Max(1, (int) MathF.Ceiling(maxDistance / step));
        _bypassInitialBlockers.Clear();
        if (CanOccupyTransform(uid, mover, grid, mover.Position, desiredRot, Clearance, applyEffects: false, blockers: _bypassInitialBlockers) ||
            _bypassInitialBlockers.Count == 0)
        {
            return false;
        }

        for (var i = 1; i <= steps; i++)
        {
            var distance = MathF.Min(i * step, maxDistance);
            var sample = mover.Position + forward * distance;
            _bypassSampleBlockers.Clear();
            if (CanOccupyTransform(uid, mover, grid, sample, desiredRot, Clearance, applyEffects: false, blockers: _bypassSampleBlockers))
            {
                clearPosition = sample;
                return true;
            }

            foreach (var blocker in _bypassSampleBlockers)
            {
                if (!_bypassInitialBlockers.Contains(blocker))
                    return false;
            }
        }

        return false;
    }

    private bool TryMoveWithLaneGuidance(
        EntityUid uid,
        GridVehicleMoverComponent mover,
        EntityUid grid,
        MapGridComponent gridComp,
        Vector2i moveDir,
        Angle? rotation,
        float travel,
        float frameTime,
        out bool blocked)
    {
        blocked = false;
        var forward = new Vector2(moveDir.X, moveDir.Y);
        var directTarget = mover.Position + forward * travel;
        var moved = false;

        // The drive itself, forward or reverse, is the only movement that may go over an open tile.
        _driveProbe = true;
        if (CanMoveContinuous(uid, mover, grid, directTarget, rotation, debugProbes: CollisionDebugEnabled))
        {
            AddDebugMovementDecision(uid, grid, mover.Position, directTarget, forward, DebugMovementDecisionKind.DirectClear, true);
            var knownClear = TryMoveKnownClear(uid, mover, grid, directTarget, rotation, out blocked);
            _driveProbe = false;
            return knownClear;
        }

        AddDebugMovementDecision(uid, grid, mover.Position, directTarget, forward, DebugMovementDecisionKind.DirectBlocked, false);

        _driveProbe = false;
        if (TryGetLaneCorrection(uid, mover, grid, gridComp, moveDir, rotation, directTarget, frameTime, out var correction))
            moved = TryApplyLateralCorrection(uid, mover, grid, moveDir, rotation, correction);

        var forwardStart = mover.Position;
        var forwardTarget = mover.Position + forward * travel;
        _driveProbe = true;
        var forwardMoved = TryMoveContinuous(uid, mover, grid, forwardTarget, rotation, out blocked);
        _driveProbe = false;

        AddDebugMovementDecision(
            uid,
            grid,
            forwardStart,
            forwardTarget,
            forward,
            blocked ? DebugMovementDecisionKind.ForwardBlocked : DebugMovementDecisionKind.ForwardAfterCorrection,
            forwardMoved && !blocked);
        return moved || forwardMoved;
    }

    private bool TryApplyLateralCorrection(
        EntityUid uid,
        GridVehicleMoverComponent mover,
        EntityUid grid,
        Vector2i moveDir,
        Angle? rotation,
        float correction)
    {
        var lateralTarget = SetLateralCoordinate(
            mover.Position,
            moveDir,
            GetLateralCoordinate(mover.Position, moveDir) + correction);

        if ((lateralTarget - mover.Position).LengthSquared() <= MinMoveDistance * MinMoveDistance)
            return false;

        var lateralStart = mover.Position;
        var lateralDirection = lateralTarget - lateralStart;
        var moved = TryMoveContinuous(uid, mover, grid, lateralTarget, rotation, out _, applyBlockEffects: false, debugProbes: false);
        AddDebugMovementDecision(
            uid,
            grid,
            lateralStart,
            lateralTarget,
            lateralDirection,
            moved ? DebugMovementDecisionKind.LaneCorrection : DebugMovementDecisionKind.LaneCorrectionFailed,
            moved);

        return moved;
    }

    private static void AddDebugMovementDecision(
        EntityUid uid,
        EntityUid grid,
        Vector2 start,
        Vector2 end,
        Vector2 moveDirection,
        DebugMovementDecisionKind kind,
        bool success)
    {
        if (!MovementDebugEnabled)
            return;

        if (moveDirection.LengthSquared() > 0.0001f)
            moveDirection = Vector2.Normalize(moveDirection);
        else
            moveDirection = Vector2.Zero;

        DebugMovementDecisions.Add(new DebugMovementDecision(uid, grid, start, end, moveDirection, kind, success));
    }

    private bool CanMoveContinuous(
        EntityUid uid,
        GridVehicleMoverComponent mover,
        EntityUid grid,
        Vector2 target,
        Angle? rotation,
        bool debugProbes)
    {
        var start = mover.Position;
        var delta = target - start;
        var distance = delta.Length();
        if (distance <= MinMoveDistance)
            return true;

        var probeStep = Math.Clamp(mover.MovementProbeStep, 0.02f, 0.5f);
        var steps = Math.Max(1, (int) MathF.Ceiling(distance / probeStep));

        for (var i = 1; i <= steps; i++)
        {
            var candidate = start + delta * (i / (float) steps);
            if (!CanOccupyTransform(uid, mover, grid, candidate, rotation, Clearance, applyEffects: false, debug: debugProbes))
                return false;
        }

        return true;
    }

    private bool TryGetLaneCorrection(
        EntityUid uid,
        GridVehicleMoverComponent mover,
        EntityUid grid,
        MapGridComponent gridComp,
        Vector2i moveDir,
        Angle? rotation,
        Vector2 target,
        float frameTime,
        out float correction)
    {
        correction = 0f;

        if (!TryFindBestLaneOffset(uid, mover, grid, gridComp, moveDir, rotation, target, out var laneOffset))
            return false;

        return TryGetLateralCorrection(mover, grid, gridComp, moveDir, target, laneOffset, frameTime, out correction);
    }

    private bool TryGetLateralCorrection(
        GridVehicleMoverComponent mover,
        EntityUid grid,
        MapGridComponent gridComp,
        Vector2i moveDir,
        Vector2 target,
        float laneOffset,
        float frameTime,
        out float correction)
    {
        var targetTile = GetTile(grid, gridComp, target);
        var center = GetTileCenter(targetTile);
        var currentLateral = GetLateralCoordinate(mover.Position, moveDir);
        var desiredLateral = GetLateralCoordinate(center, moveDir) + laneOffset;
        var correctionSpeed = MathF.Max(0f, mover.LaneCorrectionSpeed);
        if (correctionSpeed <= 0f)
        {
            correction = 0f;
            return false;
        }

        var maxCorrection = correctionSpeed * frameTime;
        correction = Math.Clamp(desiredLateral - currentLateral, -maxCorrection, maxCorrection);
        return MathF.Abs(correction) > MinMoveDistance;
    }

    private bool TryFindBestLaneOffset(
        EntityUid uid,
        GridVehicleMoverComponent mover,
        EntityUid grid,
        MapGridComponent gridComp,
        Vector2i moveDir,
        Angle? rotation,
        Vector2 target,
        out float laneOffset)
    {
        laneOffset = 0f;
        var limit = Math.Clamp(mover.TileOffsetLimit, 0f, 2f);
        if (limit <= 0f || moveDir == Vector2i.Zero)
            return false;

        var step = Math.Clamp(mover.TileOffsetStep, 0.01f, limit);
        var targetTile = GetTile(grid, gridComp, target);
        var center = GetTileCenter(targetTile);
        var baseOffset = Math.Clamp(
            GetLateralCoordinate(target, moveDir) - GetLateralCoordinate(center, moveDir),
            -limit,
            limit);
        var sampleSteps = (int) MathF.Ceiling(limit / step);
        var lookahead = Math.Max(1, mover.TileOffsetLookahead);

        // test center & both extremes before committing to full scan
        // reversed CanOccupyMoveLane loop, each probe costs 1 query when a wall
        // is present (fails at the farthest lookahead tile immediately). A solid wall
        // blocking all lateral positions is detected in 3 queries total
        if (!CanOccupyMoveLane(uid, mover, grid, gridComp, moveDir, rotation, target, 0f, lookahead) &&
            !CanOccupyMoveLane(uid, mover, grid, gridComp, moveDir, rotation, target, limit, lookahead) &&
            !CanOccupyMoveLane(uid, mover, grid, gridComp, moveDir, rotation, target, -limit, lookahead))
        {
            return false;
        }

        var foundLane = false;
        var bestOffset = baseOffset;
        var bestScore = float.MaxValue;
        var inLane = false;
        var laneStart = 0f;
        var laneEnd = 0f;

        for (var i = -sampleSteps; i <= sampleSteps; i++)
        {
            var offset = Math.Clamp(i * step, -limit, limit);
            var valid = CanOccupyMoveLane(uid, mover, grid, gridComp, moveDir, rotation, target, offset, lookahead);
            if (valid)
            {
                if (!inLane)
                {
                    inLane = true;
                    laneStart = offset;
                }

                laneEnd = offset;
                continue;
            }

            if (!inLane)
                continue;

            foundLane = true;
            SelectMoveLane(laneStart, laneEnd, baseOffset, step, ref bestOffset, ref bestScore);
            inLane = false;
        }

        if (inLane)
        {
            foundLane = true;
            SelectMoveLane(laneStart, laneEnd, baseOffset, step, ref bestOffset, ref bestScore);
        }

        if (!foundLane)
            return false;

        laneOffset = bestOffset;
        return true;
    }

    private bool CanOccupyMoveLane(
        EntityUid uid,
        GridVehicleMoverComponent mover,
        EntityUid grid,
        MapGridComponent gridComp,
        Vector2i moveDir,
        Angle? rotation,
        Vector2 target,
        float offset,
        int lookahead)
    {
        var forward = new Vector2(moveDir.X, moveDir.Y);
        var tileSize = MathF.Max(1f, gridComp.TileSize);

        // Check farthest tile first — blocked paths fail after 1 query instead of probing from current position inward.
        for (var i = lookahead - 1; i >= 0; i--)
        {
            var sample = target + forward * (tileSize * i);
            var tile = GetTile(grid, gridComp, sample);
            var center = GetTileCenter(tile);
            var lateral = GetLateralCoordinate(center, moveDir) + offset;
            sample = SetLateralCoordinate(sample, moveDir, lateral);

            if (!CanOccupyTransform(uid, mover, grid, sample, rotation, Clearance, applyEffects: false, debug: false))
                return false;
        }

        return true;
    }

    private bool TryMoveContinuous(
        EntityUid uid,
        GridVehicleMoverComponent mover,
        EntityUid grid,
        Vector2 target,
        Angle? rotation,
        out bool blocked,
        bool applyBlockEffects = true,
        bool debugProbes = true,
        bool allowVehiclePush = true)
    {
        blocked = false;
        allowVehiclePush &= applyBlockEffects;
        var start = mover.Position;
        var delta = target - start;
        var distance = delta.Length();
        if (distance <= MinMoveDistance)
            return false;

        var probeStep = Math.Clamp(mover.MovementProbeStep, 0.02f, 0.5f);
        var steps = Math.Max(1, (int) MathF.Ceiling(distance / probeStep));
        var lastGood = start;

        for (var i = 1; i <= steps; i++)
        {
            var candidate = start + delta * (i / (float) steps);
            if (!CanOccupyTransform(uid, mover, grid, candidate, rotation, Clearance, applyEffects: false, debug: debugProbes, allowVehiclePush: allowVehiclePush))
            {
                if (applyBlockEffects)
                    CanOccupyTransform(uid, mover, grid, candidate, rotation, Clearance, applyEffects: true, debug: false, allowVehiclePush: allowVehiclePush);

                mover.Position = lastGood;
                blocked = true;
                return lastGood != start;
            }

            if (applyBlockEffects &&
                !CanOccupyTransform(uid, mover, grid, candidate, rotation, Clearance, applyEffects: true, debug: false, allowVehiclePush: allowVehiclePush))
            {
                mover.Position = lastGood;
                blocked = true;
                return lastGood != start;
            }
            lastGood = candidate;
            mover.Position = candidate;
        }

        mover.Position = lastGood;
        return true;
    }

    private bool TryMoveKnownClear(
        EntityUid uid,
        GridVehicleMoverComponent mover,
        EntityUid grid,
        Vector2 target,
        Angle? rotation,
        out bool blocked,
        bool applyBlockEffects = true)
    {
        // A previous clearance probe may have offered a hypothetical vehicle push.
        return TryMoveContinuous(uid, mover, grid, target, rotation, out blocked, applyBlockEffects);
    }

    private bool TryFindTurnPosition(
        EntityUid uid,
        GridVehicleMoverComponent mover,
        EntityUid grid,
        MapGridComponent gridComp,
        Angle desiredRot,
        out Vector2 turnPosition)
    {
        turnPosition = mover.Position;
        if (CanOccupyTransform(uid, mover, grid, turnPosition, desiredRot, Clearance, applyEffects: false))
            return true;

        var limit = Math.Clamp(mover.TurnNudgeLimit, 0f, 0.49f);
        if (limit <= 0f)
            return false;

        var step = Math.Clamp(mover.TurnNudgeStep, 0.01f, limit);
        var currentTile = GetTile(grid, gridComp, mover.Position);
        var center = GetTileCenter(currentTile);
        var min = center - new Vector2(limit, limit);
        var max = center + new Vector2(limit, limit);

        var steps = (int) MathF.Ceiling(limit / step);
        for (var ring = 1; ring <= steps; ring++)
        {
            var axialDistance = Math.Clamp(ring * step, -limit, limit);
            if (TryTurnNudgePosition(uid, mover, grid, gridComp, currentTile, desiredRot, new Vector2(axialDistance, 0f), min, max, out turnPosition))
                return true;

            if (TryTurnNudgePosition(uid, mover, grid, gridComp, currentTile, desiredRot, new Vector2(-axialDistance, 0f), min, max, out turnPosition))
                return true;

            if (TryTurnNudgePosition(uid, mover, grid, gridComp, currentTile, desiredRot, new Vector2(0f, axialDistance), min, max, out turnPosition))
                return true;

            if (TryTurnNudgePosition(uid, mover, grid, gridComp, currentTile, desiredRot, new Vector2(0f, -axialDistance), min, max, out turnPosition))
                return true;

            // traverse the perimeter first
            var outerDist = Math.Clamp(ring * step, 0f, limit);
            for (var yi = -ring; yi <= ring; yi++)
            {
                if (yi == 0)
                    continue;
                var innerY = Math.Clamp(MathF.Abs(yi) * step, 0f, limit) * MathF.Sign(yi);
                if (TryTurnNudgePosition(uid, mover, grid, gridComp, currentTile, desiredRot, new Vector2(outerDist, innerY), min, max, out turnPosition))
                    return true;
                if (TryTurnNudgePosition(uid, mover, grid, gridComp, currentTile, desiredRot, new Vector2(-outerDist, innerY), min, max, out turnPosition))
                    return true;
            }
            for (var xi = -(ring - 1); xi <= ring - 1; xi++)
            {
                if (xi == 0)
                    continue;
                var innerX = Math.Clamp(MathF.Abs(xi) * step, 0f, limit) * MathF.Sign(xi);
                if (TryTurnNudgePosition(uid, mover, grid, gridComp, currentTile, desiredRot, new Vector2(innerX, outerDist), min, max, out turnPosition))
                    return true;
                if (TryTurnNudgePosition(uid, mover, grid, gridComp, currentTile, desiredRot, new Vector2(innerX, -outerDist), min, max, out turnPosition))
                    return true;
            }
        }

        return false;
    }

    private bool TryTurnNudgePosition(
        EntityUid uid,
        GridVehicleMoverComponent mover,
        EntityUid grid,
        MapGridComponent gridComp,
        Vector2i currentTile,
        Angle desiredRot,
        Vector2 offset,
        Vector2 min,
        Vector2 max,
        out Vector2 turnPosition)
    {
        turnPosition = new Vector2(
            Math.Clamp(mover.Position.X + offset.X, min.X, max.X),
            Math.Clamp(mover.Position.Y + offset.Y, min.Y, max.Y));

        if (turnPosition == mover.Position)
            return false;

        if (GetTile(grid, gridComp, turnPosition) != currentTile)
            return false;

        return CanOccupyTransform(uid, mover, grid, turnPosition, desiredRot, Clearance, applyEffects: false);
    }

    private void SelectMoveLane(
        float laneStart,
        float laneEnd,
        float baseOffset,
        float step,
        ref float bestOffset,
        ref float bestScore)
    {
        var laneWidth = laneEnd - laneStart;
        var center = (laneStart + laneEnd) * 0.5f;
        var margin = MathF.Min(step * 2f, laneWidth * 0.5f);
        var safeStart = laneStart + margin;
        var safeEnd = laneEnd - margin;
        var offset = safeStart <= safeEnd
            ? Math.Clamp(baseOffset, safeStart, safeEnd)
            : center;
        var distance = MathF.Abs(offset - baseOffset);
        var score = distance - laneWidth * 0.25f;

        if (score >= bestScore)
            return;

        bestScore = score;
        bestOffset = offset;
    }

    private float GetCoastDeceleration(EntityUid uid, GridVehicleMoverComponent mover)
    {
        return TryComp(uid, out WFVehicleEngineComponent? engine) && engine.ParkingBrake
            ? mover.Deceleration
            : mover.Deceleration * 0.12f;
    }

    private GridVehicleMotionSimulator.DriveProfile GetDriveProfile(EntityUid uid, GridVehicleMoverComponent mover)
    {
        var accelModifier = GetAccelerationModifier(uid);
        return new GridVehicleMotionSimulator.DriveProfile(
            GetModifiedMaxSpeed(uid, mover),
            GetModifiedMaxReverseSpeed(uid, mover),
            mover.Acceleration * accelModifier,
            mover.ReverseAcceleration * accelModifier,
            mover.Deceleration);
    }

    private float GetModifiedMaxSpeed(EntityUid uid, GridVehicleMoverComponent mover)
    {
        return GetModifiedSpeed(uid, mover, mover.MaxSpeed);
    }

    private float GetModifiedMaxReverseSpeed(EntityUid uid, GridVehicleMoverComponent mover)
    {
        return GetModifiedSpeed(uid, mover, mover.MaxReverseSpeed);
    }

    private float GetModifiedSpeed(EntityUid uid, GridVehicleMoverComponent mover, float baseSpeed)
    {
        var speed = baseSpeed * GetSmashSlowdownMultiplier(mover);

        if (TryComp<VehicleOverchargeComponent>(uid, out var overcharge) && _timing.CurTime < overcharge.ActiveUntil)
            speed *= overcharge.SpeedMultiplier;
        if (TryComp<VehicleSpeedModifierComponent>(uid, out var speedMod))
            speed *= speedMod.SpeedMultiplier;

        return speed;
    }

    private float GetAccelerationModifier(EntityUid uid)
    {
        if (TryComp<VehicleAccelerationModifierComponent>(uid, out var accelMod))
            return MathF.Max(0.05f, accelMod.AccelerationMultiplier);

        return 1f;
    }

    private void StopMover(GridVehicleMoverComponent mover)
    {
        mover.CurrentSpeed = 0f;
        mover.AngularVelocityDegrees = 0f;
        mover.IsCommittedToMove = false;
        mover.IsMoving = false;
        mover.TargetPosition = mover.Position;
        mover.TargetTile = mover.CurrentTile;
    }

    private void UpdateDerivedTileState(EntityUid grid, MapGridComponent gridComp, GridVehicleMoverComponent mover)
    {
        var tile = GetTile(grid, gridComp, mover.Position);
        mover.CurrentTile = tile;
        mover.TargetTile = tile;
        mover.TargetPosition = mover.Position;
    }

    private static Angle DirectionToVehicleRotation(Vector2i direction)
    {
        return new Vector2(direction.X, direction.Y).ToWorldAngle();
    }

    /// <summary>The facing the mover holds the vehicle to in its grid's frame; none until it has been driven.</summary>
    public static bool TryGetLocalFacing(GridVehicleMoverComponent mover, out Angle facing)
    {
        facing = default;
        if (mover.CurrentDirection == Vector2i.Zero)
            return false;

        facing = mover.FreeSteering
            ? mover.Heading
            : DirectionToVehicleRotation(mover.CurrentDirection);
        return true;
    }

    private static float GetLateralCoordinate(Vector2 position, Vector2i moveDir)
    {
        return moveDir.X != 0 ? position.Y : position.X;
    }

    private static Vector2 SetLateralCoordinate(Vector2 position, Vector2i moveDir, float lateral)
    {
        if (moveDir.X != 0)
            position.Y = lateral;
        else
            position.X = lateral;

        return position;
    }

    private static Vector2 GetTileCenter(Vector2i tile)
    {
        return new Vector2(tile.X + 0.5f, tile.Y + 0.5f);
    }

    private bool CanApplyTurn(GridVehicleMoverComponent mover)
    {
        if (mover.TurnDelay <= 0f)
            return true;

        return _timing.CurTime >= mover.NextTurnTime;
    }

    private void StartTurnDelay(GridVehicleMoverComponent mover)
    {
        if (mover.TurnDelay <= 0f)
            return;

        mover.NextTurnTime = _timing.CurTime + TimeSpan.FromSeconds(mover.TurnDelay);
    }

    private void SetGridPosition(EntityUid uid, EntityUid grid, Vector2 gridPos)
    {
        var xform = Transform(uid);
        if (!xform.ParentUid.IsValid())
            return;

        var coords = new EntityCoordinates(grid, gridPos);
        var local = _transform.WithEntityId(coords, xform.ParentUid).Position;
        var moved = !xform.LocalPosition.EqualsApprox(local);

        _transform.SetLocalPosition(uid, local, xform);

        // The facing is held too, so a blast or a bump cannot leave a parked vehicle turned.
        if (xform.ParentUid == grid && TryComp(uid, out GridVehicleMoverComponent? mover))
        {
            if (TryGetLocalFacing(mover, out var facing))
                _transform.SetLocalRotation(uid, facing, xform);
            else
                HoldRestRotation(uid, mover, xform);
        }

        // The engine's own check is off for a mover, so this is where the vehicle meets the next grid.
        if (moved)
            CheckGridTraversal(uid, xform);
    }

    /// <summary>Holds the rotation of a vehicle nobody has driven, and undoes a turn that physics gave it.</summary>
    private void HoldRestRotation(EntityUid uid, GridVehicleMoverComponent mover, TransformComponent xform)
    {
        var spun = physicsQ.TryComp(uid, out var body) && body.AngularVelocity != 0f;
        if (spun && mover.RestRotation is { } rest)
            _transform.SetLocalRotation(uid, rest, xform);
        else
            mover.RestRotation = xform.LocalRotation;
    }

    private Vector2i GetTile(EntityUid grid, MapGridComponent gridComp, Vector2 pos)
    {
        var coords = new EntityCoordinates(grid, pos);
        return _map.TileIndicesFor(grid, gridComp, coords);
    }

    private void PlayRunningSound(EntityUid uid)
    {
        if (!TryComp<VehicleSoundComponent>(uid, out var sound))
            return;

        if (sound.RunningSound == null)
            return;

        if (_net.IsClient)
            return;

        var now = _timing.CurTime;
        if (sound.NextRunningSound > now)
            return;

        _audio.PlayPvs(sound.RunningSound, uid);
        sound.NextRunningSound = now + TimeSpan.FromSeconds(sound.RunningSoundCooldown);
        Dirty(uid, sound);
    }

    private float GetSmashSlowdownMultiplier(GridVehicleMoverComponent mover)
    {
        if (mover.SmashSlowdownMultiplier >= 1f && mover.SmashSlowdownUntil == TimeSpan.Zero)
            return 1f;

        var now = _timing.CurTime;
        if (mover.SmashSlowdownUntil != TimeSpan.Zero && now >= mover.SmashSlowdownUntil)
        {
            mover.SmashSlowdownMultiplier = 1f;
            mover.SmashSlowdownUntil = TimeSpan.Zero;
            return 1f;
        }

        return Math.Clamp(mover.SmashSlowdownMultiplier, 0f, 1f);
    }

    private void ApplySmashSlowdown(EntityUid vehicle, GridVehicleMoverComponent mover, VehicleSmashSettings smashable)
    {
        if (smashable.SlowdownDuration <= 0f || smashable.SlowdownMultiplier >= 1f)
            return;

        if (smashable.SlowdownBelowWeightClass is { } maxWeight && mover.WeightClass >= maxWeight)
            return;

        // The vehicle loses its speed once, when the slowdown starts. It keeps hitting what it is passing for as many steps
        // as it overlaps it, and the client, which cannot delete a target, hits it again until the server's deletion arrives.
        var now = _timing.CurTime;
        var starting = mover.SmashSlowdownUntil <= now;
        mover.SmashSlowdownMultiplier = starting
            ? smashable.SlowdownMultiplier
            : MathF.Min(mover.SmashSlowdownMultiplier, smashable.SlowdownMultiplier);
        var until = now + TimeSpan.FromSeconds(smashable.SlowdownDuration);
        if (until > mover.SmashSlowdownUntil)
            mover.SmashSlowdownUntil = until;

        if (starting)
            mover.CurrentSpeed *= smashable.SlowdownMultiplier;
    }
}
