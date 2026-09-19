using System;
using Content.Shared._WF.TractorBeam;
using NUnit.Framework;

namespace Content.Tests.Shared._WF;

[TestFixture]
[TestOf(typeof(TractorBeamPhysics))]
public sealed class TractorBeamDistancePowerTest
{
    [TestCase(0f)]
    [TestCase(1000f)]
    [TestCase(5000f)]
    public void DistanceRaisesStationaryAndResistingPowerMonotonically(float force)
    {
        var lastStrain = -1f;
        var lastPower = -1f;
        foreach (var distance in new[] { 0f, 25f, 50f, 75f, 100f })
        {
            var distanceStrain = TractorBeamPhysics.CalculateDistanceStrain(distance, 100f);
            var strain = TractorBeamPhysics.CalculateStrain(force, 10000f, distanceStrain);
            var power = TractorBeamPhysics.CalculatePower(force, 10000f, 100000f, 2000000f, distanceStrain);
            Assert.That(strain, Is.GreaterThan(lastStrain));
            Assert.That(power, Is.GreaterThan(lastPower));
            Assert.That(strain, Is.InRange(0f, 1f));
            Assert.That(power, Is.InRange(100000f, 2000000f));
            lastStrain = strain;
            lastPower = power;
        }
        if (force == 0)
            Assert.That(lastPower, Is.EqualTo(1240000f).Within(1f), "A stationary beam at full range still consumes substantial power.");
    }

    [Test]
    public void DistanceBaselineIsQuadraticAndSharesTheMechanicalPowerCap()
    {
        Assert.That(TractorBeamPhysics.CalculateDistanceStrain(25f, 100f), Is.EqualTo(0.0375f).Within(0.000001f));
        Assert.That(TractorBeamPhysics.CalculateDistanceStrain(50f, 100f), Is.EqualTo(0.15f).Within(0.000001f));
        Assert.That(TractorBeamPhysics.CalculateDistanceStrain(100f, 100f), Is.EqualTo(0.6f).Within(0.000001f));
        Assert.That(TractorBeamPhysics.CalculateDistanceStrain(200f, 100f), Is.EqualTo(0.6f).Within(0.000001f));
        Assert.That(TractorBeamPhysics.CalculateStrain(2500f, 10000f, 0.6f), Is.EqualTo(0.8f).Within(0.000001f));
        Assert.That(TractorBeamPhysics.CalculatePower(2500f, 10000f, 100000f, 2000000f, 0.6f),
            Is.EqualTo(1620000f).Within(1f));
        foreach (var baseline in new[] { 0f, 0.15f, 0.6f, 1f })
        {
            Assert.That(TractorBeamPhysics.CalculateStrain(20000f, 10000f, baseline), Is.EqualTo(1f));
            Assert.That(TractorBeamPhysics.CalculatePower(20000f, 10000f, 100000f, 2000000f, baseline),
                Is.EqualTo(2000000f), "Distance and resistance cannot each claim a separate power budget.");
        }
    }

    [TestCase(25f)]
    [TestCase(50f)]
    [TestCase(100f)]
    public void BrownoutPaysDistanceOverheadBeforeBuyingMechanicalAuthority(float distance)
    {
        const float force = 2500f;
        const float maximumForce = 10000f;
        const float holdingPower = 100000f;
        const float maximumPower = 2000000f;
        var distanceStrain = TractorBeamPhysics.CalculateDistanceStrain(distance, 100f);
        var baseline = TractorBeamPhysics.CalculatePower(0f, maximumForce, holdingPower, maximumPower, distanceStrain);
        var requested = TractorBeamPhysics.CalculatePower(force, maximumForce, holdingPower, maximumPower, distanceStrain);
        Assert.That(TractorBeamPhysics.AvailableForceFraction(baseline - 1f, baseline, requested), Is.Zero);
        Assert.That(TractorBeamPhysics.AvailableForceFraction(baseline, baseline, requested), Is.Zero);
        var received = baseline + (requested - baseline) / 2f;
        var affordableForce = force * TractorBeamPhysics.AvailableForceFraction(received, baseline, requested);
        Assert.That(affordableForce, Is.EqualTo(force / 4f).Within(0.01f));
        Assert.That(TractorBeamPhysics.CalculatePower(affordableForce, maximumForce, holdingPower, maximumPower, distanceStrain),
            Is.EqualTo(received).Within(1f), "Reduced restraint must exactly fit the electricity left after the distance baseline.");
    }

    [Test]
    public void ZeroDistanceRetainsExistingMechanicalDefaults()
    {
        Assert.That(TractorBeamPhysics.CalculateDistanceStrain(0f, 100f), Is.Zero);
        Assert.That(TractorBeamPhysics.CalculateStrain(2500f, 10000f), Is.EqualTo(0.5f));
        Assert.That(TractorBeamPhysics.CalculatePower(2500f, 10000f, 100000f, 2000000f), Is.EqualTo(1050000f));
        Assert.That(TractorBeamPhysics.CalculatePower(0f, 10000f, 100000f, 2000000f), Is.EqualTo(100000f));
    }

    [Test]
    public void FiniteSettingsAreClampedAndExtremeInputsStayBounded()
    {
        Assert.That(TractorBeamPhysics.CalculateDistanceStrain(-10f, 100f), Is.Zero);
        Assert.That(TractorBeamPhysics.CalculateDistanceStrain(100f, 100f, -1f), Is.Zero);
        Assert.That(TractorBeamPhysics.CalculateDistanceStrain(100f, 100f, 2f), Is.EqualTo(1f));
        Assert.That(TractorBeamPhysics.CalculateDistanceStrain(float.MaxValue, float.Epsilon), Is.EqualTo(0.6f));
        Assert.That(TractorBeamPhysics.CalculateStrain(2500f, 10000f, -1f), Is.EqualTo(0.5f));
        Assert.That(TractorBeamPhysics.CalculateStrain(0f, 10000f, 2f), Is.EqualTo(1f));
        Assert.That(TractorBeamPhysics.CalculatePower(float.MaxValue, float.Epsilon, 100000f, float.MaxValue, 0.6f),
            Is.EqualTo(float.MaxValue));
    }

    [Test]
    public void InvalidDistanceInputsAndBaselinesFailClosed()
    {
        foreach (var invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            Assert.That(TractorBeamPhysics.CalculateDistanceStrain(invalid, 100f), Is.Zero);
            Assert.That(TractorBeamPhysics.CalculateDistanceStrain(50f, invalid), Is.Zero);
            Assert.That(TractorBeamPhysics.CalculateDistanceStrain(50f, 100f, invalid), Is.Zero);
            Assert.That(TractorBeamPhysics.CalculateStrain(2500f, 10000f, invalid), Is.Zero);
            Assert.That(TractorBeamPhysics.CalculatePower(2500f, 10000f, 100000f, 2000000f, invalid), Is.Zero);
        }
        Assert.That(TractorBeamPhysics.CalculateDistanceStrain(50f, 0f), Is.Zero);
        Assert.That(TractorBeamPhysics.CalculateDistanceStrain(50f, -100f), Is.Zero);
        Assert.That(TractorBeamPhysics.CalculateStrain(-1f, 10000f, 0.6f), Is.Zero);
        Assert.That(TractorBeamPhysics.CalculatePower(-1f, 10000f, 100000f, 2000000f, 0.6f), Is.Zero);
    }
}
