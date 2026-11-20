using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Shared._WF.SectorControl;
using NUnit.Framework;

namespace Content.Tests._WF.SectorControl;

[TestFixture]
[TestOf(typeof(WFSectorHex))]
public sealed class WFSectorHexTest
{
    private const float Size = 3000f;

    private static IEnumerable<WFSectorCell> Cells(int radius)
    {
        return WFSectorHex.Within(WFSectorCell.Origin, radius);
    }

    [TestCase(3000f)]
    [TestCase(1234.5f)]
    public void CentreMapsBackToItsCell(float size)
    {
        foreach (var cell in Cells(10))
        {
            Assert.That(WFSectorHex.CellOf(WFSectorHex.Centre(cell, size), size), Is.EqualTo(cell), $"cell {cell}");
        }
    }

    [Test]
    public void OriginCellIsCentredOnTheOrigin()
    {
        Assert.That(WFSectorHex.Centre(WFSectorCell.Origin, Size), Is.EqualTo(Vector2.Zero));
        Assert.That(WFSectorHex.CellOf(new Vector2(10f, -10f), Size), Is.EqualTo(WFSectorCell.Origin));
    }

    [Test]
    public void PointsNearCornersAndEdgesStayInTheirCell()
    {
        foreach (var cell in Cells(9))
        {
            var centre = WFSectorHex.Centre(cell, Size);
            for (var i = 0; i < 6; i++)
            {
                var corner = WFSectorHex.Corner(cell, Size, i);
                var next = WFSectorHex.Corner(cell, Size, i + 1);
                var nearCorner = Vector2.Lerp(centre, corner, 0.97f);
                var nearEdge = Vector2.Lerp(centre, (corner + next) / 2f, 0.97f);
                Assert.That(WFSectorHex.CellOf(nearCorner, Size), Is.EqualTo(cell), $"corner {i} of {cell}");
                Assert.That(WFSectorHex.CellOf(nearEdge, Size), Is.EqualTo(cell), $"edge {i} of {cell}");
            }
        }
    }

    [Test]
    public void ExactCornersFallInOneOfTheirThreeCells()
    {
        foreach (var cell in Cells(6))
        {
            for (var i = 0; i < 6; i++)
            {
                var corner = WFSectorHex.Corner(cell, Size, i);
                var found = WFSectorHex.CellOf(corner, Size);
                Assert.That(WFSectorHex.Distance(found, cell), Is.LessThanOrEqualTo(1), $"corner {i} of {cell}");
                Assert.That(Vector2.Distance(WFSectorHex.Centre(found, Size), corner), Is.EqualTo(Size).Within(1f));
            }
        }
    }

    [Test]
    public void PointsAcrossTheSectorFallInTheNearestCell()
    {
        var random = new Random(4242);
        for (var i = 0; i < 5000; i++)
        {
            var point = new Vector2(random.NextSingle() * 60000f - 30000f, random.NextSingle() * 60000f - 30000f);
            var cell = WFSectorHex.CellOf(point, Size);
            var own = Vector2.Distance(WFSectorHex.Centre(cell, Size), point);
            Assert.That(own, Is.LessThanOrEqualTo(Size + 0.5f), $"point {point}");
            for (var direction = 0; direction < 6; direction++)
            {
                var other = Vector2.Distance(WFSectorHex.Centre(cell.Neighbour(direction), Size), point);
                Assert.That(own, Is.LessThanOrEqualTo(other + 0.5f), $"point {point} is nearer {cell.Neighbour(direction)} than {cell}");
            }
        }
    }

    [Test]
    public void SixNeighboursFaceTheirEdges()
    {
        var cell = new WFSectorCell(2, -3);
        var centre = WFSectorHex.Centre(cell, Size);
        var neighbours = new HashSet<WFSectorCell>();
        for (var i = 0; i < 6; i++)
        {
            var neighbour = cell.Neighbour(i);
            neighbours.Add(neighbour);
            Assert.That(WFSectorHex.Distance(cell, neighbour), Is.EqualTo(1));

            var offset = WFSectorHex.Centre(neighbour, Size) - centre;
            Assert.That(offset.Length(), Is.EqualTo(MathF.Sqrt(3f) * Size).Within(0.5f));

            var angle = MathF.Atan2(offset.Y, offset.X) * 180f / MathF.PI;
            var expected = 30f + 60f * i;
            Assert.That(((angle - expected) % 360f + 360f) % 360f, Is.EqualTo(0f).Within(0.01f).Or.EqualTo(360f).Within(0.01f));

            // Edge i lies halfway between the two centres.
            var midpoint = (WFSectorHex.Corner(cell, Size, i) + WFSectorHex.Corner(cell, Size, i + 1)) / 2f;
            Assert.That(Vector2.Distance(midpoint, centre + offset / 2f), Is.LessThan(0.5f));
        }

        Assert.That(neighbours, Has.Count.EqualTo(6));
        Assert.That(cell.Neighbour(6), Is.EqualTo(cell.Neighbour(0)));
        Assert.That(cell.Neighbour(-1), Is.EqualTo(cell.Neighbour(5)));
    }

