using System;
using System.Numerics;
using Content.Shared._WF.TractorBeam;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests.Shared._WF;

[TestFixture]
[TestOf(typeof(TractorBeamGeometry))]
public sealed class TractorBeamGeometryTest
{
    [Test]
    public void VisibleBeamEndsBeforeNearHullRatherThanAtShipCenter()
    {
        var hull = new Box2(-2, -5, 2, 5);
        Assert.That(TractorBeamGeometry.ClipVisualSection(Vector2.Zero, new Vector2(20, 0), new Vector2(20, 0),
            hull, Matrix3x2.CreateTranslation(-20, 0), 0.75f, out var left, out var right), Is.True);
        Assert.That(left.X, Is.EqualTo(20f - 2f * MathF.Sqrt(2f) - 0.75f).Within(0.0001f));
        Assert.That(left.Y, Is.Zero);
        Assert.That(right, Is.EqualTo(left));

        // The gap is only visual: a loose object in front of the hull still belongs to the field.
        Assert.That(TractorBeamGeometry.ContainsPoint(Vector2.Zero, new Vector2(20, 0),
            new Vector2(17.9f, 0), 5), Is.True);
    }

    [Test]
    public void VisibleCutoutHasRoundedCorners()
    {
        var hull = new Box2(-2, -2, 2, 2);
        Assert.That(TractorBeamGeometry.ClipVisualSection(new Vector2(-10, -10), Vector2.Zero, Vector2.Zero,
            hull, Matrix3x2.Identity, 0.75f, out var left, out _), Is.True);
        Assert.That(left.X, Is.EqualTo(-2f - 0.75f / MathF.Sqrt(2f)).Within(0.0001f));
        Assert.That(left.Y, Is.EqualTo(left.X));
    }

    [Test]
    public void VisibleFanEndsInConcaveCurveAcrossTargetWidth()
    {
        var hull = new Box2(18, -5, 22, 5);
        Assert.That(TractorBeamGeometry.ClipVisualSection(Vector2.Zero, new Vector2(20, 0), new Vector2(20, 5),
            hull, Matrix3x2.Identity, 0.75f, out var middle, out var edge), Is.True);
        Assert.That(edge.X, Is.GreaterThan(middle.X + 0.3f),
            "The fan should wrap around a rounded opening, rather than end at a flat hull-facing plane.");
    }

    [TestCase(0f, 2f, 8f)]
    [TestCase(45f, 2f, 8f)]
    [TestCase(90f, 2f, 8f)]
    [TestCase(135f, 2f, 8f)]
    [TestCase(0f, 0.2f, 30f)]
    [TestCase(15f, 0.2f, 30f)]
    [TestCase(45f, 0.2f, 30f)]
    [TestCase(90f, 0.2f, 30f)]
    public void VisibleFanDoesNotCoverRotatedOrLongNarrowHulls(float degrees, float halfWidth, float halfLength)
    {
        // Off-center bounds also verify that the cutout follows the hull, not the grid origin.
        var hull = new Box2(-halfWidth + 3, -halfLength - 7, halfWidth + 3, halfLength - 7);
        var matrix = Matrix3x2.CreateRotation(degrees * MathF.PI / 180f) * Matrix3x2.CreateTranslation(50, 20);
        Assert.That(Matrix3x2.Invert(matrix, out var inverse), Is.True);
        var start = new Vector2(-15, -25);
        var end = Vector2.Transform(hull.Center, matrix);
        var across = Vector2.Normalize(new Vector2(-(end - start).Y, (end - start).X));
        var width = TractorBeamGeometry.GetTargetHalfWidth(start, end, hull, matrix);
        var fanLeft = end - across * width;
        var fanRight = end + across * width;
        var overlaps = false;
        var clippedCount = 0;
        for (var section = 0; section < 48; section++)
        {
            Assert.That(TractorBeamGeometry.ClipVisualSection(start,
                Vector2.Lerp(fanLeft, fanRight, section / 48f),
                Vector2.Lerp(fanLeft, fanRight, (section + 1) / 48f), hull, inverse, 0.75f,
                out var left, out var right), Is.True);
            if (Vector2.Distance(start, (left + right) / 2) < Vector2.Distance(start, end))
                clippedCount++;

            // Sample the complete drawn triangle, including the chord across its clipped end.
            for (var along = 0; along <= 30; along++)
            for (var sideways = 0; sideways <= 30; sideways++)
            {
                var point = Vector2.Lerp(start, Vector2.Lerp(left, right, sideways / 30f), along / 30f);
                overlaps |= hull.Contains(Vector2.Transform(point, inverse));
            }
        }
        Assert.That(clippedCount, Is.GreaterThan(0));
        Assert.That(overlaps, Is.False, "The visible fan must leave the entire target hull clear.");
    }

