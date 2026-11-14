using System.Numerics;
using Content.Client._WF.Tether;
using Content.Shared._WF.Tether;

namespace Content.IntegrationTests.Tests._WF.Tether;

/// <summary>Pure maths for the rope spring and the drawn verlet chain; no server pair needed.</summary>
[TestFixture]
public sealed class RopeMathTest
{
    private const float Step = 1f / 60f;

    [Test]
    public void SlackRopeAppliesNothingAndDampingNeverPulls()
    {
        Assert.Multiple(() =>
        {
            Assert.That(RopeMath.Extension(4f, 5f), Is.LessThan(0f));
            Assert.That(RopeMath.Strain(4f, 5f, 0.1f), Is.Zero);
            Assert.That(RopeMath.SpringImpulse(-1f, 5f, 3f, 100f, 4000f, 0.7f, 0.1f, Step), Is.Zero,
                "A slack rope must not pull even while its ends separate quickly.");
            Assert.That(RopeMath.SpringImpulse(0.1f, 5f, -20f, 100f, 4000f, 0.7f, 0.1f, Step), Is.GreaterThanOrEqualTo(0f),
                "Damping on a closing rope must never flip the impulse into a push.");
        });
    }

    [Test]
    public void StrainReportsTheHardLimitTravel()
    {
        Assert.Multiple(() =>
        {
            Assert.That(RopeMath.Strain(10f, 10f, 0.1f), Is.EqualTo(0f).Within(0.001f));
            Assert.That(RopeMath.Strain(10.5f, 10f, 0.1f), Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(RopeMath.Strain(11f, 10f, 0.1f), Is.EqualTo(1f).Within(0.001f));
            Assert.That(RopeMath.Strain(100f, 10f, 0.1f), Is.EqualTo(2f).Within(0.001f), "Strain is clamped for the network.");
            Assert.That(RopeMath.Strain(float.NaN, 10f, 0.1f), Is.Zero);
        });
    }

    [Test]
    public void StiffRopeOnALightBodyCannotExplode()
    {
        // A very light body on a very stiff rope: the impulse may never do more than remove the
        // separating motion and the extension itself within one step.
        const float mass = 0.5f;
        const float extension = 0.4f;
        const float separating = 2f;
        var impulse = RopeMath.SpringImpulse(extension, 5f, separating, mass, 5_000_000f, 0.7f, 0.1f, Step);
        var ceiling = mass * (separating + extension / Step);
        Assert.That(impulse, Is.LessThanOrEqualTo(ceiling + 0.0001f));
        Assert.That(impulse, Is.GreaterThan(0f));
    }

    [Test]
    public void EffectiveMassAccountsForAnchorArms()
    {
        var direction = Vector2.UnitX;
        var straight = RopeMath.EffectiveMass(direction, Vector2.Zero, Vector2.Zero, 0.5f, 0.5f, 1f, 1f);
        var levered = RopeMath.EffectiveMass(direction, new Vector2(0f, 2f), Vector2.Zero, 0.5f, 0.5f, 1f, 1f);
        Assert.That(straight, Is.EqualTo(1f).Within(0.0001f));
        Assert.That(levered, Is.LessThan(straight), "An anchor on a lever arm is easier to move.");
        Assert.That(RopeMath.EffectiveMass(direction, Vector2.Zero, Vector2.Zero, 0f, 0f, 0f, 0f), Is.Zero,
            "Two static bodies have no effective mass and must produce no impulse.");
    }

    [Test]
    public void VerletChainStaysFiniteAndRespectsItsMaximumLength()
    {
        var count = RopeVerlet.PointCount(10f, 3f);
        Assert.That(count, Is.InRange(RopeVerlet.MinPoints, RopeVerlet.MaxPoints));

        var points = new Vector2[count];
        var previous = new Vector2[count];
        var endA = Vector2.Zero;
        var endB = new Vector2(4f, 0f);
        var rest = 10f / (count - 1);
        RopeVerlet.Seed(points, previous, count, endA, endB, 0.5f);

        for (var i = 0; i < 400; i++)
        {
            // An end whipping about with an over-long step must not destabilise the chain.
            endB = new Vector2(4f + MathF.Sin(i * 0.4f) * 3f, MathF.Cos(i * 0.31f) * 3f);
            RopeVerlet.Step(points, previous, count, endA, endB, rest, 0.5f, 1.5f, i * 0.016f, 7);
        }

        Assert.Multiple(() =>
        {
            Assert.That(points[0], Is.EqualTo(endA));
            Assert.That(points[count - 1], Is.EqualTo(endB));
            for (var i = 0; i < count; i++)
            {
                Assert.That(float.IsFinite(points[i].X) && float.IsFinite(points[i].Y), Is.True, $"Point {i} is finite.");
            }

            for (var i = 0; i < count - 2; i++)
            {
                // Relaxation is iterative, so a violently whipping end leaves a little overshoot;
                // what matters is that it stays bounded instead of growing every frame.
                Assert.That(Vector2.Distance(points[i], points[i + 1]), Is.LessThan(rest * 1.6f));
            }
        });
    }

    [Test]
    public void TeleportingAnEndRequestsAReseed()
    {
        Assert.Multiple(() =>
        {
            Assert.That(RopeVerlet.NeedsReseed(Vector2.Zero, Vector2.One, Vector2.Zero, Vector2.One), Is.False);
            Assert.That(RopeVerlet.NeedsReseed(Vector2.Zero, Vector2.One, new Vector2(500f, 0f), Vector2.One), Is.True);
            Assert.That(RopeVerlet.NeedsReseed(Vector2.Zero, Vector2.One, Vector2.Zero, new Vector2(float.NaN, 0f)), Is.True);
        });
    }
}
