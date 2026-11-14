#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._WF.PlanetPois;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.PlanetPois;

/// <summary>Footpaths to off-road sites: they reach the road around what's in the way, and give up when it's too far.</summary>
[TestFixture]
[TestOf(typeof(WFPlanetTrailPlanner))]
public sealed class PlanetTrailPlannerTest
{
    private static readonly HashSet<Vector2i> Road = Enumerable.Range(-20, 41).Select(y => new Vector2i(30, y)).ToHashSet();

    [Test]
    public void TheTrailGoesRoundAWallToTheRoad()
    {
        var wall = Enumerable.Range(-10, 21).Select(y => new Vector2i(15, y)).ToHashSet();
        for (var seed = 0; seed < 8; seed++)
        {
            var path = WFPlanetTrailPlanner.Route(Vector2i.Zero, Road, tile => !wall.Contains(tile), Vector2.Zero, 200, 160, seed);
            Assert.That(path, Is.Not.Null, $"Seed {seed} found no way to the road.");
            Assert.Multiple(() =>
            {
                Assert.That(path![0], Is.EqualTo(Vector2i.Zero));
                Assert.That(Road.Contains(path[^1]), Is.True, "The trail should end on the road.");
                Assert.That(path.Any(wall.Contains), Is.False, "The trail went through the wall.");
                Assert.That(path.Count(Road.Contains), Is.EqualTo(1), "The trail should stop at the first road tile.");
                for (var i = 1; i < path.Count; i++)
                    Assert.That(Math.Abs(path[i].X - path[i - 1].X) + Math.Abs(path[i].Y - path[i - 1].Y), Is.EqualTo(1), "The trail has a gap.");
            });
        }
    }

    [Test]
    public void ATrailTooLongIsNotLaid()
    {
        Assert.That(WFPlanetTrailPlanner.Route(Vector2i.Zero, Road, _ => true, Vector2.Zero, 200, 20, 1), Is.Null);
        Assert.That(WFPlanetTrailPlanner.Route(Vector2i.Zero, Road, _ => true, Vector2.Zero, 10, 160, 1), Is.Null, "The world's edge is in the way.");
    }
}
