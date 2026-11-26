#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._WF.Planets;
using Content.Shared._WF.Planets;
using Content.Shared.Maps;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Planets.Biomes;

/// <summary>Verda's ground: weather tiles only, and every region (shores, meadow, woods, scars, snow) turns up across the world.</summary>
[TestFixture]
public sealed class VerdaBiomeTest
{
    private const string Surface = "WFSurfaceAsclepiu";
    private const string Water = "MonoFloorWaterEntity";

    /// <summary>Every tile the world's biome may lay.</summary>
    private static readonly HashSet<string> Allowed = new()
    {
        "FloorPlanetGrass", "FloorPlanetDirt", "FloorSnow", "FloorRiverbed",
        "WFFloorVerdaMeadowGrass", "WFFloorVerdaForestGrass", "WFFloorVerdaPondMud",
        "WFFloorVerdaMarshGrass", "WFFloorVerdaMarshMud", "WFFloorVerdaShoreSand",
        "WFFloorVerdaScarDirt", "WFFloorVerdaScarDirtPatch", "WFFloorVerdaScarWiltedGrass", "WFFloorVerdaScarAshDirt",
        "WFFloorVerdaSnowRough", "WFFloorVerdaWaterJelly",
    };

    private static readonly HashSet<string> Shore = new() { "WFFloorVerdaMarshGrass", "WFFloorVerdaMarshMud", "WFFloorVerdaShoreSand" };

    private static readonly HashSet<string> Scar = new()
    {
        "WFFloorVerdaScarDirt", "WFFloorVerdaScarDirtPatch", "WFFloorVerdaScarWiltedGrass", "WFFloorVerdaScarAshDirt",
    };

    private static readonly HashSet<string> Reeds = new() { "AuroraFloraBushKonyangReeds", "AuroraAssunzioneGrassAlt" };
    private static readonly HashSet<string> Lilies = new() { "VoreFloraLily1", "VoreFloraLily2", "VoreFloraLily3", "GoonFakeobjectLotus" };
    private static readonly HashSet<string> Pines = new() { "CoyoteFloraTreeMedPine", "DarkPackTreeVampPine", "CoyoteFloraTreeCypressTree" };
    private static readonly HashSet<string> SnowRocks = new() { "GoonStoneSnow", "GoonStoneSnow2", "GoonStoneSnow3", "GoonStoneSnow4", "AuroraFloraRockSnow" };

