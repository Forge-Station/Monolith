#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._WF.PlanetPois;
using Content.Shared._WF.PlanetPois;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.PlanetPois;

/// <summary>Checks connected routing, protected terrain and bounded deterministic failures.</summary>
[TestFixture]
[TestOf(typeof(WFPlanetRoadPlanner))]
public sealed class PlanetRoadPlannerTest
{
    private static WFPlanetRoadSite Site(int x, int y, WFPlanetPoiConnection mode = WFPlanetPoiConnection.Roadside)
    {
        var sockets = new List<WFPlanetRoadSocket> { new(new Vector2i(x + 2, y), WFPlanetPoiDirection.East) };
        if (mode == WFPlanetPoiConnection.Through)
            sockets.Add(new(new Vector2i(x - 2, y), WFPlanetPoiDirection.West));
        return new(new Box2i(x - 2, y - 2, x + 3, y + 3), mode, sockets);
    }

    private static WFPlanetRoadPlan Plan(IReadOnlyList<WFPlanetRoadSite> sites, HashSet<Vector2i>? blocked = null, int seed = 17) =>
        WFPlanetRoadPlanner.Plan(Vector2.Zero, 48, 5, sites, blocked ?? new(), 3, 1, seed);

    [Test]
    public void BranchesAndThroughEntrancesFormOnePhysicalNetwork()
    {
        var sites = new[] { Site(-20, -15), Site(17, -12, WFPlanetPoiConnection.Through), Site(-8, 22), Site(22, 22, WFPlanetPoiConnection.Isolated) };
        var plan = Plan(sites);
        Assert.That(plan.Success, Is.True, plan.Error);
        var reached = new HashSet<Vector2i>();
        var frontier = new Queue<Vector2i>();
        frontier.Enqueue(plan.RoadTiles.First());
        while (frontier.TryDequeue(out var current))
        {
            if (!reached.Add(current))
                continue;
            foreach (var delta in new[] { new Vector2i(1, 0), new Vector2i(-1, 0), new Vector2i(0, 1), new Vector2i(0, -1) })
                if (plan.RoadTiles.Contains(current + delta) && !reached.Contains(current + delta))
                    frontier.Enqueue(current + delta);
        }
        Assert.That(reached.Count, Is.EqualTo(plan.RoadTiles.Count));
        foreach (var site in sites.Where(s => s.Connection != WFPlanetPoiConnection.Isolated))
        foreach (var socket in site.Sockets)
            Assert.That(reached, Does.Contain(socket.Position));
        Assert.That(plan.RoadTiles, Does.Not.Contain(sites[3].Sockets[0].Position));
        foreach (var site in sites)
        for (var x = site.Bounds.Left + 1; x < site.Bounds.Right - 1; x++)
        for (var y = site.Bounds.Bottom + 1; y < site.Bounds.Top - 1; y++)
        {
            Assert.That(plan.RoadTiles, Does.Not.Contain(new Vector2i(x, y)));
            Assert.That(plan.ShoulderTiles, Does.Not.Contain(new Vector2i(x, y)));
        }
    }

    [Test]
    public void DetoursProtectPinnedTilesCircleAndCentre()
    {
        var blocked = new HashSet<Vector2i>();
        for (var y = -35; y <= 10; y++)
            blocked.Add(new Vector2i(0, y));
        var plan = Plan(new[] { Site(-20, -15), Site(20, -15) }, blocked);
        Assert.That(plan.Success, Is.True, plan.Error);
        foreach (var tile in plan.RoadTiles.Concat(plan.ShoulderTiles))
        {
            Assert.That(blocked, Does.Not.Contain(tile));
            var distance = new Vector2(tile.X + .5f, tile.Y + .5f).Length();
            Assert.That(distance + .707107f, Is.LessThanOrEqualTo(48));
            Assert.That(distance - .707107f, Is.GreaterThanOrEqualTo(5));
        }
        Assert.That(plan.RoadTiles.Any(t => t.X == 0 && (t.Y > 10 || t.Y < -35)), Is.True);
    }