    [Test]
    public void CoarseVisualSectionStillKeepsItsEndChordOutsideHull()
    {
        var hull = new Box2(18, -2, 22, 2);
        // Both outer rays miss the hull, but the triangle between them would cover it.
        Assert.That(TractorBeamGeometry.ClipVisualSection(Vector2.Zero, new Vector2(20, 10), new Vector2(20, -10),
            hull, Matrix3x2.Identity, 0.75f, out var left, out var right), Is.True);
        Assert.That(left.X, Is.LessThan(hull.Left - 0.18f));
        Assert.That(right.X, Is.LessThan(hull.Left - 0.18f));
    }

    [Test]
    public void DishInsideHullClearanceOrInvalidGeometryHasNoVisibleSection()
    {
        var hull = new Box2(-2, -2, 2, 2);
        Assert.That(TractorBeamGeometry.ClipVisualSection(Vector2.Zero, Vector2.One, -Vector2.One,
            hull, Matrix3x2.Identity, 0.75f, out _, out _), Is.False);
        Assert.That(TractorBeamGeometry.ClipVisualSection(new Vector2(-2.5f, 0), Vector2.One, -Vector2.One,
            hull, Matrix3x2.Identity, 0.75f, out _, out _), Is.False);
        Assert.That(TractorBeamGeometry.ClipVisualSection(new Vector2(-10, 0), Vector2.One, -Vector2.One,
            hull, Matrix3x2.Identity, float.NaN, out _, out _), Is.False);
        Assert.That(TractorBeamGeometry.ClipVisualSection(new Vector2(-10, 0), Vector2.One, -Vector2.One,
            hull, Matrix3x2.CreateScale(float.NaN), 0.75f, out _, out _), Is.False);
    }

    [TestCase(10f)]
    [TestCase(100f)]
    [TestCase(10000f)]
    public void FanWidthMatchesTargetHullRegardlessOfDistance(float length)
    {
        Assert.That(TractorBeamGeometry.GetTargetHalfWidth(Vector2.Zero, new Vector2(length, 0),
            new Box2(-2, -30, 2, 30), Matrix3x2.Identity), Is.EqualTo(30f));
    }

    [TestCase(0f, 30f)]
    [TestCase(90f, 2f)]
    [TestCase(45f, 22.627417f)]
    public void FanWidthProjectsRotatedTargetHull(float degrees, float expected)
    {
        var matrix = Matrix3x2.CreateRotation(degrees * MathF.PI / 180f) *
                     Matrix3x2.CreateTranslation(100, 200);
        Assert.That(TractorBeamGeometry.GetTargetHalfWidth(new Vector2(10, 20), new Vector2(110, 20),
            new Box2(-2, -30, 2, 30), matrix), Is.EqualTo(expected).Within(0.0001f));
    }

    [Test]
    public void FanWidthUsesBeamDirectionAndNarrowTargetsStayNarrow()
    {
        var bounds = new Box2(-0.2f, -30, 0.2f, 30);
        Assert.That(TractorBeamGeometry.GetTargetHalfWidth(Vector2.Zero, new Vector2(0, 100),
            bounds, Matrix3x2.Identity), Is.EqualTo(0.2f));
        Assert.That(TractorBeamGeometry.ContainsPoint(Vector2.Zero, new Vector2(100, 0),
            new Vector2(50, 10), 30f), Is.True);
        Assert.That(TractorBeamGeometry.ContainsPoint(Vector2.Zero, new Vector2(100, 0),
            new Vector2(50, 10), 2f), Is.False);
        Assert.That(TractorBeamGeometry.IntersectsBox(Vector2.Zero, new Vector2(100, 0),
            new Box2(49, 9, 51, 11), 30f), Is.True);
        Assert.That(TractorBeamGeometry.IntersectsBox(Vector2.Zero, new Vector2(100, 0),
            new Box2(49, 9, 51, 11), 2f), Is.False);
    }

