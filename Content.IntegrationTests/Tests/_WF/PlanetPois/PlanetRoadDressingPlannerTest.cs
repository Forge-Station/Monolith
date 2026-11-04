#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._WF.PlanetPois;
using Content.Shared._WF.PlanetPois;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.PlanetPois;

/// <summary>The layout rules of road dressing, on hand-drawn roads: which way a stretch runs, which kerb a shoulder shows, where lights stand.</summary>
[TestFixture]
[TestOf(typeof(WFRoadDressingPlanner))]
public sealed class PlanetRoadDressingPlannerTest
{
    /// <summary>A road north along x = 0 from y = 0 to 20, with a branch east along y = 10 to x = 12.</summary>
    private static HashSet<Vector2i> Tee()
    {
        var centre = new HashSet<Vector2i>();
        for (var y = 0; y <= 20; y++)
            centre.Add(new Vector2i(0, y));
        for (var x = 1; x <= 12; x++)
            centre.Add(new Vector2i(x, 10));
        return centre;
    }

    /// <summary>The carriageway and shoulder of a centreline, as the planner paints them.</summary>
    private static (HashSet<Vector2i> Road, HashSet<Vector2i> Shoulder) Paint(HashSet<Vector2i> centre, int half, int shoulderWidth)
    {
        var road = new HashSet<Vector2i>();
        var shoulder = new HashSet<Vector2i>();
        foreach (var tile in centre)
        {
            for (var x = -half - shoulderWidth; x <= half + shoulderWidth; x++)
            for (var y = -half - shoulderWidth; y <= half + shoulderWidth; y++)
            {
                (Math.Abs(x) <= half && Math.Abs(y) <= half ? road : shoulder).Add(tile + new Vector2i(x, y));
            }
        }

        shoulder.ExceptWith(road);
        return (road, shoulder);
    }

    [Test]
    public void StretchesBendsAndJunctionsAreToldApart()
    {
        var centre = Tee();
        Assert.Multiple(() =>
        {
            Assert.That(WFRoadDressingPlanner.AxisOf(centre, new Vector2i(0, 5)), Is.EqualTo(WFRoadAxis.NorthSouth));
            Assert.That(WFRoadDressingPlanner.AxisOf(centre, new Vector2i(6, 10)), Is.EqualTo(WFRoadAxis.EastWest));
            Assert.That(WFRoadDressingPlanner.AxisOf(centre, new Vector2i(0, 10)), Is.Null, "The junction is not a straight stretch.");
            Assert.That(WFRoadDressingPlanner.AxisOf(centre, new Vector2i(0, 0)), Is.Null, "An end is not a straight stretch.");
            Assert.That(WFRoadDressingPlanner.IsJunction(centre, new Vector2i(0, 10)), Is.True);
            Assert.That(WFRoadDressingPlanner.IsJunction(centre, new Vector2i(0, 5)), Is.False);
        });

        var open = WFRoadDressingPlanner.StraightRuns(centre, 3);
        Assert.Multiple(() =>
        {
            Assert.That(open.ContainsKey(new Vector2i(0, 5)), Is.True);
            Assert.That(open.ContainsKey(new Vector2i(0, 8)), Is.False, "Two tiles from the junction is too near.");
            Assert.That(open.ContainsKey(new Vector2i(0, 7)), Is.True, "Three tiles from the junction is far enough.");
            Assert.That(open.ContainsKey(new Vector2i(0, 2)), Is.False, "Two tiles from the end is too near.");
            Assert.That(open.ContainsKey(new Vector2i(11, 10)), Is.False);
            Assert.That(open[new Vector2i(6, 10)], Is.EqualTo(WFRoadAxis.EastWest));
        });
    }

    [Test]
    public void AShoulderShowsItsKerbToTheRoad()
    {
        var (road, shoulder) = Paint(Tee(), 1, 1);
        Assert.Multiple(() =>
        {
            Assert.That(WFRoadDressingPlanner.KerbOf(road, new Vector2i(-2, 5)), Is.EqualTo(WFRoadKerb.East), "West shoulder of the north road.");
            Assert.That(WFRoadDressingPlanner.KerbOf(road, new Vector2i(2, 5)), Is.EqualTo(WFRoadKerb.West), "East shoulder of the north road.");
            Assert.That(WFRoadDressingPlanner.KerbOf(road, new Vector2i(6, 12)), Is.EqualTo(WFRoadKerb.South), "North shoulder of the branch.");
            Assert.That(WFRoadDressingPlanner.KerbOf(road, new Vector2i(6, 8)), Is.EqualTo(WFRoadKerb.North), "South shoulder of the branch.");
            Assert.That(WFRoadDressingPlanner.KerbOf(road, new Vector2i(2, 12)), Is.EqualTo(WFRoadKerb.SouthWest), "The corner where the branch leaves has road on two sides.");
            Assert.That(WFRoadDressingPlanner.KerbOf(road, new Vector2i(-2, -2)), Is.EqualTo(WFRoadKerb.InnerNorthEast), "The corner round the road's end.");
            Assert.That(WFRoadDressingPlanner.KerbOf(road, new Vector2i(30, 30)), Is.Null);
            Assert.That(shoulder.Where(tile => !road.Contains(tile)).All(tile => WFRoadDressingPlanner.KerbOf(road, tile) != null), Is.True,
                "Every tile of a one-tile shoulder has a kerb.");
        });
    }

    [Test]
    public void LightsStandOnTheShoulderOnAlternatingSides()
    {
        var centre = new HashSet<Vector2i>();
        for (var y = -30; y <= 30; y++)
            centre.Add(new Vector2i(0, y));

        var (road, shoulder) = Paint(centre, 2, 1);
        var lights = WFRoadDressingPlanner.Lights(WFRoadDressingPlanner.StraightRuns(centre, 0), shoulder, 2, 10).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(lights, Has.Count.EqualTo(5), "One every ten tiles from y = -20 to y = 20.");
            Assert.That(lights.All(shoulder.Contains), Is.True);
            Assert.That(lights.Any(road.Contains), Is.False);
            Assert.That(lights, Does.Contain(new Vector2i(3, 0)));
            Assert.That(lights, Does.Contain(new Vector2i(-3, 10)));
            Assert.That(lights, Does.Contain(new Vector2i(-3, -10)), "The sides alternate below the origin too.");
            Assert.That(lights, Does.Contain(new Vector2i(3, 20)));
        });
    }

    [Test]
    public void AFootprintIsTheTilesAPieceCovers()
    {
        // A side-on car: one tile deep, reaching a tile east of where it stands.
        var car = WFRoadDressingPlanner.Footprint(new Box2(-0.5f, -0.5f, 1.4f, 0.5f), new Vector2i(4, 7)).ToHashSet();
        // A truck seen from above, pointing along the y axis.
        var truck = WFRoadDressingPlanner.Footprint(new Box2(-0.6f, -0.9f, 0.6f, 0.9f), new Vector2i(0, 0)).ToHashSet();
        // A street light.
        var light = WFRoadDressingPlanner.Footprint(new Box2(-0.1f, -0.1f, 0.1f, 0.1f), new Vector2i(2, 2)).ToHashSet();

        Assert.Multiple(() =>
        {
            Assert.That(car, Is.EquivalentTo(new[] { new Vector2i(4, 7), new Vector2i(5, 7) }));
            Assert.That(truck, Is.EquivalentTo(new[] { new Vector2i(0, -1), new Vector2i(0, 0), new Vector2i(0, 1) }));
            Assert.That(light, Is.EquivalentTo(new[] { new Vector2i(2, 2) }));
        });
    }
}
