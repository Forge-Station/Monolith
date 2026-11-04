using System.Numerics;
using Content.Shared._WF.Vehicles.Grid;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests._WF.Vehicles;

/// <summary>The grid vehicle mover's speed maths: acceleration, braking, reversing and target advance.</summary>
[TestFixture]
public sealed class GridVehicleMotionSimulatorTest
{
    [Test]
    public void CollisionImpulseConservesMomentumAndDoesNotAttractSeparatingVehicles()
    {
        var impulse = GridVehicleMotionSimulator.CollisionImpulse(10f, 1000f, 2000f);
        var sourceSpeed = 10f - impulse / 1000f;
        var targetSpeed = impulse / 2000f;
        Assert.That(sourceSpeed * 1000f + targetSpeed * 2000f, Is.EqualTo(10000f).Within(0.01f));
        Assert.That(targetSpeed - sourceSpeed, Is.EqualTo(1.5f).Within(0.001f));
        Assert.That(GridVehicleMotionSimulator.CollisionImpulse(-5f, 1000f, 2000f), Is.Zero);
    }

    private const float Step = 1f / 60f;

    private static readonly GridVehicleMotionSimulator.DriveProfile Profile = new(
        MaxSpeed: 6f,
        MaxReverseSpeed: 2f,
        Acceleration: 5f,
        ReverseAcceleration: 4f,
        Deceleration: 12f);

    private static readonly Vector2i North = new(0, 1);
    private static readonly Vector2i South = new(0, -1);

    [TestCase(3f, 12f, 0.1f, 1.8f)]
    [TestCase(-3f, 12f, 0.1f, -1.8f)]
    [TestCase(1f, 12f, 0.5f, 0f)]
    [TestCase(-1f, 12f, 0.5f, 0f)]
    [TestCase(0f, 12f, 0.5f, 0f)]
    public void IdleSpeedDecaysTowardZeroWithoutCrossingIt(float speed, float deceleration, float frameTime, float expected)
    {
        Assert.That(GridVehicleMotionSimulator.StepIdleSpeed(speed, deceleration, frameTime), Is.EqualTo(expected).Within(0.0001f));
    }

    [Test]
    public void DrivingForwardAcceleratesAtTheProfileRateAndStopsAtTopSpeed()
    {
        var speed = 0f;
        var result = GridVehicleMotionSimulator.StepDriveSpeed(speed, Profile, North, North, hasInput: true, isCommittedToMove: false, 0.2f);

        Assert.That(result.CurrentSpeed, Is.EqualTo(1f).Within(0.0001f), "Five tiles per second squared for 0.2 s.");
        Assert.That(result.ReversingInput, Is.False);
        Assert.That(result.ChangingDirection, Is.False);

        speed = 0f;
        for (var i = 0; i < 600; i++)
        {
            speed = GridVehicleMotionSimulator.StepDriveSpeed(speed, Profile, North, North, true, false, Step).CurrentSpeed;
            Assert.That(speed, Is.LessThanOrEqualTo(Profile.MaxSpeed));
        }

        Assert.That(speed, Is.EqualTo(Profile.MaxSpeed), "Ten seconds of throttle reaches the top speed exactly.");
    }

    [Test]
    public void ReleasingTheThrottleBrakesAtTheDecelerationRate()
    {
        var result = GridVehicleMotionSimulator.StepDriveSpeed(6f, Profile, North, Vector2i.Zero, hasInput: false, isCommittedToMove: false, 0.25f);

        Assert.That(result.CurrentSpeed, Is.EqualTo(3f).Within(0.0001f), "Twelve tiles per second squared for a quarter second.");
        Assert.That(result.ReversingInput, Is.False);
    }

    [Test]
    public void ReversingFromForwardMotionBrakesBeforeItBacksUp()
    {
        var first = GridVehicleMotionSimulator.StepDriveSpeed(3f, Profile, North, South, true, false, 0.1f);

        Assert.That(first.ReversingInput, Is.True);
        Assert.That(first.CurrentSpeed, Is.EqualTo(1.8f).Within(0.0001f), "It brakes at the deceleration rate, not the reverse acceleration.");
        Assert.That(first.ChangingDirection, Is.True, "Still rolling forward while the input says reverse.");

        var speed = 3f;
        var steps = 0;
        while (speed > 0f && steps < 600)
        {
            speed = GridVehicleMotionSimulator.StepDriveSpeed(speed, Profile, North, South, true, false, Step).CurrentSpeed;
            steps++;
        }

        Assert.That(speed, Is.EqualTo(0f), "It comes to rest before reversing.");

        var reverse = GridVehicleMotionSimulator.StepDriveSpeed(0f, Profile, North, South, true, false, 0.05f);
        Assert.That(reverse.CurrentSpeed, Is.EqualTo(-0.2f).Within(0.0001f),
            "Backing up uses the reverse acceleration, independently of braking strength.");
        Assert.That(reverse.ChangingDirection, Is.False);
    }

    [Test]
    public void ReverseSpeedIsCappedAtTheReverseLimit()
    {
        var speed = 0f;
        for (var i = 0; i < 600; i++)
            speed = GridVehicleMotionSimulator.StepDriveSpeed(speed, Profile, North, South, true, false, Step).CurrentSpeed;

        Assert.That(speed, Is.EqualTo(-Profile.MaxReverseSpeed));
    }

    [Test]
    public void ThrottleWhileRollingBackwardsBrakesFirst()
    {
        var result = GridVehicleMotionSimulator.StepDriveSpeed(-1.5f, Profile, North, North, true, false, 0.05f);

        Assert.That(result.ReversingInput, Is.False);
        Assert.That(result.CurrentSpeed, Is.EqualTo(-0.9f).Within(0.0001f));
        Assert.That(result.ChangingDirection, Is.True);
    }

