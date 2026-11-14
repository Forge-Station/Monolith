using System;
using System.Numerics;

namespace Content.Shared._WF.TractorBeam;

/// <summary>
/// Force and power calculations for a ship-to-ship tractor restraint.
/// Callers must apply equal and opposite impulses to both participating bodies.
/// </summary>
public static class TractorBeamPhysics
{
    /// <summary>
    /// Computes the force on the source. Separation and relative velocity are target minus source.
    /// Frequency is in hertz, frame time in seconds, and reduced mass is m1*m2/(m1+m2).
    /// The beam resists movement in either axial direction and sideways about the lock bearing.
    /// </summary>
    public static Vector2 CalculateForce(
        Vector2 separation,
        Vector2 relativeVelocity,
        float holdDistance,
        float reducedMass,
        float frequency,
        float dampingRatio,
        float maxForce,
        float frameTime,
        Vector2 holdDirection = default,
        float? lateralReducedMass = null)
    {
        if (!IsFinite(separation) || !IsFinite(relativeVelocity) || !IsFinite(holdDirection) ||
            !float.IsFinite(holdDistance) || holdDistance < 0f ||
            !float.IsFinite(reducedMass) || reducedMass <= 0f ||
            (lateralReducedMass is { } lateralMass && (!float.IsFinite(lateralMass) || lateralMass <= 0f)) ||
            !float.IsFinite(frequency) || frequency <= 0f ||
            !float.IsFinite(dampingRatio) || dampingRatio < 0f ||
            !float.IsFinite(maxForce) || maxForce <= 0f ||
            !float.IsFinite(frameTime) || frameTime <= 0f)
            return Vector2.Zero;

        // Use doubles for intermediates so large but finite inputs cannot overflow a float.
        var distance = Math.Sqrt((double) separation.X * separation.X + (double) separation.Y * separation.Y);
        if (distance <= 0.0001)
            return Vector2.Zero;

        var bearingLength = Math.Sqrt((double) holdDirection.X * holdDirection.X + (double) holdDirection.Y * holdDirection.Y);
        var directionX = bearingLength > 0.0001 ? holdDirection.X / bearingLength : separation.X / distance;
        var directionY = bearingLength > 0.0001 ? holdDirection.Y / bearingLength : separation.Y / distance;
        var axialDistance = separation.X * directionX + separation.Y * directionY;
        var lateralDistance = -separation.X * directionY + separation.Y * directionX;
        // Float map coordinates and a normalized bearing do not project back onto exactly the
        // captured float distance. Ignore sub-0.1mm position noise, but retain all velocity and
        // pending-thrust resistance so a stationary lock has no artificial electrical load.
        var extension = axialDistance - holdDistance;
        if (Math.Abs(extension) <= 0.0001)
            extension = 0;
        if (Math.Abs(lateralDistance) <= 0.0001)
            lateralDistance = 0;
        var radialVelocity = relativeVelocity.X * directionX + relativeVelocity.Y * directionY;
        var lateralVelocity = -relativeVelocity.X * directionY + relativeVelocity.Y * directionX;
        var angularFrequency = 2.0 * Math.PI * frequency;
        var stiffness = reducedMass * angularFrequency * angularFrequency;
        var damping = 2.0 * reducedMass * dampingRatio * angularFrequency;

        // Implicit spring integration accounts for this impulse's effect on the next position and
        // velocity. Negative axial force pushes a closing target back toward the held distance.
        var denominator = 1.0 + (damping * frameTime + stiffness * frameTime * frameTime) / reducedMass;
        var force = (stiffness * extension +
            (damping + stiffness * frameTime) * radialVelocity) / denominator;
        // Sideways impulses also rotate the source through the arm's lever. Account for that
        // extra freedom in the implicit solve instead of over-correcting and oscillating.
        var lateralDenominator = 1.0 + (damping * frameTime + stiffness * frameTime * frameTime) /
            (lateralReducedMass ?? reducedMass);
        var lateralForce = (stiffness * lateralDistance +
            (damping + stiffness * frameTime) * lateralVelocity) / lateralDenominator;
        if (!double.IsFinite(force) || !double.IsFinite(lateralForce))
            return Vector2.Zero;

        // Axial and sideways restraint share the dish's single force/power budget.
        var magnitude = Math.Sqrt(force * force + lateralForce * lateralForce);
        var scale = magnitude > maxForce ? maxForce / magnitude : 1.0;
        return new Vector2((float) ((directionX * force - directionY * lateralForce) * scale),
            (float) ((directionY * force + directionX * lateralForce) * scale));
    }

