using System;
using System.Numerics;
using Content.Shared._WF.TractorBeam;
using NUnit.Framework;

namespace Content.Tests.Shared._WF;

[TestFixture]
[TestOf(typeof(TractorBeamPhysics))]
public sealed class TractorBeamPhysicsTest
{
    [Test]
    public void ReactionConservesMomentumAndAcceleratesLighterShipMore()
    {
        const float sourceMass = 100f;
        const float targetMass = 1000f;
        const float dt = 1f / 60f;
        var force = TractorBeamPhysics.CalculateForce(new Vector2(20f, 0f), new Vector2(5f, 0f),
            10f, sourceMass * targetMass / (sourceMass + targetMass), 1f, 1f, 10000f, dt);

        var sourceVelocityChange = force * dt / sourceMass;
        var targetVelocityChange = -force * dt / targetMass;
        Assert.That(sourceVelocityChange.X, Is.GreaterThan(0f));
        Assert.That(sourceVelocityChange.X, Is.EqualTo(-10f * targetVelocityChange.X).Within(0.0001f));
        Assert.That((sourceMass * sourceVelocityChange + targetMass * targetVelocityChange).Length(),
            Is.LessThan(0.0001f));
    }

    [Test]
    public void PullingAwayIncreasesStrainAndPower()
    {
        var stationary = Force(new Vector2(10f, 0f), Vector2.Zero).Length();
        var resisting = Force(new Vector2(10f, 0f), new Vector2(5f, 0f)).Length();
        var resistingHarder = Force(new Vector2(10f, 0f), new Vector2(10f, 0f)).Length();

        Assert.That(stationary, Is.Zero);
        Assert.That(resisting, Is.GreaterThan(stationary));
        Assert.That(resistingHarder, Is.GreaterThan(resisting));
        Assert.That(TractorBeamPhysics.CalculatePower(resistingHarder, 10000f, 1000f, 11000f),
            Is.GreaterThan(TractorBeamPhysics.CalculatePower(resisting, 10000f, 1000f, 11000f)));
    }

    [Test]
    public void FloatProjectionNoiseDoesNotCreateIdleStrainButThrustStillDoes()
    {
        var separation = new Vector2(52.3f, 1.7f);
        var holdDistance = separation.Length();
        var bearing = separation / holdDistance;
        var stationary = TractorBeamPhysics.CalculateForce(separation, Vector2.Zero,
            holdDistance, 100f, 0.7f, 1f, 10000f, 1f / 240f, bearing);
        Assert.That(stationary, Is.EqualTo(Vector2.Zero));
        Assert.That(TractorBeamPhysics.CalculatePower(stationary.Length(), 10000f, 100000f, 2000000f),
            Is.EqualTo(100000f));
        var resisting = TractorBeamPhysics.CalculateForce(separation, bearing * 0.01f,
            holdDistance, 100f, 0.7f, 1f, 10000f, 1f / 240f, bearing);
        Assert.That(resisting.Length(), Is.GreaterThan(0f), "The position tolerance must not suppress thrust feedback.");
    }

    [Test]
    public void ClosingMotionAndCompressionAreRestrainedAndCostPower()
    {
        var compressed = Force(new Vector2(5f, 0f), Vector2.Zero);
        var closing = Force(new Vector2(10f, 0f), new Vector2(-5f, 0f));
        var escaping = Force(new Vector2(10f, 0f), new Vector2(5f, 0f));
        Assert.That(compressed.X, Is.LessThan(0f));
        Assert.That(closing.X, Is.LessThan(0f), "The opposite impulse brakes the approaching target.");
        Assert.That(closing.X, Is.EqualTo(-escaping.X).Within(0.001f));
        Assert.That(TractorBeamPhysics.CalculatePower(closing.Length(), 10000f, 1000f, 11000f),
            Is.GreaterThan(1000f));
        Assert.That(Force(new Vector2(20f, 0f), new Vector2(-1000f, 0f)).Length(),
            Is.LessThanOrEqualTo(10000f));
    }

