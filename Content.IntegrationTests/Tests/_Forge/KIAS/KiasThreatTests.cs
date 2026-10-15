using System.Numerics;
using Content.Shared._Forge.KIAS;

namespace Content.IntegrationTests.Tests._Forge.KIAS;

[TestFixture]
public sealed class KiasThreatTests
{
    [Test]
    public void ApproachingRecedingAndMissTrajectories()
    {
        Assert.That(KiasThreatMath.Approaches(new(100, 0), new(-50, 0), 10, out var time), Is.True);
        Assert.That(time, Is.EqualTo(2));
        Assert.That(KiasThreatMath.Approaches(new(100, 0), new(50, 0), 10, out _), Is.False);
        Assert.That(KiasThreatMath.Approaches(new(100, 30), new(-50, 0), 10, out _), Is.False);
        Assert.That(KiasThreatMath.Approaches(new(100, 0), Vector2.Zero, 10, out _), Is.False);
    }

    [Test]
    public void InterceptAndSweptCollision()
    {
        Assert.That(KiasThreatMath.Intercept(new(100, 0), new(-50, 0), 100, out var time), Is.True);
        Assert.That(time, Is.EqualTo(2f / 3).Within(0.001));
        Assert.That(KiasThreatMath.Intercept(new(100, 0), new(200, 0), 100, out _), Is.False);
        Assert.That(KiasThreatMath.SweptHit(new(10, 0), new(-10, 0), 0.4f), Is.True);
        Assert.That(KiasThreatMath.SweptHit(new(10, 2), new(-10, 2), 0.4f), Is.False);
    }
}
