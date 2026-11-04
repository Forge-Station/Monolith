using System;
using System.Numerics;
using Robust.Shared.Maths;

namespace Content.Shared._WF.Vehicles.Grid;

/// <summary>The mover's speed arithmetic, pure so it can be tested without a server.</summary>
public static class GridVehicleMotionSimulator
{
    private const float MinimumMovingSpeed = 0.01f;

    /// <summary>Normal collision impulse for two masses; separating bodies exchange no momentum.</summary>
    public static float CollisionImpulse(float closingSpeed, float mass, float otherMass)
    {
        return 1.15f * MathF.Max(0f, closingSpeed) /
               (1f / MathF.Max(1f, mass) + 1f / MathF.Max(1f, otherMass));
    }

    /// <summary>The speed limits and rates a drive step works with.</summary>
    public readonly record struct DriveProfile(
        float MaxSpeed,
        float MaxReverseSpeed,
        float Acceleration,
        float ReverseAcceleration,
        float Deceleration);

    /// <summary>The speed after a drive step, and whether the input asked to reverse or to change direction.</summary>
    public readonly record struct DriveSpeedResult(
        float CurrentSpeed,
        bool ReversingInput,
        bool ChangingDirection);

    /// <summary>Where a vehicle stands after advancing along a path, and how much of the distance was left.</summary>
    public readonly record struct AdvanceResult(
        Vector2 Position,
        Vector2i CurrentTile,
        float RemainingDistance,
        bool ReachedTarget);

    /// <summary>Speed after a step with no input: the vehicle coasts down.</summary>
    public static float StepIdleSpeed(float currentSpeed, float deceleration, float frameTime)
    {
        if (currentSpeed > 0f)
            return MathF.Max(0f, currentSpeed - deceleration * frameTime);

        if (currentSpeed < 0f)
            return MathF.Min(0f, currentSpeed + deceleration * frameTime);

        return 0f;
    }

    /// <summary>Speed after a step of being pushed.</summary>
    public static float StepPushSpeed(
        float currentSpeed,
        float maxSpeed,
        float acceleration,
        float deceleration,
        bool hasInput,
        bool isCommittedToMove,
        float frameTime)
    {
        var hasInputForSpeed = hasInput || isCommittedToMove;
        float targetSpeed;
        float accel;

        if (!hasInputForSpeed)
        {
            targetSpeed = 0f;
            accel = deceleration;
        }
        else
        {
            targetSpeed = maxSpeed;
            accel = acceleration;
        }

        return StepTowardsTargetSpeed(currentSpeed, targetSpeed, accel, deceleration, frameTime);
    }

    /// <summary>Speed after a driven step: accelerates toward the target, or brakes.</summary>
    /// <summary>
    /// Whether an obstacle lies against the hull's front face rather than a side or the back. Everything is in one frame:
    /// the hull's pose, its bounds in its own axes and the obstacle's box. A corner contact goes to whichever face it is
    /// nearer, measured in hull halves.
    /// </summary>
    public static bool IsFrontImpact(Vector2 position, Angle rotation, Box2 hullBounds, Box2 obstacle)
    {
        var centre = position + rotation.RotateVec(hullBounds.Center);
        var offset = obstacle.Center - centre;
        var forward = rotation.ToWorldVec();
        var right = new Vector2(-forward.Y, forward.X);
        var ahead = Vector2.Dot(offset, forward);
        var aside = MathF.Abs(Vector2.Dot(offset, right));
        var halfWidth = MathF.Max(hullBounds.Width * 0.5f, 0.001f);
        var halfLength = MathF.Max(hullBounds.Height * 0.5f, 0.001f);

        return ahead > 0f && ahead / halfLength > aside / halfWidth;
    }

    /// <summary>The steering as the chassis answers it: reversed while the vehicle rolls, or is about to roll, backwards.</summary>
    public static float GetEffectiveSteering(float steering, float currentSpeed, float throttle)
    {
        var reversing = currentSpeed < -MinimumMovingSpeed ||
                        (MathF.Abs(currentSpeed) <= MinimumMovingSpeed && throttle < 0f);
        return reversing ? -steering : steering;
    }

    /// <summary>Whether the chassis can turn: always for one that turns on the spot, otherwise only while it rolls.</summary>
    public static bool CanSteer(bool turnInPlace, float currentSpeed)
    {
        return turnInPlace || MathF.Abs(currentSpeed) > MinimumMovingSpeed;
    }

    public static DriveSpeedResult StepDriveSpeed(
        float currentSpeed,
        DriveProfile profile,
        Vector2i facing,
        Vector2i inputDir,
        bool hasInput,
        bool isCommittedToMove,
        float frameTime)
    {
        var hasInputForSpeed = hasInput || isCommittedToMove;
        var reversing = hasInput && facing != Vector2i.Zero && inputDir == -facing;

        float targetSpeed;
        float accel;

        if (!hasInputForSpeed)
        {
            targetSpeed = 0f;
            accel = profile.Deceleration;
        }
        else if (reversing)
        {
            if (currentSpeed > 0f)
            {
                targetSpeed = 0f;
                accel = profile.Deceleration;
            }
            else
            {
                targetSpeed = -profile.MaxReverseSpeed;
                accel = profile.ReverseAcceleration;
            }
        }
        else
        {
            if (currentSpeed < 0f && hasInputForSpeed)
            {
                targetSpeed = 0f;
                accel = profile.Deceleration;
            }
            else
            {
                targetSpeed = profile.MaxSpeed;
                accel = profile.Acceleration;
            }
        }

        var steppedSpeed = StepTowardsTargetSpeed(currentSpeed, targetSpeed, accel, accel, frameTime);
        var changingDirection =
            MathF.Abs(steppedSpeed) > MinimumMovingSpeed &&
            ((reversing && steppedSpeed > 0f) ||
             (!reversing && steppedSpeed < 0f));

        return new DriveSpeedResult(steppedSpeed, reversing, changingDirection);
    }

    /// <summary>Moves a position toward a target by the distance a speed covers in a step.</summary>
    public static AdvanceResult AdvanceToTarget(
        Vector2 position,
        Vector2i currentTile,
        Vector2i targetTile,
        Vector2 targetPosition,
        float travelDistance)
    {
        var toTarget = targetPosition - position;
        var distToTarget = toTarget.Length();

        if (distToTarget <= 0.0001f || travelDistance >= distToTarget)
        {
            return new AdvanceResult(
                targetPosition,
                targetTile,
                MathF.Max(0f, travelDistance - distToTarget),
                true);
        }

        var dir = toTarget / distToTarget;
        return new AdvanceResult(
            position + dir * travelDistance,
            currentTile,
            0f,
            false);
    }

    private static float StepTowardsTargetSpeed(
        float currentSpeed,
        float targetSpeed,
        float accelerateTowardTarget,
        float decelerateTowardTarget,
        float frameTime)
    {
        if (currentSpeed < targetSpeed)
            return MathF.Min(currentSpeed + accelerateTowardTarget * frameTime, targetSpeed);

        if (currentSpeed > targetSpeed)
            return MathF.Max(currentSpeed - decelerateTowardTarget * frameTime, targetSpeed);

        return currentSpeed;
    }
}