    [TestCase(0f)]
    [TestCase(-1f)]
    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    public void InvalidWidthFailsClosed(float width)
    {
        Assert.That(TractorBeamGeometry.ContainsPoint(Vector2.Zero, new Vector2(100, 0),
            new Vector2(50, 0), width), Is.False);
        Assert.That(TractorBeamGeometry.IntersectsBox(Vector2.Zero, new Vector2(100, 0),
            new Box2(49, -1, 51, 1), width), Is.False);
    }

    [Test]
    public void InvalidTargetGeometryHasNoWidth()
    {
        var bounds = new Box2(-2, -30, 2, 30);
        Assert.That(TractorBeamGeometry.GetTargetHalfWidth(Vector2.Zero, Vector2.Zero,
            bounds, Matrix3x2.Identity), Is.Zero);
        Assert.That(TractorBeamGeometry.GetTargetHalfWidth(Vector2.Zero, new Vector2(float.NaN, 0),
            bounds, Matrix3x2.Identity), Is.Zero);
        Assert.That(TractorBeamGeometry.GetTargetHalfWidth(Vector2.Zero, new Vector2(100, 0),
            new Box2(-2, -30, float.PositiveInfinity, 30), Matrix3x2.Identity), Is.Zero);
        Assert.That(TractorBeamGeometry.GetTargetHalfWidth(Vector2.Zero, new Vector2(100, 0),
            bounds, Matrix3x2.CreateScale(float.NaN)), Is.Zero);
    }

    [TestCase(0f, 0f, true)]
    [TestCase(12.5f, 0.75f, true)]
    [TestCase(12.5f, -0.75f, true)]
    [TestCase(25f, 1.5f, true)]
    [TestCase(25f, -1.5f, true)]
    [TestCase(25f, 0f, true)]
    [TestCase(-0.001f, 0f, false)]
    [TestCase(25.001f, 0f, false)]
    [TestCase(12.5f, 0.751f, false)]
    [TestCase(0f, 0.001f, false)]
    public void PointTestIncludesBoundariesButNotBeyondFan(float x, float y, bool expected)
    {
        Assert.That(TractorBeamGeometry.ContainsPoint(Vector2.Zero, new Vector2(25f, 0f),
            new Vector2(x, y), 1.5f), Is.EqualTo(expected));
    }

    [Test]
    public void RotatedTranslatedFanUsesWorldDirection()
    {
        var start = new Vector2(100f, -200f);
        var end = start + new Vector2(0f, 25f);
        Assert.That(TractorBeamGeometry.ContainsPoint(start, end, start + new Vector2(0.75f, 12.5f), 1.5f), Is.True);
        Assert.That(TractorBeamGeometry.ContainsPoint(start, end, start + new Vector2(0.751f, 12.5f), 1.5f), Is.False);
        Assert.That(TractorBeamGeometry.ContainsPoint(start, end, start + new Vector2(0f, -1f), 1.5f), Is.False);
        Assert.That(TractorBeamGeometry.IntersectsBox(start, end, new Box2(99.9f, -188f, 100.1f, -187f), 1.5f), Is.True);
        Assert.That(TractorBeamGeometry.IntersectsBox(start, end, new Box2(102f, -188f, 103f, -187f), 1.5f), Is.False);
    }

    [Test]
    public void BoxCrossingBothFanEdgesIntersectsWithoutAnyContainedVertices()
    {
        // The narrow vertical box crosses both fan edges, but contains no triangle vertex;
        // all four box corners are also outside the triangle.
        Assert.That(TractorBeamGeometry.IntersectsBox(Vector2.Zero, new Vector2(25f, 0f),
            new Box2(10f, -2f, 11f, 2f), 1.5f), Is.True);
    }