    [TestCase(-1f)]
    [TestCase(1f)]
    public void RotationIsRestrainedInBothDirections(float sign)
    {
        var moving = TractorBeamPhysics.CalculateTorque(0f, sign, 100f, 0.7f, 1f, 5000f, 1f / 60f);
        var displaced = TractorBeamPhysics.CalculateTorque(sign * 0.2f, 0f, 100f, 0.7f, 1f, 5000f, 1f / 60f);
        Assert.That(moving * sign, Is.GreaterThan(0));
        Assert.That(displaced * sign, Is.GreaterThan(0));
        Assert.That(MathF.Abs(TractorBeamPhysics.CalculateTorque(sign * 2f, sign * 100f,
            100f, 0.7f, 1f, 5000f, 1f / 60f)), Is.EqualTo(5000f));
    }

    [Test]
    public void RotationUsesShortestTurnAcrossAngleSeam()
    {
        var seam = TractorBeamPhysics.CalculateTorque(-2f * MathF.PI + 0.1f, 0f,
            100f, 0.7f, 1f, 5000f, 1f / 60f);
        var nearby = TractorBeamPhysics.CalculateTorque(0.1f, 0f, 100f, 0.7f, 1f, 5000f, 1f / 60f);
        Assert.That(seam, Is.EqualTo(nearby).Within(0.01f));
        Assert.That(TractorBeamPhysics.CalculateTorque(0.0000001f, 0f, 100f, 0.7f, 1f, 5000f, 1f / 60f),
            Is.Zero, "Capture roundoff must not add idle strain.");
    }

    [Test]
    public void AngularRestraintSettlesInsteadOfAllowingContinuousSpin()
    {
        var angle = 0f;
        var angularVelocity = 1f;
        const float dt = 1f / 240f;
        for (var step = 0; step < 1200; step++)
        {
            angularVelocity -= TractorBeamPhysics.CalculateTorque(angle, angularVelocity,
                100f, 0.7f, 1f, 5000f, dt) / 100f * dt;
            angle += angularVelocity * dt;
        }
        Assert.That(MathF.Abs(angle), Is.LessThan(0.001f));
        Assert.That(MathF.Abs(angularVelocity), Is.LessThan(0.001f));
    }

    [Test]
    public void SidewaysMotionIsRestrainedAndConsumesPower()
    {
        var force = Force(new Vector2(10f, 0f), new Vector2(0f, 5f));
        Assert.That(force.X, Is.Zero);
        Assert.That(force.Y, Is.GreaterThan(0), "The negative reaction slows the target's sideways motion.");
        Assert.That(TractorBeamPhysics.CalculatePower(force.Length(), 10000f, 1000f, 11000f),
            Is.GreaterThan(1000f));
    }

    [Test]
    public void LateralDisplacementRestoresOriginalBearingEvenAfterMotionStops()
    {
        var force = TractorBeamPhysics.CalculateForce(new Vector2(10f, 3f), Vector2.Zero,
            10f, 100f, 1f, 1f, 10000f, 1f / 60f, Vector2.UnitX);
        Assert.That(force.X, Is.Zero);
        Assert.That(force.Y, Is.GreaterThan(0));
        var opposite = TractorBeamPhysics.CalculateForce(new Vector2(10f, -3f), Vector2.Zero,
            10f, 100f, 1f, 1f, 10000f, 1f / 60f, Vector2.UnitX);
        Assert.That(opposite.Y, Is.EqualTo(-force.Y).Within(0.001f));
    }

    [Test]
    public void DiagonalResistanceSharesOneForceLimit()
    {
        var force = TractorBeamPhysics.CalculateForce(new Vector2(100f, 100f), new Vector2(100f, 100f),
            10f, 100f, 1f, 1f, 10000f, 1f / 60f, Vector2.UnitX);
        Assert.That(force.X, Is.GreaterThan(0));
        Assert.That(force.Y, Is.GreaterThan(0));
        Assert.That(force.Length(), Is.EqualTo(10000f).Within(0.01f));
    }

    [Test]
    public void LateralRestraintSettlesWithoutUnboundedWeaving()
    {
        var separation = new Vector2(10f, 0f);
        var relativeVelocity = new Vector2(0f, 10f);
        const float reducedMass = 100f;
        const float dt = 1f / 240f;
        for (var step = 0; step < 1200; step++)
        {
            var force = TractorBeamPhysics.CalculateForce(separation, relativeVelocity,
                10f, reducedMass, 0.7f, 1f, 10000f, dt, Vector2.UnitX);
            relativeVelocity -= force / reducedMass * dt;
            separation += relativeVelocity * dt;
        }
        Assert.That(MathF.Abs(separation.Y), Is.LessThan(0.01f));
        Assert.That(MathF.Abs(relativeVelocity.Y), Is.LessThan(0.01f));
    }

