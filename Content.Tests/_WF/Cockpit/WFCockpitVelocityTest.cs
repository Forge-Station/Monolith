using System;
using System.Globalization;
using System.Numerics;
using Content.Client._WF.Cockpit;
using Content.Client._WF.CombatConsole;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests._WF.Cockpit;

/// <summary>Checks bow-relative drift signs and magnitude independently of camera or world heading.</summary>
[TestFixture]
public sealed class WFCockpitVelocityTest
{
    /// <summary>Reads and writes numbers the same way on every machine.</summary>
    [SetUp]
    public void UseInvariantCulture() => CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

    [TestCase(0, 10, 0, 0, -1, 0)]
    [TestCase(0, -10, 0, 0, 1, 180)]
    [TestCase(10, 0, 0, 1, 0, 90)]
    [TestCase(-10, 0, 0, -1, 0, 270)]
    [TestCase(-10, 0, 90, 0, -1, 0)]
    [TestCase(0, 10, 90, 1, 0, 90)]
    [TestCase(0, 10, 180, 0, 1, 180)]
    [TestCase(10, 0, -90, 0, -1, 0)]
    public void CardinalMotionUsesTheShipBow(float x, float y, double rotation, float screenX, float screenY, double bearing)
    {
        var reading = WFCockpitVelocityReading.FromWorld(new Vector2(x, y), Angle.FromDegrees(rotation))!.Value;
        Assert.That(reading.Speed, Is.EqualTo(10).Within(0.0001f));
        Assert.That(reading.ScreenDirection!.Value.X, Is.EqualTo(screenX).Within(0.0001f));
        Assert.That(reading.ScreenDirection.Value.Y, Is.EqualTo(screenY).Within(0.0001f));
        var difference = Math.Abs((reading.BearingDegrees!.Value - bearing + 540) % 360 - 180);
        Assert.That(difference, Is.LessThan(0.0001));
    }

    [TestCase(-450)]
    [TestCase(-73)]
    [TestCase(0)]
    [TestCase(61)]
    [TestCase(400)]
    public void RotatingShipAndMotionTogetherPreservesDrift(double degrees)
    {
        var rotation = Angle.FromDegrees(degrees);
        var expected = new Vector2(3, -4);
        var reading = WFCockpitVelocityReading.FromWorld(rotation.RotateVec(expected), rotation)!.Value;
        Assert.That(reading.Speed, Is.EqualTo(5).Within(0.0001f));
        Assert.That(reading.BowVelocity.X, Is.EqualTo(3).Within(0.0001f));
        Assert.That(reading.BowVelocity.Y, Is.EqualTo(-4).Within(0.0001f));
        Assert.That(reading.ScreenDirection!.Value.X, Is.EqualTo(0.6f).Within(0.0001f));
        Assert.That(reading.ScreenDirection.Value.Y, Is.EqualTo(0.8f).Within(0.0001f));
        Assert.That(reading.BearingDegrees, Is.EqualTo(143.130102).Within(0.0001));
    }

    [TestCase(0, 0)]
    [TestCase(1, 0.1f)]
    [TestCase(4, 0.2f)]
    [TestCase(25, 0.5f)]
    [TestCase(50, 0.7071068f)]
    [TestCase(100, 1)]
    [TestCase(200, 1)]
    public void ArrowLengthMakesSlowDriftVisibleOnAFixedScale(float speed, float expected)
    {
        var reading = WFCockpitVelocityReading.FromWorld(new Vector2(speed, 0), Angle.Zero)!.Value;
        Assert.That(reading.SpeedFraction, Is.EqualTo(expected).Within(0.0001f));
        Assert.That(reading.Speed, Is.EqualTo(speed), "Visual sensitivity must not change the exact speed readout.");
    }

    [Test]
    public void ArrowGrowthIsMonotonicAndDoesNotJumpBetweenSpeedRanges()
    {
        var previous = 0f;
        for (var step = 1; step <= 2000; step++)
        {
            var speed = step / 10f;
            var reading = WFCockpitVelocityReading.FromWorld(new Vector2(speed, 0), Angle.Zero)!.Value;
            Assert.That(reading.SpeedFraction, Is.InRange(previous, 1f));
            if (speed <= WFCockpitVelocityReading.FullScaleSpeed)
                Assert.That(reading.SpeedFraction, Is.GreaterThan(previous));
            if (speed >= 1f)
                Assert.That(reading.SpeedFraction - previous, Is.LessThan(0.01f),
                    "Crossing an instrument range boundary must not suddenly resize the direction arrow.");
            previous = reading.SpeedFraction;
        }
    }

    [Test]
    public void SpeedReadoutKeepsItsWidthAcrossTheVectorScale()
    {
        foreach (var speed in new[] { 0d, 0.04, 5, 12.34, 99.94, WFCockpitVelocityReading.FullScaleSpeed, 123.4 })
            Assert.That(WFGaugeScale.Digits(speed, 0, WFCockpitVelocityReading.FullScaleSpeed, 1), Has.Length.EqualTo(5),
                "The speed digits must not change width as the ship accelerates.");
    }

    [Test]
    public void StoppedAndUnavailableSamplesAreDifferent()
    {
        var stopped = WFCockpitVelocityReading.FromWorld(Vector2.Zero, Angle.Zero)!.Value;
        Assert.That(stopped.Speed, Is.Zero);
        Assert.That(stopped.ScreenDirection, Is.Null);
        Assert.That(stopped.BearingDegrees, Is.Null);
        var tiny = WFCockpitVelocityReading.FromWorld(new Vector2(0.01f, 0), Angle.Zero)!.Value;
        Assert.That(tiny.Speed, Is.GreaterThan(0));
        Assert.That(tiny.ScreenDirection, Is.Null);
        Assert.That(WFCockpitVelocityReading.FromWorld(null, Angle.Zero), Is.Null);
        Assert.That(WFCockpitVelocityReading.FromWorld(Vector2.Zero, null), Is.Null);
        foreach (var invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, float.MaxValue })
            Assert.That(WFCockpitVelocityReading.FromWorld(new Vector2(invalid, 0), Angle.Zero), Is.Null);
        Assert.That(WFCockpitVelocityReading.FromWorld(Vector2.One, new Angle(double.NaN)), Is.Null);
    }
}