    [Test]
    public void SeededPlanIsRepeatable()
    {
        var sites = new[] { Site(-20, -15), Site(20, 15), Site(-15, 20) };
        var first = Plan(sites);
        var second = Plan(sites);
        Assert.That(first.Success, Is.True, first.Error);
        Assert.That(second.RoadTiles.SetEquals(first.RoadTiles), Is.True);
        Assert.That(second.ShoulderTiles.SetEquals(first.ShoulderTiles), Is.True);
    }

    [Test]
    public void ImpossibleRouteReturnsNoPartialNetwork()
    {
        var blocked = new HashSet<Vector2i>();
        for (var y = -48; y <= 48; y++)
            blocked.Add(new Vector2i(0, y));
        var plan = Plan(new[] { Site(-20, -15), Site(20, -15) }, blocked);
        Assert.That(plan.Success, Is.False);
        Assert.That(plan.Error, Is.Not.Empty);
        Assert.That(plan.RoadTiles, Is.Empty);
        Assert.That(plan.ShoulderTiles, Is.Empty);
    }

    [Test]
    public void ThroughRequiresDifferentEntranceSides()
    {
        var site = Site(20, 10, WFPlanetPoiConnection.Through);
        var invalid = site with { Sockets = new[] { site.Sockets[0], site.Sockets[0] with { Position = new Vector2i(22, 11) } } };
        Assert.That(Plan(new[] { invalid }).Success, Is.False);
    }
    [Test]
    public void EntranceAtWorldEdgeFailsWithFullWidthClearance()
    {
        Assert.That(Plan(new[] { Site(44, 0) }).Success, Is.False);
    }

    [Test]
    public void PinnedEntranceCannotBeOverwritten()
    {
        var site = Site(20, 10);
        var plan = Plan(new[] { site }, new HashSet<Vector2i> { site.Sockets[0].Position });
        Assert.That(plan.Success, Is.False);
        Assert.That(plan.FailedEntrance, Is.EqualTo(site.Sockets[0].Position), "The failure should name the entrance, so the world can route without its site.");
    }
    [Test]
    public void SocketApproachCannotExcuseAnotherBuilding()
    {
        var first = Site(20, 10);
        var neighbor = new WFPlanetRoadSite(new Box2i(23, 8, 28, 13), WFPlanetPoiConnection.Isolated, new List<WFPlanetRoadSocket>());
        Assert.That(Plan(new[] { first, neighbor }).Success, Is.False);
    }

    [Test]
    public void SocketMustBeOnItsOwnBoundary()
    {
        var site = Site(20, 10);
        var invalid = site with { Sockets = new[] { new WFPlanetRoadSocket(new Vector2i(30, 10), WFPlanetPoiDirection.East) } };
        Assert.That(Plan(new[] { invalid }).Success, Is.False);
    }

    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    public void NonFiniteWorldIsRejected(float radius)
    {
        var plan = WFPlanetRoadPlanner.Plan(Vector2.Zero, radius, 0, new[] { Site(20, 10) }, new HashSet<Vector2i>(), 3, 0, 1);
        Assert.That(plan.Success, Is.False);
    }