    /// <summary>
    /// Damped angular restraint about the captured relative orientation. Returns torque on the
    /// source; apply its opposite to the target. The caller shares its cost with translation.
    /// </summary>
    public static float CalculateTorque(float angleError, float relativeAngularVelocity, float reducedInertia,
        float frequency, float dampingRatio, float maxTorque, float frameTime)
    {
        if (!float.IsFinite(angleError) || !float.IsFinite(relativeAngularVelocity) ||
            !float.IsFinite(reducedInertia) || reducedInertia <= 0f ||
            !float.IsFinite(frequency) || frequency <= 0f ||
            !float.IsFinite(dampingRatio) || dampingRatio < 0f ||
            !float.IsFinite(maxTorque) || maxTorque <= 0f ||
            !float.IsFinite(frameTime) || frameTime <= 0f)
            return 0f;

        // Use the shortest turn across the +/- pi seam and ignore capture rounding noise.
        var error = Math.Atan2(Math.Sin(angleError), Math.Cos(angleError));
        if (Math.Abs(error) <= 0.000001)
            error = 0;
        var angularFrequency = 2.0 * Math.PI * frequency;
        var stiffness = reducedInertia * angularFrequency * angularFrequency;
        var damping = 2.0 * reducedInertia * dampingRatio * angularFrequency;
        var torque = (stiffness * error + (damping + stiffness * frameTime) * relativeAngularVelocity) /
            (1.0 + (damping * frameTime + stiffness * frameTime * frameTime) / reducedInertia);
        return double.IsFinite(torque) ? (float) Math.Clamp(torque, -maxTorque, maxTorque) : 0f;
    }

    /// <summary>Maintaining a longer beam consumes a quadratic share of the dish's power budget.</summary>
    public static float CalculateDistanceStrain(float distance, float maxRange, float strainAtMaxRange = 0.6f)
    {
        if (!float.IsFinite(distance) || !float.IsFinite(maxRange) || maxRange <= 0f ||
            !float.IsFinite(strainAtMaxRange))
            return 0f;
        var fraction = Math.Clamp((double) distance / maxRange, 0.0, 1.0);
        return (float) (fraction * fraction * Math.Clamp((double) strainAtMaxRange, 0.0, 1.0));
    }

    /// <summary>
    /// Distance pays its baseline strain first. Mechanical resistance then consumes the remaining
    /// budget on a square-root curve, keeping both contributions within the same maximum power.
    /// </summary>
    public static float CalculateStrain(float force, float maxForce, float distanceStrain = 0f)
    {
        if (!float.IsFinite(force) || force < 0f || !float.IsFinite(maxForce) || maxForce <= 0f ||
            !float.IsFinite(distanceStrain))
            return 0f;
        var baseline = Math.Clamp((double) distanceStrain, 0.0, 1.0);
        var mechanical = Math.Sqrt(Math.Clamp((double) force / maxForce, 0.0, 1.0));
        return (float) (baseline + (1.0 - baseline) * mechanical);
    }

