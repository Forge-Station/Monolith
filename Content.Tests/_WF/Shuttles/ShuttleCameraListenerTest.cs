using System.Numerics;
using Content.Client._WF.Shuttles.Systems;
using NUnit.Framework;

namespace Content.Tests._WF.Shuttles;

[TestFixture]
public sealed class ShuttleCameraListenerTest
{
    private static readonly Vector2 Body = new(10, 5);
    private static readonly Vector2 Eye = new(60, -20);

    [Test]
    public void ASoundBesideTheBodyIsHeardWhereItSitsFromTheBody()
    {
        var source = Body + new Vector2(3, 4);
        Assert.That(ShuttleCameraListenerSystem.TryPlace(Body, Eye - Body, source, 15f, 0f, out var placed, out var delta, out var distance));
        Assert.That(distance, Is.EqualTo(5f).Within(0.001f));
        Assert.That(delta, Is.EqualTo(new Vector2(3, 4)));
        // The engine hears from the eye, so the sound must sit as far from the eye as it really is from the body.
        Assert.That((placed - Eye - (source - Body)).Length(), Is.LessThan(0.001f));
    }

    [Test]
    public void ASoundBesideTheCameraIsSilentWhenTheBodyIsOutOfItsRange()
    {
        var source = Eye + new Vector2(1, 0);
        Assert.That(ShuttleCameraListenerSystem.TryPlace(Body, Eye - Body, source, 15f, 0f, out _, out _, out var distance), Is.False);
        Assert.That(distance, Is.GreaterThan(15f));
    }

    [Test]
    public void RangeCountsTheListenersHeightAsTheEngineDoes()
    {
        var source = Body + new Vector2(9, 0);
        Assert.That(ShuttleCameraListenerSystem.TryPlace(Body, Vector2.Zero, source, 10f, 0f, out _, out _, out _));
        Assert.That(ShuttleCameraListenerSystem.TryPlace(Body, Vector2.Zero, source, 10f, 5f, out _, out _, out _), Is.False,
            "Nine across and five up is further than ten.");
    }

    [Test]
    public void ASoundOnTheBodySnapsToIt()
    {
        var source = Body + new Vector2(0.004f, 0);
        Assert.That(ShuttleCameraListenerSystem.TryPlace(Body, Eye - Body, source, 15f, 0f, out var placed, out var delta, out var distance));
        Assert.That(distance, Is.Zero);
        Assert.That(delta, Is.EqualTo(Vector2.Zero));
        Assert.That(placed, Is.EqualTo(Eye));
    }

    [Test]
    public void WithNoShiftASoundStaysWhereItIs()
    {
        var source = Body + new Vector2(-2, 6);
        Assert.That(ShuttleCameraListenerSystem.TryPlace(Body, Vector2.Zero, source, 15f, 0f, out var placed, out _, out _));
        Assert.That(placed, Is.EqualTo(source));
    }
}