    [Test]
    public void ForceAndPowerCannotExceedLimits()
    {
        var force = Force(new Vector2(1000000f, 0f), new Vector2(1000000f, 0f));
        Assert.That(force.X, Is.EqualTo(10000f));
        Assert.That(TractorBeamPhysics.CalculatePower(force.Length() * 10f, 10000f, 1000f, 11000f),
            Is.EqualTo(11000f));
        Assert.That(TractorBeamPhysics.CalculatePower(0f, 10000f, 1000f, 11000f), Is.EqualTo(1000f));
    }

    [TestCase(0f, 0f)]
    [TestCase(500f, 0f)]
    [TestCase(1000f, 0f)]
    [TestCase(3500f, 0.25f)]
    [TestCase(6000f, 1f)]
    [TestCase(12000f, 1f)]
    public void BrownoutPaysLockOverheadBeforeApplyingForce(float supply, float expectedFraction)
    {
        Assert.That(TractorBeamPhysics.AvailableForceFraction(supply, 1000f, 6000f),
            Is.EqualTo(expectedFraction).Within(0.0001f));
    }

    [Test]
    public void ModestResistanceConsumesSubstantialPowerAndBrownoutInvertsCurve()
    {
        // A 20kN load is only a tenth of mechanical capacity, but draws over 700kW.
        var demand = TractorBeamPhysics.CalculatePower(20000f, 200000f, 100000f, 2000000f);
        Assert.That(demand, Is.GreaterThan(700000f));
        Assert.That(TractorBeamPhysics.CalculateStrain(20000f, 200000f), Is.EqualTo(MathF.Sqrt(0.1f)).Within(0.00001f));
        var received = 100000f + (demand - 100000f) / 2f;
        var availableForce = 20000f * TractorBeamPhysics.AvailableForceFraction(received, 100000f, demand);
        Assert.That(availableForce, Is.EqualTo(5000f).Within(0.01f));
        Assert.That(TractorBeamPhysics.CalculatePower(availableForce, 200000f, 100000f, 2000000f),
            Is.EqualTo(received).Within(0.1f), "Reduced restraint must fit the power actually received.");
    }

    [TestCase(-5f)]
    [TestCase(5f)]
    public void PinStopsBothClosingAndDepartingMotion(float velocity)
    {
        const float mass = 1000f;
        const float dt = 1f / 240f;
        var force = TractorBeamPhysics.CalculateLockForce(0f, velocity, mass, dt);
        Assert.That(velocity - force / mass * dt, Is.EqualTo(0f).Within(0.0001f));
    }

    [Test]
    public void StationaryPinnedThrustStillRequiresSustainedPower()
    {
        const float mass = 1000f;
        const float thrust = 20000f;
        const float dt = 1f / 240f;
        var velocity = 0f;
        for (var step = 0; step < 400; step++)
        {
            var predictedVelocity = velocity + thrust / mass * dt;
            var force = TractorBeamPhysics.CalculateLockForce(0f, predictedVelocity, mass, dt);
            velocity = predictedVelocity - force / mass * dt;
            Assert.That(velocity, Is.EqualTo(0f).Within(0.00001f));
            Assert.That(force, Is.EqualTo(thrust).Within(0.01f));
            Assert.That(TractorBeamPhysics.CalculatePower(force, 200000f, 100000f, 2000000f), Is.GreaterThan(700000f));
        }
    }