    [Test]
    public void BoxContainingEntireFanIntersects()
    {
        Assert.That(TractorBeamGeometry.IntersectsBox(Vector2.Zero, new Vector2(25f, 0f),
            new Box2(-1f, -2f, 26f, 2f), 1.5f), Is.True);
    }

    [Test]
    public void BoxTouchingEndCapOrEdgeIntersectsButJustOutsideDoesNot()
    {
        var end = new Vector2(25f, 0f);
        Assert.That(TractorBeamGeometry.IntersectsBox(Vector2.Zero, end, new Box2(25f, -1f, 26f, 1f), 1.5f), Is.True);
        Assert.That(TractorBeamGeometry.IntersectsBox(Vector2.Zero, end, new Box2(25.001f, -1f, 26f, 1f), 1.5f), Is.False);
        Assert.That(TractorBeamGeometry.IntersectsBox(Vector2.Zero, end, new Box2(12f, 0.75f, 12.5f, 1f), 1.5f), Is.True);
        Assert.That(TractorBeamGeometry.IntersectsBox(Vector2.Zero, end, new Box2(12f, 0.751f, 12.5f, 1f), 1.5f), Is.False);
        Assert.That(TractorBeamGeometry.IntersectsBox(Vector2.Zero, end, new Box2(-2f, -1f, -0.001f, 1f), 1.5f), Is.False);
    }

    [Test]
    public void DiagonalFanRejectsBoxInsideEnclosingBoundsButOutsideTriangle()
    {
        var end = new Vector2(20f, 20f);
        Assert.That(TractorBeamGeometry.IntersectsBox(Vector2.Zero, end, new Box2(9f, 9f, 11f, 11f), 1.5f), Is.True);
        Assert.That(TractorBeamGeometry.IntersectsBox(Vector2.Zero, end, new Box2(1f, 18f, 2f, 19f), 1.5f), Is.False);
        Assert.That(TractorBeamGeometry.ContainsPoint(Vector2.Zero, end, new Vector2(10f, 10f), 1.5f), Is.True);
        Assert.That(TractorBeamGeometry.ContainsPoint(Vector2.Zero, end, end, 1.5f), Is.True);
    }

    [Test]
    public void DegenerateConeFailsClosedAndPointBoxCanIntersect()
    {
        Assert.That(TractorBeamGeometry.ContainsPoint(Vector2.One, Vector2.One, Vector2.One, 1.5f), Is.False);
        Assert.That(TractorBeamGeometry.IntersectsBox(Vector2.One, Vector2.One, new Box2(0f, 0f, 2f, 2f), 1.5f), Is.False);
        Assert.That(TractorBeamGeometry.IntersectsBox(Vector2.Zero, new Vector2(25f, 0f),
            new Box2(12.5f, 0.75f, 12.5f, 0.75f), 1.5f), Is.True);
    }

    [Test]
    public void NonFiniteCoordinatesFailClosed()
    {
        var end = new Vector2(25f, 0f);
        foreach (var invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            var invalidPoint = new Vector2(invalid, 0f);
            Assert.That(TractorBeamGeometry.ContainsPoint(invalidPoint, end, Vector2.One, 1.5f), Is.False);
            Assert.That(TractorBeamGeometry.ContainsPoint(Vector2.Zero, invalidPoint, Vector2.One, 1.5f), Is.False);
            Assert.That(TractorBeamGeometry.ContainsPoint(Vector2.Zero, end, invalidPoint, 1.5f), Is.False);
            Assert.That(TractorBeamGeometry.IntersectsBox(invalidPoint, end, new Box2(0f, 0f, 1f, 1f), 1.5f), Is.False);
            Assert.That(TractorBeamGeometry.IntersectsBox(Vector2.Zero, invalidPoint, new Box2(0f, 0f, 1f, 1f), 1.5f), Is.False);
            Assert.That(TractorBeamGeometry.IntersectsBox(Vector2.Zero, end, new Box2(0f, 0f, invalid, 1f), 1.5f), Is.False);
        }
    }
}
