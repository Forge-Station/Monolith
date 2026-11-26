using System;
using System.Numerics;
using Content.Shared._WF.TractorBeam;
using NUnit.Framework;

namespace Content.Tests.Shared._WF;

[TestFixture]
[TestOf(typeof(TractorBeamOperatingCone))]
public sealed class TractorBeamOperatingConeTest
{
    [TestCase(0, 100, true)]
    [TestCase(0, 200, true)]
    [TestCase(0, 200.01f, false)]
    [TestCase(100, 100, true)]
    [TestCase(-100, 100, true)]
    [TestCase(100.01f, 100, false)]
    [TestCase(0, -100, false)]
    [TestCase(100, 0, false)]
    [TestCase(0, 0, true)]
    public void ForwardSectorHasInclusiveAngularAndRangeBoundaries(float x, float y, bool inside)
    {
        Assert.That(TractorBeamOperatingCone.Contains(new Vector2(x, y), Vector2.UnitY, 200,
            TractorBeamOperatingCone.DefaultHalfAngle), Is.EqualTo(inside));
    }

    [TestCase(0f)]
    [TestCase(37f)]
    [TestCase(90f)]
    [TestCase(180f)]
    [TestCase(270f)]
    public void RotatedDishPreservesItsOperatingSector(float degrees)
    {
        var rotation = Matrix3x2.CreateRotation(degrees * MathF.PI / 180f);
        var forward = Vector2.TransformNormal(Vector2.UnitY, rotation);
        Assert.That(TractorBeamOperatingCone.Contains(Vector2.TransformNormal(new Vector2(20, 100), rotation),
            forward, 200, TractorBeamOperatingCone.DefaultHalfAngle), Is.True);
        Assert.That(TractorBeamOperatingCone.Contains(Vector2.TransformNormal(new Vector2(100, 20), rotation),
            forward, 200, TractorBeamOperatingCone.DefaultHalfAngle), Is.False);
    }

    [TestCase(0f, 169f, false)]
    [TestCase(0f, 171f, true)]
    [TestCase(0f, 201f, false)]
    [TestCase(37f, 100f, false)]
    [TestCase(39f, 100f, true)]
    [TestCase(46f, 100f, false)]
    public void WarningPrecedesEitherBoundaryButDoesNotDescribeAnOutsideTarget(float degrees, float distance, bool warning)
    {
        var direction = Vector2.TransformNormal(Vector2.UnitY, Matrix3x2.CreateRotation(degrees * MathF.PI / 180f));
        Assert.That(TractorBeamOperatingCone.IsNearEdge(direction * distance, Vector2.UnitY, 200,
            TractorBeamOperatingCone.DefaultHalfAngle), Is.EqualTo(warning));
    }

    [Test]
    public void InvalidGeometryDoesNotProduceAnOperatingField()
    {
        Assert.Multiple(() =>
        {
            Assert.That(TractorBeamOperatingCone.Contains(Vector2.One, Vector2.Zero, 200, 0.5f), Is.False);
            Assert.That(TractorBeamOperatingCone.Contains(new Vector2(float.NaN, 1), Vector2.UnitY, 200, 0.5f), Is.False);
            Assert.That(TractorBeamOperatingCone.Contains(Vector2.One, Vector2.UnitY, float.PositiveInfinity, 0.5f), Is.False);
            Assert.That(TractorBeamOperatingCone.Contains(Vector2.One, Vector2.UnitY, -1, 0.5f), Is.False);
            Assert.That(TractorBeamOperatingCone.Contains(Vector2.One, Vector2.UnitY, 200, 0), Is.False);
            Assert.That(TractorBeamOperatingCone.Contains(Vector2.One, Vector2.UnitY, 200, MathF.PI), Is.False);
            Assert.That(TractorBeamOperatingCone.IsNearEdge(Vector2.One, Vector2.UnitY, 200, 0.5f, float.NaN), Is.False);
        });
    }
}