    [Test]
    public void InvalidInputsFailClosed()
    {
        Assert.That(Force(Vector2.Zero, Vector2.Zero), Is.EqualTo(Vector2.Zero));
        Assert.That(Force(new Vector2(float.NaN, 0f), Vector2.Zero), Is.EqualTo(Vector2.Zero));
        Assert.That(Force(new Vector2(20f, 0f), new Vector2(float.PositiveInfinity, 0f)), Is.EqualTo(Vector2.Zero));
        foreach (var invalid in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
        {
            Assert.That(TractorBeamPhysics.CalculateTorque(0.1f, 1f, invalid, 0.7f, 1f, 5000f, 1f / 60f), Is.Zero);
            Assert.That(TractorBeamPhysics.CalculateTorque(0.1f, 1f, 100f, 0.7f, 1f, 5000f, invalid), Is.Zero);
            Assert.That(TractorBeamPhysics.CalculateForce(new Vector2(20f, 0f), Vector2.Zero,
                10f, invalid, 1f, 1f, 10000f, 1f / 60f), Is.EqualTo(Vector2.Zero));
            Assert.That(TractorBeamPhysics.CalculateForce(new Vector2(20f, 0f), Vector2.Zero,
                10f, 100f, 1f, 1f, 10000f, invalid), Is.EqualTo(Vector2.Zero));
        }

        Assert.That(TractorBeamPhysics.CalculatePower(float.NaN, 100f, 10f, 1000f), Is.Zero);
        Assert.That(TractorBeamPhysics.CalculatePower(100f, 0f, 10f, 1000f), Is.Zero);
        Assert.That(TractorBeamPhysics.CalculatePower(100f, 100f, 1000f, 10f), Is.Zero);
        Assert.That(TractorBeamPhysics.AvailableForceFraction(float.NaN, 10f, 100f), Is.Zero);
        Assert.That(TractorBeamPhysics.AvailableForceFraction(100f, 100f, 10f), Is.Zero);
        Assert.That(TractorBeamPhysics.AvailableForceFraction(0f, 0f, 0f), Is.Zero);
    }

    [Test]
    public void FiniteExtremeInputsDoNotProduceNonFiniteForces()
    {
        var force = TractorBeamPhysics.CalculateForce(new Vector2(float.MaxValue, float.MaxValue),
            new Vector2(float.MaxValue, float.MaxValue), 10f, float.MaxValue, float.MaxValue,
            float.MaxValue, 10000f, float.MaxValue);
        Assert.That(float.IsFinite(force.X) && float.IsFinite(force.Y), Is.True);
        Assert.That(force.Length(), Is.LessThanOrEqualTo(10000.01f));
    }

    [Test]
    public void CooperatingShipsReduceTargetSpeedWithoutCreatingMomentum()
    {
        var singleBeamSpeed = SimulateCooperatingShips(1);
        var twoBeamSpeed = SimulateCooperatingShips(2);
        Assert.That(twoBeamSpeed, Is.LessThan(singleBeamSpeed));
        Assert.That(singleBeamSpeed, Is.LessThan(10f));
        Assert.That(twoBeamSpeed, Is.GreaterThan(0f));
    }

    private static float SimulateCooperatingShips(int sourceCount)
    {
        const float sourceMass = 100f;
        const float targetMass = 150f;
        const float dt = 1f / 120f;
        var sourcePositions = new float[sourceCount];
        var sourceVelocities = new float[sourceCount];
        var targetPosition = 10f;
        var targetVelocity = 10f;
        // Compare the initial braking phase. More participating source mass shares the reaction;
        // bringing the entire group to rest still requires the source ships' thrusters.
        for (var step = 0; step < 30; step++)
        {
            for (var i = 0; i < sourceCount; i++)
            {
                var force = TractorBeamPhysics.CalculateForce(new Vector2(targetPosition - sourcePositions[i], 0f),
                    new Vector2(targetVelocity - sourceVelocities[i], 0f), 10f,
                    sourceMass * targetMass / (sourceMass + targetMass), 0.5f, 1f, 10000f, dt).X;
                sourceVelocities[i] += force * dt / sourceMass;
                targetVelocity -= force * dt / targetMass;
            }

            targetPosition += targetVelocity * dt;
            for (var i = 0; i < sourceCount; i++)
                sourcePositions[i] += sourceVelocities[i] * dt;
        }

        var momentum = targetMass * targetVelocity;
        foreach (var sourceVelocity in sourceVelocities)
            momentum += sourceMass * sourceVelocity;
        Assert.That(momentum, Is.EqualTo(targetMass * 10f).Within(0.1f));
        return targetVelocity;
    }

    private static Vector2 Force(Vector2 separation, Vector2 velocity)
    {
        return TractorBeamPhysics.CalculateForce(separation, velocity, 10f, 100f, 1f, 1f, 10000f, 1f / 60f);
    }
}