    /// <summary>Power required for a given mechanical load and beam length, including fixed overhead.</summary>
    public static float CalculatePower(float force, float maxForce, float holdingPower, float maxPower, float distanceStrain = 0f)
    {
        if (!float.IsFinite(force) || force < 0f ||
            !float.IsFinite(maxForce) || maxForce <= 0f ||
            !float.IsFinite(holdingPower) || holdingPower < 0f ||
            !float.IsFinite(maxPower) || maxPower < holdingPower || !float.IsFinite(distanceStrain))
            return 0f;

        var strain = CalculateStrain(force, maxForce, distanceStrain);
        return (float) (holdingPower + ((double) maxPower - holdingPower) * strain);
    }

    /// <summary>
    /// Fraction of the requested force affordable after paying the lock's fixed power cost.
    /// No force is available when the supply cannot maintain the lock.
    /// </summary>
    public static float AvailableForceFraction(float receivedPower, float holdingPower, float requestedPower)
    {
        if (!float.IsFinite(receivedPower) || receivedPower <= 0f ||
            !float.IsFinite(holdingPower) || holdingPower < 0f ||
            !float.IsFinite(requestedPower) || requestedPower < holdingPower ||
            receivedPower < holdingPower)
            return 0f;

        if (requestedPower == holdingPower)
            return 1f;

        var available = Math.Clamp(((double) receivedPower - holdingPower) / (requestedPower - holdingPower), 0.0, 1.0);
        // Invert the square-root power curve: half of the variable electricity buys a quarter
        // of the requested force, rather than silently granting unaffordable restraint.
        return (float) (available * available);
    }

    /// <summary>
    /// Firm capture constraint, returning the source force/torque. Pinning directly arrests
    /// closing, sideways motion and rotation. Call with the target mass or inertia.
    /// Velocity includes this substep's pending external forces, so stationary thrust still costs power.
    /// </summary>
    public static float CalculateLockForce(float error, float velocity, float reducedMass, float frameTime)
    {
        if (!float.IsFinite(error) || !float.IsFinite(velocity) || !float.IsFinite(reducedMass) || reducedMass <= 0 ||
            !float.IsFinite(frameTime) || frameTime <= 0)
            return 0;
        // Correct drift smoothly after overload; never snap a vessel back to its old pose.
        var correctionSpeed = Math.Clamp((double) error * 4.0, -5.0, 5.0);
        var force = (velocity + correctionSpeed) * reducedMass / frameTime;
        return (float) Math.Clamp(force, -float.MaxValue, float.MaxValue);
    }

    private static bool IsFinite(Vector2 vector)
    {
        return float.IsFinite(vector.X) && float.IsFinite(vector.Y);
    }

    /// <summary>Solve coupled translation/rotation of a rigid arm, including its reaction lever.</summary>
    public static (Vector2 Force, float Torque) CalculateArmLock(Vector2 acceleration, float angularAcceleration,
        Vector2 separation, float inverseMass, float sourceInverseInertia, float targetInverseInertia)
    {
        var arm = new Vector2(-separation.Y, separation.X);
        var inertia = (double) sourceInverseInertia + targetInverseInertia;
        if (inverseMass <= 0 || inertia <= 0)
            return (inverseMass > 0 ? acceleration / inverseMass : Vector2.Zero, 0);
        // Eliminate the angular row of the symmetric constraint matrix, then invert the
        // remaining rank-one linear term. This prevents the two servos fighting each other.
        var coupling = sourceInverseInertia * (double) targetInverseInertia / inertia;
        var rhsX = acceleration.X - arm.X * sourceInverseInertia * angularAcceleration / inertia;
        var rhsY = acceleration.Y - arm.Y * sourceInverseInertia * angularAcceleration / inertia;
        var projection = coupling * (arm.X * rhsX + arm.Y * rhsY) /
            (inverseMass + coupling * ((double) arm.X * arm.X + (double) arm.Y * arm.Y));
        var x = (rhsX - arm.X * projection) / inverseMass;
        var y = (rhsY - arm.Y * projection) / inverseMass;
        var torque = (angularAcceleration - sourceInverseInertia * (arm.X * x + arm.Y * y)) / inertia;
        return (new Vector2((float) x, (float) y), (float) torque);
    }
}