    [Test]
    public void ACommittedMoveKeepsDrivingWithoutInput()
    {
        var committed = GridVehicleMotionSimulator.StepDriveSpeed(0f, Profile, North, Vector2i.Zero, hasInput: false, isCommittedToMove: true, 0.2f);
        var idle = GridVehicleMotionSimulator.StepDriveSpeed(0f, Profile, North, Vector2i.Zero, hasInput: false, isCommittedToMove: false, 0.2f);

        Assert.That(committed.CurrentSpeed, Is.EqualTo(1f).Within(0.0001f));
        Assert.That(idle.CurrentSpeed, Is.EqualTo(0f));
    }

    [Test]
    public void PushSpeedAcceleratesWithInputAndDecaysWithout()
    {
        Assert.That(GridVehicleMotionSimulator.StepPushSpeed(0f, 4f, 5f, 12f, true, false, 0.1f), Is.EqualTo(0.5f).Within(0.0001f));
        Assert.That(GridVehicleMotionSimulator.StepPushSpeed(3.9f, 4f, 5f, 12f, true, false, 1f), Is.EqualTo(4f));
        Assert.That(GridVehicleMotionSimulator.StepPushSpeed(2f, 4f, 5f, 12f, false, false, 0.1f), Is.EqualTo(0.8f).Within(0.0001f));
        Assert.That(GridVehicleMotionSimulator.StepPushSpeed(2f, 4f, 5f, 12f, false, true, 0.1f), Is.EqualTo(2.5f).Within(0.0001f),
            "A committed move counts as input.");
    }

    [Test]
    public void AdvanceToTargetStopsAtTheTargetAndReportsWhatIsLeft()
    {
        var reached = GridVehicleMotionSimulator.AdvanceToTarget(Vector2.Zero, Vector2i.Zero, new Vector2i(1, 0), new Vector2(2f, 0f), 3f);

        Assert.That(reached.ReachedTarget, Is.True);
        Assert.That(reached.Position, Is.EqualTo(new Vector2(2f, 0f)));
        Assert.That(reached.CurrentTile, Is.EqualTo(new Vector2i(1, 0)));
        Assert.That(reached.RemainingDistance, Is.EqualTo(1f).Within(0.0001f));

        var partial = GridVehicleMotionSimulator.AdvanceToTarget(Vector2.Zero, Vector2i.Zero, new Vector2i(1, 0), new Vector2(0f, 4f), 1.5f);

        Assert.That(partial.ReachedTarget, Is.False);
        Assert.That(partial.Position, Is.EqualTo(new Vector2(0f, 1.5f)));
        Assert.That(partial.CurrentTile, Is.EqualTo(Vector2i.Zero), "The tile only changes on arrival.");
        Assert.That(partial.RemainingDistance, Is.EqualTo(0f));
    }

    private static readonly Box2 Hull = new(-0.425f, -1.2f, 0.425f, 1.2f);

    private static Box2 TileAt(float x, float y)
    {
        return new Box2(x - 0.5f, y - 0.5f, x + 0.5f, y + 0.5f);
    }

    /// <summary>A hull at rotation zero faces south; only what lies off its nose is a front impact.</summary>
    [TestCase(0f, -1.7f, true)]
    [TestCase(0.4f, -1.7f, true)]
    [TestCase(0f, 1.7f, false)]
    [TestCase(0.9f, 0f, false)]
    [TestCase(-0.9f, -0.5f, false)]
    public void FrontImpactIsOffTheNose(float x, float y, bool expected)
    {
        Assert.That(GridVehicleMotionSimulator.IsFrontImpact(Vector2.Zero, Angle.Zero, Hull, TileAt(x, y)), Is.EqualTo(expected));
    }

    /// <summary>The front face turns with the hull, at any angle.</summary>
    [TestCase(90f)]
    [TestCase(30f)]
    [TestCase(215f)]
    public void FrontImpactFollowsTheHeading(float degrees)
    {
        var rotation = Angle.FromDegrees(degrees);
        var ahead = rotation.ToWorldVec() * 1.7f;
        var position = new Vector2(8f, 3f);

        Assert.Multiple(() =>
        {
            Assert.That(GridVehicleMotionSimulator.IsFrontImpact(position, rotation, Hull, TileAt(position.X + ahead.X, position.Y + ahead.Y)), Is.True);
            Assert.That(GridVehicleMotionSimulator.IsFrontImpact(position, rotation, Hull, TileAt(position.X - ahead.X, position.Y - ahead.Y)), Is.False);
        });
    }

    /// <summary>Steering is mirrored while the vehicle rolls, or is about to roll, backwards.</summary>
    [TestCase(1f, 2f, 1f, 1f)]
    [TestCase(1f, -2f, 0f, -1f)]
    [TestCase(1f, 0f, -1f, -1f)]
    [TestCase(-1f, 0f, 1f, -1f)]
    public void SteeringMirrorsInReverse(float steering, float speed, float throttle, float expected)
    {
        Assert.That(GridVehicleMotionSimulator.GetEffectiveSteering(steering, speed, throttle), Is.EqualTo(expected));
    }

    /// <summary>Wheels need to roll to steer; a vehicle that turns on the spot never does.</summary>
    [TestCase(false, 0f, false)]
    [TestCase(false, 1f, true)]
    [TestCase(false, -1f, true)]
    [TestCase(true, 0f, true)]
    public void OnlyARollingOrTurnInPlaceVehicleSteers(bool turnInPlace, float speed, bool expected)
    {
        Assert.That(GridVehicleMotionSimulator.CanSteer(turnInPlace, speed), Is.EqualTo(expected));
    }
}