    [Test]
    public void EvenWidthIsRejected()
    {
        var plan = WFPlanetRoadPlanner.Plan(Vector2.Zero, 48, 0, new[] { Site(20, 10) }, new HashSet<Vector2i>(), 2, 0, 1);
        Assert.That(plan.Success, Is.False);
    }
    [Test]
    public void TranslatingWorldAndSitesTranslatesTheNetwork()
    {
        var sites = new[] { Site(-20, -15), Site(20, 15), Site(-15, 20) };
        var first = Plan(sites);
        var offset = new Vector2i(93, -71);
        var translated = sites.Select(s => s with
        {
            Bounds = new Box2i(s.Bounds.Left + offset.X, s.Bounds.Bottom + offset.Y, s.Bounds.Right + offset.X, s.Bounds.Top + offset.Y),
            Sockets = s.Sockets.Select(socket => socket with { Position = socket.Position + offset }).ToArray(),
        }).ToArray();
        var second = WFPlanetRoadPlanner.Plan(new Vector2(offset.X, offset.Y), 48, 5, translated, new HashSet<Vector2i>(), 3, 1, 17);
        Assert.That(first.Success, Is.True, first.Error);
        Assert.That(second.Success, Is.True, second.Error);
        Assert.That(second.RoadTiles.SetEquals(first.RoadTiles.Select(tile => tile + offset)), Is.True);
        Assert.That(second.ShoulderTiles.SetEquals(first.ShoulderTiles.Select(tile => tile + offset)), Is.True);
    }

    /// <summary>Sites scattered round a production-sized world, between the landing zone and the edge margin.</summary>
    private static WFPlanetRoadSite[] Scattered(int count, float inner, float outer, int seed)
    {
        var random = new System.Random(seed);
        var sites = new List<WFPlanetRoadSite>();
        for (var i = 0; i < count; i++)
        {
            var angle = i * System.MathF.Tau / count + (float) random.NextDouble() * 0.4f;
            var distance = inner + (float) random.NextDouble() * (outer - inner);
            var site = Site((int) (System.MathF.Cos(angle) * distance), (int) (System.MathF.Sin(angle) * distance),
                i % 3 == 0 ? WFPlanetPoiConnection.Through : WFPlanetPoiConnection.Roadside);
            sites.Add(site);
        }
        return sites.ToArray();
    }

    /// <summary>The road tiles left after shedding every dead end, tile by tile: what lies on a loop.</summary>
    private static HashSet<Vector2i> Loops(HashSet<Vector2i> centreline)
    {
        var core = new HashSet<Vector2i>(centreline);
        var steps = new[] { new Vector2i(1, 0), new Vector2i(-1, 0), new Vector2i(0, 1), new Vector2i(0, -1) };
        bool changed;
        do
        {
            changed = core.RemoveWhere(tile => steps.Count(step => core.Contains(tile + step)) <= 1) > 0;
        } while (changed);
        return core;
    }

    /// <summary>Whether the roads close round the landing zone: open ground from the centre can't get out past them.</summary>
    private static bool Encircled(HashSet<Vector2i> roads, int radius)
    {
        var seen = new HashSet<Vector2i>();
        var frontier = new Queue<Vector2i>();
        frontier.Enqueue(Vector2i.Zero);
        var steps = new[] { new Vector2i(1, 0), new Vector2i(-1, 0), new Vector2i(0, 1), new Vector2i(0, -1) };
        while (frontier.TryDequeue(out var tile))
        {
            if (roads.Contains(tile) || !seen.Add(tile))
                continue;
            if (tile.X * tile.X + tile.Y * tile.Y >= radius * radius)
                return false;
            foreach (var step in steps)
                frontier.Enqueue(tile + step);
        }
        return true;
    }

