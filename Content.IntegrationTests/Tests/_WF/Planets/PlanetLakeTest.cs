#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._WF.Planets;
using Content.Server.Parallax;
using Content.Shared._WF.Planets;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Planets;

/// <summary>Checks every world's standing water comes as lakes and ponds, never a sea; rivers may run as long as they like.</summary>
[TestFixture]
[TestOf(typeof(BiomeSystem))]
public sealed class PlanetLakeTest
{
    /// <summary>The widest a lake may be, in tiles, either way.</summary>
    private const int MaxLake = 48;

    [TestCase("WFSurfaceAsclepiu", true)]
    [TestCase("WFSurfaceMerak", true)]
    [TestCase("WFSurfaceAerumna", true)]
    [TestCase("WFSurfaceCarcinoma", true)]
    [TestCase("WFSurfaceFervidus", false)]
    [TestCase("WFSurfaceThrascias", true)]
    public async Task WaterFormsLakesNotSeas(string surfaceId, bool wet)
    {
        await using var pair = await PoolManager.GetServerClient();
        await PlanetFixture.EnableFeature(pair);
        var server = pair.Server;
        var entities = server.EntMan;
        var layers = new List<EntityUid>();
        await server.WaitAssertion(() =>
        {
            var surface = server.ResolveDependency<IPrototypeManager>().Index<WFPlanetSurfacePrototype>(surfaceId);
            var network = server.System<WFPlanetNetworkSystem>().BuildNetwork(surface, Vector2.Zero, surfaceId, null);
            Assert.That(network, Is.Not.Null);
            layers.AddRange(entities.GetComponent<WFPlanetNetworkComponent>(network!.Value).Layers);
            var biome = new Entity<BiomeComponent>(layers[0], entities.GetComponent<BiomeComponent>(layers[0]));
            var biomes = server.System<BiomeSystem>();
            var radius = surface.Radius;
            var water = new HashSet<Vector2i>();
            var rivers = new HashSet<Vector2i>();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var sampled = 0;
            for (var x = -radius; x < radius; x++)
            for (var y = -radius; y < radius; y++)
            {
                if (x * x + y * y > radius * radius)
                    continue;
                sampled++;
                var index = new Vector2i(x, y);
                if (!biomes.WfIsWater(biome, index))
                    continue;
                water.Add(index);
                if (biomes.TryGetBiomeTile(index, biome.Comp.Layers, biome.Comp.Seed, (Entity<MapGridComponent>?) null, out var tile)
                    && biomes.TryGetEntity(index, biome.Comp.Layers, tile.Value, biome.Comp.Seed, (Entity<MapGridComponent>?) null, out var feature)
                    && feature == "WFBloodRiver")
                    rivers.Add(index);
            }
            watch.Stop();

            var lakes = new List<(int Tiles, int Extent)>();
            var unvisited = new HashSet<Vector2i>(water);
            unvisited.ExceptWith(rivers);
            while (unvisited.Count > 0)
            {
                var start = unvisited.First();
                var pending = new Queue<Vector2i>();
                pending.Enqueue(start);
                unvisited.Remove(start);
                var tiles = 0;
                var min = start;
                var max = start;
                while (pending.TryDequeue(out var tile))
                {
                    tiles++;
                    min = Vector2i.ComponentMin(min, tile);
                    max = Vector2i.ComponentMax(max, tile);
                    foreach (var step in new[] { Vector2i.Up, Vector2i.Down, Vector2i.Left, Vector2i.Right })
                    {
                        if (unvisited.Remove(tile + step))
                            pending.Enqueue(tile + step);
                    }
                }
                lakes.Add((tiles, System.Math.Max(max.X - min.X, max.Y - min.Y) + 1));
            }

            var real = lakes.Where(lake => lake.Tiles >= 20).ToList();
            TestContext.Out.WriteLine($"{surfaceId}: {water.Count}/{sampled} water tiles ({100.0 * water.Count / sampled:F2}%, {rivers.Count} of them river), "
                + $"{lakes.Count} bodies, {real.Count} of 20+ tiles, widest {(lakes.Count == 0 ? 0 : lakes.Max(l => l.Extent))}, "
                + $"extents {string.Join(",", real.Select(l => l.Extent).OrderByDescending(e => e).Take(12))}; sampled in {watch.ElapsedMilliseconds} ms");

            if (!wet)
            {
                Assert.That(water, Is.Empty, $"{surfaceId} has no standing water.");
                return;
            }
            Assert.That(real, Has.Count.GreaterThanOrEqualTo(3), $"{surfaceId} lost its lakes.");
            Assert.That(lakes.Max(lake => lake.Extent), Is.LessThanOrEqualTo(MaxLake), $"{surfaceId} has a sea, not a lake.");
            Assert.That((double) water.Count / sampled, Is.LessThan(0.08), $"{surfaceId} is too much water.");
        });
        await PlanetFixture.Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }
}