    [Test]
    public async Task VerdaRegionsAllTurnUp()
    {
        await using var pair = await PoolManager.GetServerClient();
        await PlanetFixture.EnableFeature(pair);
        var server = pair.Server;
        var entities = server.EntMan;
        var layers = new List<EntityUid>();
        await server.WaitAssertion(() =>
        {
            var protos = server.ResolveDependency<IPrototypeManager>();
            var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
            var surface = protos.Index<WFPlanetSurfacePrototype>(Surface);
            var network = server.System<WFPlanetNetworkSystem>().BuildNetwork(surface, Vector2.Zero, Surface, null);
            Assert.That(network, Is.Not.Null);
            layers.AddRange(entities.GetComponent<WFPlanetNetworkComponent>(network!.Value).Layers);
            var biome = entities.GetComponent<BiomeComponent>(layers[0]);
            var biomes = server.System<SharedBiomeSystem>();

            var tileCount = new Dictionary<string, int>();
            var entityCount = new Dictionary<string, int>();
            var tileAt = new Dictionary<Vector2i, string>();
            var waterAt = new HashSet<Vector2i>();
            var sampled = 0;
            var open = 0;
            var radius = surface.Radius;
            const int step = 2;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            for (var x = -radius; x < radius; x += step)
            for (var y = -radius; y < radius; y += step)
            {
                if (x * x + y * y > radius * radius)
                    continue;
                var index = new Vector2i(x, y);
                Assert.That(biomes.TryGetBiomeTile(index, biome.Layers, biome.Seed, (Entity<MapGridComponent>?) null, out var tile), Is.True);
                var def = (ContentTileDefinition) tileDefs[tile!.Value.TypeId];
                tileAt[index] = def.ID;
                tileCount[def.ID] = tileCount.GetValueOrDefault(def.ID) + 1;
                sampled++;
                if (biomes.TryGetEntity(index, biome.Layers, tile.Value, biome.Seed, (Entity<MapGridComponent>?) null, out var entity))
                {
                    entityCount[entity] = entityCount.GetValueOrDefault(entity) + 1;
                    if (entity == Water)
                        waterAt.Add(index);
                }
                else
                {
                    open++;
                }
            }
            watch.Stop();

            double Share(IEnumerable<string> ids) => ids.Sum(id => tileCount.GetValueOrDefault(id)) / (double) sampled;
            int Count(IEnumerable<string> ids) => ids.Sum(id => entityCount.GetValueOrDefault(id));
            bool IsDead(string id) => id.Contains("Dead") || id.Contains("Burned") || id.Contains("Ashtree") || id.Contains("Barren");

            // The ring round the water: what lies two tiles out from each lake and pond tile (a lake is over riverbed).
            var lakeRing = new Dictionary<string, int>();
            var pondRing = new Dictionary<string, int>();
            foreach (var water in waterAt)
            {
                var ring = tileAt[water] == "FloorRiverbed" ? lakeRing : pondRing;
                foreach (var offset in new[] { new Vector2i(step, 0), new Vector2i(-step, 0), new Vector2i(0, step), new Vector2i(0, -step) })
                {
                    if (waterAt.Contains(water + offset) || !tileAt.TryGetValue(water + offset, out var next))
                        continue;
                    ring[next] = ring.GetValueOrDefault(next) + 1;
                }
            }
            // A lake the snow has reached is the snow's business.
            var lakeTotal = lakeRing.Where(kv => kv.Key is not ("FloorSnow" or "WFFloorVerdaSnowRough")).Sum(kv => kv.Value);
            var lakeShore = lakeRing.Where(kv => Shore.Contains(kv.Key)).Sum(kv => kv.Value);
            var pondTotal = pondRing.Where(kv => kv.Key is not ("FloorSnow" or "WFFloorVerdaSnowRough")).Sum(kv => kv.Value);
            var pondMud = pondRing.GetValueOrDefault("WFFloorVerdaPondMud");

            var flowerDecals = 0;
            var snowDecals = 0;
            for (var x = -radius; x < radius; x += 4)
            for (var y = -radius; y < radius; y += 4)
            {
                if (x * x + y * y > radius * radius)
                    continue;
                if (!biomes.TryGetDecals(new Vector2i(x, y), biome.Layers, biome.Seed, (Entity<MapGridComponent>?) null, out var decals))
                    continue;
                foreach (var (id, _) in decals)
                {
                    if (id == "AuroraDecalFloorDecalKonyangFlowers")
                        flowerDecals++;
                    else if (id is "AuroraDecalFloorDecalSnowdrift" or "AuroraDecalFloorDecalSnowrocks")
                        snowDecals++;
                }
            }

            TestContext.Out.WriteLine($"Verda: {sampled} samples in {watch.ElapsedMilliseconds} ms, open {100.0 * open / sampled:F1}%");
            foreach (var (id, n) in tileCount.OrderByDescending(kv => kv.Value))
                TestContext.Out.WriteLine($"  tile {id}: {100.0 * n / sampled:F2}%");
            foreach (var (id, n) in entityCount.OrderByDescending(kv => kv.Value).Take(80))
                TestContext.Out.WriteLine($"  entity {id}: {100.0 * n / sampled:F2}%");
            TestContext.Out.WriteLine($"  water {waterAt.Count}; lake edge {lakeTotal}, shore {lakeShore}: "
                + string.Join(", ", lakeRing.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value}")));
            TestContext.Out.WriteLine($"  pond edge {pondTotal}, mud {pondMud}: "
                + string.Join(", ", pondRing.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value}")));
            TestContext.Out.WriteLine($"  decals: flowers {flowerDecals}, snow {snowDecals}");

            using (Assert.EnterMultipleScope())
            {
                foreach (var id in tileCount.Keys)
                {
                    Assert.That(Allowed, Does.Contain(id), $"Verda lays {id}, which is not one of its tiles.");
                    Assert.That(((ContentTileDefinition) tileDefs[id]).Weather, Is.True, $"{id} would not take the rain.");
                }

                Assert.That(Share(new[] { "WFFloorVerdaMeadowGrass" }), Is.GreaterThan(0.02), "No meadows.");
                Assert.That(Share(new[] { "WFFloorVerdaForestGrass" }), Is.GreaterThan(0.02), "No forest floor.");
                Assert.That(Share(Shore), Is.GreaterThan(0.002), "No lake shores.");
                Assert.That(Share(new[] { "WFFloorVerdaShoreSand" }), Is.GreaterThan(0.0003), "No sand on the shores.");
                Assert.That(Share(new[] { "WFFloorVerdaPondMud" }), Is.GreaterThan(0.0003), "No mud round the ponds.");
                Assert.That(Share(Scar), Is.InRange(0.01, 0.08), "The irradiated scars are not a few per cent of the surface.");
                Assert.That(Share(new[] { "WFFloorVerdaSnowRough" }), Is.GreaterThan(0.005), "No rough snow in the highlands.");
                Assert.That(lakeTotal, Is.GreaterThan(0), "No lake was sampled.");
                Assert.That((double) lakeShore / lakeTotal, Is.GreaterThan(0.9), "Lakes do not sit in a ring of shore.");
                Assert.That(pondTotal, Is.GreaterThan(0), "No pond was sampled.");
                Assert.That((double) pondMud / pondTotal, Is.GreaterThan(0.5), "Ponds do not sit in a ring of mud.");
                Assert.That(flowerDecals, Is.GreaterThan(0), "No flowers on the meadows.");
                Assert.That(snowDecals, Is.GreaterThan(0), "No drifts on the highlands.");

                Assert.That(Count(Reeds), Is.GreaterThan(0), "No reeds.");
                Assert.That(Count(Lilies), Is.GreaterThan(0), "No lilies.");
                Assert.That(Count(Pines), Is.GreaterThan(0), "No pines.");
                Assert.That(Count(entityCount.Keys.Where(IsDead)), Is.GreaterThan(0), "No dead trees.");
                Assert.That(Count(entityCount.Keys.Where(id => id.StartsWith("CoyoteWildPlantFlower"))), Is.GreaterThan(0), "No wildflowers.");
                Assert.That(Count(SnowRocks), Is.GreaterThan(0), "No snow-capped rocks.");
                Assert.That(Count(new[] { "GoonShrubSnow", "GoonShrubSnow2" }), Is.GreaterThan(0), "No snow shrubs.");
                Assert.That(Count(entityCount.Keys.Where(id => id.StartsWith("NebulaTreeHardwood"))), Is.GreaterThan(0), "No hardwoods.");
                Assert.That((double) open / sampled, Is.GreaterThan(0.6), "Most of the surface should be open.");
            }
        });
        await PlanetFixture.Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }
}