    [Test]
    public void RingsAndDistance()
    {
        var centre = new WFSectorCell(-1, 4);
        Assert.That(WFSectorHex.Ring(centre, 0), Is.EqualTo(new List<WFSectorCell> { centre }));
        for (var radius = 1; radius <= 6; radius++)
        {
            var ring = WFSectorHex.Ring(centre, radius);
            Assert.That(ring, Has.Count.EqualTo(6 * radius));
            Assert.That(ring.Distinct().Count(), Is.EqualTo(ring.Count));
            Assert.That(ring.All(cell => WFSectorHex.Distance(centre, cell) == radius), Is.True, $"ring {radius}");
        }

        var within = WFSectorHex.Within(centre, 4);
        Assert.That(within, Has.Count.EqualTo(1 + 3 * 4 * 5));
        Assert.That(within.Distinct().Count(), Is.EqualTo(within.Count));

        var a = new WFSectorCell(3, -7);
        var b = new WFSectorCell(-2, 5);
        Assert.That(WFSectorHex.Distance(a, b), Is.EqualTo(WFSectorHex.Distance(b, a)));
        Assert.That(WFSectorHex.Distance(a, a), Is.Zero);
        Assert.That(WFSectorHex.Distance(WFSectorCell.Origin, new WFSectorCell(3, -1)), Is.EqualTo(3));
    }

    [TestCase(30000f)]
    [TestCase(4500f)]
    [TestCase(0f)]
    public void CellsWithinARadiusAreEveryCentreInsideIt(float radius)
    {
        var cells = WFSectorHex.CellsWithin(radius, Size);
        Assert.That(cells, Does.Contain(WFSectorCell.Origin));
        Assert.That(cells.Distinct().Count(), Is.EqualTo(cells.Count));

        // Every cell a few rings past the radius whose centre is inside it, and no other.
        var expected = Cells((int) (radius / Size) + 3)
            .Where(cell => WFSectorHex.Centre(cell, Size).Length() <= radius)
            .ToHashSet();
        Assert.That(cells.ToHashSet().SetEquals(expected), Is.True);
        Assert.That(WFSectorHex.CellsWithin(-1f, Size), Is.Empty);
    }

    [Test]
    public void CornersRunAnticlockwiseFromEast()
    {
        var cell = new WFSectorCell(1, 2);
        var centre = WFSectorHex.Centre(cell, Size);
        Span<Vector2> corners = stackalloc Vector2[6];
        WFSectorHex.Corners(cell, Size, corners);

        Assert.That(Vector2.Distance(corners[0], centre + new Vector2(Size, 0f)), Is.LessThan(0.5f));
        for (var i = 0; i < 6; i++)
        {
            var corner = corners[i];
            var next = corners[(i + 1) % 6];
            Assert.That(Vector2.Distance(corner, WFSectorHex.Corner(cell, Size, i)), Is.LessThan(0.01f));
            Assert.That(Vector2.Distance(corner, centre), Is.EqualTo(Size).Within(0.5f));
            Assert.That(Vector2.Distance(corner, next), Is.EqualTo(Size).Within(0.5f));

            var a = corner - centre;
            var b = next - centre;
            Assert.That(a.X * b.Y - a.Y * b.X, Is.GreaterThan(0f), $"corner {i}");
        }
    }

    [Test]
    public void CallsignsAreUniqueAndStable()
    {
        var seen = new Dictionary<string, WFSectorCell>();
        for (var q = -45; q <= 45; q++)
        {
            for (var r = -45; r <= 45; r++)
            {
                var cell = new WFSectorCell(q, r);
                var callsign = WFSectorHex.Callsign(cell);
                Assert.That(WFSectorHex.Callsign(new WFSectorCell(q, r)), Is.EqualTo(callsign));
                Assert.That(seen.TryAdd(callsign, cell), Is.True, $"{callsign} is both {cell} and {seen.GetValueOrDefault(callsign)}");
            }
        }

        Assert.That(WFSectorHex.Callsign(WFSectorCell.Origin), Is.EqualTo("N-13"));
        Assert.That(WFSectorHex.Callsign(new WFSectorCell(1, 0)), Is.EqualTo("O-13"));
        Assert.That(WFSectorHex.Callsign(new WFSectorCell(0, 1)), Is.EqualTo("N-12"));
        Assert.That(WFSectorHex.Callsign(new WFSectorCell(-13, 0)), Is.EqualTo("A-20"));
        Assert.That(WFSectorHex.Callsign(new WFSectorCell(13, 0)), Is.EqualTo("AA-7"));
    }
}