    /// <summary>
    /// A production-sized world gets one connected road net: out of the landing zone, to every entrance and on toward
    /// the edge, branching like a tree with never a loop round the landing zone, mostly straight runs, planned in good
    /// time.
    /// </summary>
    [TestCase(383, 96, 1977)]
    [TestCase(383, 96, 8231)]
    [TestCase(319, 96, 44119)]
    [TestCase(191, 64, 1977)]
    public void ProductionWorldGetsAConnectedRoadNetwork(int radius, int clear, int seed)
    {
        var sites = Scattered(10, clear + 30, radius - 96, seed);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var plan = WFPlanetRoadPlanner.Plan(Vector2.Zero, radius, clear, sites, new HashSet<Vector2i>(), 5, 1, seed);
        watch.Stop();
        Assert.That(plan.Success, Is.True, plan.Error);
        var straight = plan.Centreline.Count(tile => WFRoadDressingPlanner.AxisOf(plan.Centreline, tile) != null);
        var loops = Loops(plan.Centreline);
        var reach = plan.Centreline.Max(tile => new Vector2(tile.X, tile.Y).Length());
        TestContext.Out.WriteLine($"radius {radius}, seed {seed}: planned in {watch.ElapsedMilliseconds} ms; {plan.Centreline.Count} centreline tiles, "
            + $"{plan.Backbone.Count} on gates, exits and links, {100.0 * straight / plan.Centreline.Count:F1}% straight, {loops.Count} on loops, reaching {reach:F0}");

        Assert.That(plan.Backbone, Is.Not.Empty, "A world this size should have roads out of the landing zone and toward the edge.");
        Assert.That(plan.Centreline.Any(tile => new Vector2(tile.X + .5f, tile.Y + .5f).Length() < clear + 12), Is.True,
            "No road comes out of the landing zone.");
        Assert.That(WFPlanetRoadPlanner.Components(plan.Centreline), Has.Count.EqualTo(1), "The centreline is in pieces.");
        Assert.That(WFPlanetRoadPlanner.Components(plan.RoadTiles), Has.Count.EqualTo(1), "The paved roads are in pieces.");
        foreach (var site in sites)
        foreach (var socket in site.Sockets.Take(site.Connection == WFPlanetPoiConnection.Through ? 2 : 1))
            Assert.That(plan.RoadTiles, Does.Contain(socket.Position), "An entrance was left off the roads.");
        Assert.That(Encircled(plan.RoadTiles, radius), Is.False, "The roads make a ring round the landing zone.");
        Assert.That((double) straight / plan.Centreline.Count, Is.GreaterThan(0.3), "The roads wander instead of running straight.");
        Assert.That(reach, Is.GreaterThan(radius * 0.5f), "The roads stop short of the outer world.");
        Assert.That(watch.ElapsedMilliseconds, Is.LessThan(4000), "Planning the roads takes too long for the preload.");
    }

    /// <summary>A world too small for gates and exits is joined site to site.</summary>
    [Test]
    public void SmallWorldHasNoGatesOrExits()
    {
        var plan = Plan(new[] { Site(-20, -15), Site(20, 15) });
        Assert.That(plan.Success, Is.True, plan.Error);
        Assert.That(plan.Backbone, Is.Empty);
    }

    /// <summary>With no gates, exits or links, a big world's roads only join its sites.</summary>
    [Test]
    public void ShapeTurnsOffGatesExitsAndLinks()
    {
        var sites = Scattered(6, 130, 280, 3);
        var plan = WFPlanetRoadPlanner.Plan(Vector2.Zero, 383, 96, sites, new HashSet<Vector2i>(), 5, 1, 3, null, WFPlanetRoadShape.SitesOnly);
        Assert.That(plan.Success, Is.True, plan.Error);
        Assert.That(plan.Backbone, Is.Empty);
        Assert.That(WFPlanetRoadPlanner.Components(plan.RoadTiles), Has.Count.EqualTo(1));
    }

    /// <summary>An off-road site far from the others gets a road out to within reach of its door, for its footpath.</summary>
    [Test]
    public void OffRoadSiteGetsARoadWithinReach()
    {
        var house = new WFPlanetRoadSite(new Box2i(-215, 40, -198, 53), WFPlanetPoiConnection.Isolated, new List<WFPlanetRoadSocket>());
        var door = new Vector2i(-198, 45);
        var sites = new[] { Site(200, 0), house };
        var near = WFPlanetRoadPlanner.Plan(Vector2.Zero, 319, 96, sites, new HashSet<Vector2i>(), 5, 1, 3, null, null, new[] { door });
        var alone = WFPlanetRoadPlanner.Plan(Vector2.Zero, 319, 96, sites, new HashSet<Vector2i>(), 5, 1, 3, null, new WFPlanetRoadShape(Exits: 0, Reach: 0), new[] { door });
        Assert.That(near.Success, Is.True, near.Error);
        Assert.That(alone.Success, Is.True, alone.Error);
        float Nearest(WFPlanetRoadPlan plan) => plan.Centreline.Min(tile => new Vector2(tile.X - door.X, tile.Y - door.Y).Length());
        Assert.That(Nearest(near), Is.LessThanOrEqualTo(64), "No road came out to the house.");
        Assert.That(Nearest(alone), Is.GreaterThan(64), "A road came out to the house with no reach.");
        Assert.That(WFPlanetRoadPlanner.Components(near.RoadTiles), Has.Count.EqualTo(1));
        Assert.That(near.RoadTiles.Concat(near.ShoulderTiles).Any(tile => house.Bounds.Contains(tile)), Is.False, "The road runs into the house.");
    }

