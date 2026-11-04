using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client._WF.PlanetMap;
using Content.Server._WF.PlanetMap;
using Content.Shared._WF.PlanetMap;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests._WF.PlanetMap;

/// <summary>Road tiles become few, faithful polylines within the point budget, and terrain survives its encoding.</summary>
[TestFixture]
[TestOf(typeof(WFPlanetMapGeometry))]
public sealed class PlanetMapGeometryTest
{
    private static List<Vector2i> Run(Vector2i from, Vector2i step, int count)
    {
        var points = new List<Vector2i>();
        for (var i = 0; i < count; i++)
            points.Add(from + step * i);
        return points;
    }

    [Test]
    public void SimplifyKeepsEndsAndCornersAndDropsStraightPoints()
    {
        // An L: east along y=0, then north along x=10.
        var points = Run(new Vector2i(0, 0), new Vector2i(1, 0), 10).Concat(Run(new Vector2i(10, 0), new Vector2i(0, 1), 11)).ToList();

        var simple = WFPlanetMapGeometry.Simplify(points, 1f);

        Assert.That(simple, Is.EqualTo(new[] { new Vector2i(0, 0), new Vector2i(10, 0), new Vector2i(10, 10) }));
    }

    [Test]
    public void SimplifyRespectsTolerance()
    {
        // A bump two tiles high in the middle of a straight run.
        var points = new List<Vector2i> { new(0, 0), new(5, 0), new(6, 2), new(7, 0), new(12, 0) };

        Assert.That(WFPlanetMapGeometry.Simplify(points, 1f), Does.Contain(new Vector2i(6, 2)), "A bump past the tolerance was lost.");
        Assert.That(WFPlanetMapGeometry.Simplify(points, 3f), Is.EqualTo(new[] { new Vector2i(0, 0), new Vector2i(12, 0) }),
            "A bump within the tolerance was kept.");
        Assert.That(WFPlanetMapGeometry.Simplify(new List<Vector2i> { new(3, 3), new(4, 4) }, 5f), Has.Count.EqualTo(2));
    }

    [Test]
    public void LinesSplitAtJunctionsAndTunnels()
    {
        // A T: a road east-west with a branch north from its middle.
        var tiles = new HashSet<Vector2i>(Run(new Vector2i(-20, 0), new Vector2i(1, 0), 41));
        tiles.UnionWith(Run(new Vector2i(0, 1), new Vector2i(0, 1), 20));

        var lines = WFPlanetMapGeometry.Lines(tiles, WFPlanetMapLineKind.Road, null, 4000);
        Assert.That(lines, Has.Count.EqualTo(3), "A T junction should give three lines.");
        Assert.That(lines.All(line => line.Points.Length == 2), Is.True, "Straight lines should keep only their ends.");
        Assert.That(lines.All(line => line.Points.Contains(new Vector2i(0, 0))), Is.True, "Every line should meet at the junction.");

        // A tunnel through the middle of a straight road.
        var road = new HashSet<Vector2i>(Run(new Vector2i(0, 0), new Vector2i(1, 0), 60));
        var split = WFPlanetMapGeometry.Lines(road, WFPlanetMapLineKind.Road, tile => tile.X is >= 20 and < 40, 4000);
        Assert.That(split.Count(line => line.Kind == WFPlanetMapLineKind.Tunnel), Is.EqualTo(1));
        Assert.That(split.Count(line => line.Kind == WFPlanetMapLineKind.Road), Is.EqualTo(2));
    }

    [Test]
    public void ThickRoadsThinToOneLine()
    {
        // A road three tiles wide.
        var tiles = new HashSet<Vector2i>();
        for (var x = 0; x < 40; x++)
        for (var y = -1; y <= 1; y++)
            tiles.Add(new Vector2i(x, y));

        var lines = WFPlanetMapGeometry.Lines(tiles, WFPlanetMapLineKind.Road, null, 4000);
        Assert.That(lines, Has.Count.EqualTo(1), "A wide road should thin to one line.");
        Assert.That(lines[0].Points.Length, Is.LessThanOrEqualTo(4));
    }

    [Test]
    public void LinesStayWithinThePointBudget()
    {
        // A zigzag that simplifies poorly at the base tolerance.
        var tiles = new HashSet<Vector2i>();
        var at = Vector2i.Zero;
        for (var i = 0; i < 2000; i++)
        {
            tiles.Add(at);
            at += i % 8 < 4 ? new Vector2i(1, 0) : new Vector2i(0, 1);
        }

        var lines = WFPlanetMapGeometry.Lines(tiles, WFPlanetMapLineKind.Road, null, 50);
        Assert.That(lines.Sum(line => line.Points.Length), Is.InRange(2, 50));
        Assert.That(WFPlanetMapGeometry.Lines(tiles, WFPlanetMapLineKind.Road, null, 1), Is.Empty);
    }

    [Test]
    public void TerrainRunsRoundTrip()
    {
        var cells = new byte[300];
        for (var i = 0; i < cells.Length; i++)
            cells[i] = (byte) (i < 280 ? 2 : i % 3);

        var runs = WFPlanetMapEncoding.Encode(cells);
        Assert.That(runs.Length % 2, Is.Zero);
        Assert.That(runs.Length, Is.LessThan(cells.Length / 4), "A smooth terrain should compress.");
        Assert.That(WFPlanetMapEncoding.Decode(runs, cells.Length), Is.EqualTo(cells));

        // Bad input is cut to the expected size and never overruns it.
        Assert.That(WFPlanetMapEncoding.Decode(new byte[] { 1, 255, 1, 255 }, 10), Has.Length.EqualTo(10));
        Assert.That(WFPlanetMapEncoding.Decode(runs, int.MaxValue), Has.Length.EqualTo(WFPlanetMapEncoding.MaxCells));
    }

    [Test]
    public void TerrainMeshCoversOnlyItsGround()
    {
        // A 4x4 world of grass with one water cell in the middle.
        var cells = Enumerable.Repeat((byte) WFPlanetMapGround.Grass, 16).ToArray();
        cells[1 * 4 + 1] = (byte) WFPlanetMapGround.Water;
        var terrain = new WFPlanetMapTerrain
        {
            Origin = Vector2i.Zero,
            Cell = 8,
            Width = 4,
            Height = 4,
            Base = WFPlanetMapGround.Grass,
            Runs = WFPlanetMapEncoding.Encode(cells),
        };

        var patches = WFPlanetMapMesh.Terrain(terrain);
        Assert.That(patches.Select(patch => patch.Ground).Distinct(), Is.EqualTo(new[] { WFPlanetMapGround.Water }),
            "Only the water should get a patch; grass is the base colour.");
        var points = patches.SelectMany(patch => patch.Triangles).ToList();
        Assert.That(points.Count % 3, Is.Zero);
        // The water cell's centre is (12, 12); its patch reaches halfway to the neighbouring centres.
        Assert.That(points.All(point => point.X is >= 8f and <= 16f && point.Y is >= 8f and <= 16f), Is.True);
        Assert.That(points, Does.Contain(new Vector2(12f, 12f)));
    }

    /// <summary>A box seen through a turned view keeps its size and turns with the view, as a screen rectangle can't.</summary>
    [Test]
    public void BoxCornersTurnWithTheView()
    {
        var view = Matrix3x2.CreateRotation(0.175f) * Matrix3x2.CreateScale(2f);
        var corners = WFPlanetMapMesh.BoxCorners(Vector2.Zero, new Vector2(30f, 10f), view);

        Assert.That((corners[1] - corners[0]).Length(), Is.EqualTo(60f).Within(0.01f));
        Assert.That((corners[2] - corners[1]).Length(), Is.EqualTo(20f).Within(0.01f));
        Assert.That(corners[1].Y - corners[0].Y, Is.Not.EqualTo(0f).Within(1f), "The bottom edge stayed level on a turned view.");
    }
}