    /// <summary>A lake beside the landing zone and another on a site's way in: the roads go round both, still in one piece.</summary>
    [Test]
    public void LakesAreRoutedRound()
    {
        var lakes = new[] { (Centre: new Vector2(116, 4), Radius: 22f), (Centre: new Vector2(-180, 40), Radius: 30f) };
        bool Water(Vector2i tile) => lakes.Any(lake => Vector2.Distance(new Vector2(tile.X + .5f, tile.Y + .5f), lake.Centre) <= lake.Radius);
        var sites = new[] { Site(-230, 40), Site(200, -150), Site(40, 220, WFPlanetPoiConnection.Through), Site(-120, -200) };
        var plan = WFPlanetRoadPlanner.Plan(Vector2.Zero, 383, 96, sites, new HashSet<Vector2i>(), 5, 1, 17, Water);
        Assert.That(plan.Success, Is.True, plan.Error);
        Assert.That(plan.RoadTiles.Concat(plan.ShoulderTiles).Where(Water), Is.Empty, "A road runs through a lake.");
        Assert.That(WFPlanetRoadPlanner.Components(plan.RoadTiles), Has.Count.EqualTo(1));
        foreach (var site in sites)
            Assert.That(plan.RoadTiles, Does.Contain(site.Sockets[0].Position));
    }

    /// <summary>A site wholly ringed by water can't be joined: the failure names its entrance, so it goes off the roads.</summary>
    [Test]
    public void SiteInALakeFailsAlone()
    {
        bool Water(Vector2i tile)
        {
            var distance = Vector2.Distance(new Vector2(tile.X + .5f, tile.Y + .5f), new Vector2(-200, 0));
            return distance is > 14 and < 30;
        }
        var island = Site(-200, 0);
        var plan = WFPlanetRoadPlanner.Plan(Vector2.Zero, 383, 96, new[] { Site(200, 30), island }, new HashSet<Vector2i>(), 5, 1, 17, Water);
        Assert.That(plan.Success, Is.False);
        Assert.That(plan.FailedEntrance, Is.EqualTo(island.Sockets[0].Position));
    }

    /// <summary>Pinned ground, such as a cavern mouth, in the way of a road out of the landing zone is never paved.</summary>
    [Test]
    public void RoadsKeepOffPinnedGround()
    {
        var blocked = new HashSet<Vector2i>();
        for (var x = -40; x <= 40; x++)
        for (var y = 90; y <= 300; y++)
        {
            if (x * x + (y - 200) * (y - 200) <= 30 * 30 || System.Math.Abs(x) <= 6 && y > 100)
                blocked.Add(new Vector2i(x, y));
        }
        var plan = WFPlanetRoadPlanner.Plan(Vector2.Zero, 383, 96, new[] { Site(150, 150), Site(-150, -150) }, blocked, 5, 1, 5);
        Assert.That(plan.Success, Is.True, plan.Error);
        Assert.That(plan.RoadTiles.Concat(plan.ShoulderTiles).Where(blocked.Contains), Is.Empty);
        Assert.That(WFPlanetRoadPlanner.Components(plan.RoadTiles), Has.Count.EqualTo(1));
    }
}
