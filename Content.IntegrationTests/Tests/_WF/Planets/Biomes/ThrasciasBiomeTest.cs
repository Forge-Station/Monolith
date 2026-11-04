#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._WF.Planets;
using Content.Shared._WF.Planets;
using Content.Shared.Maps;
using Content.Shared.Parallax.Biomes;
using Content.Shared.Parallax.Biomes.Layers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Noise;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Prototypes;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.Planets.Biomes;

/// <summary>Samples the whole Thrascias surface: every tile takes weather, every region occurs, and its scenery stays on its own ground.</summary>
[TestFixture]
public sealed class ThrasciasBiomeTest
{
    private static readonly string[] SnowTiles =
    {
        "FloorSnow", "WFFloorThrasciasSnowRough", "WFFloorThrasciasSnowCalm", "WFFloorThrasciasSnowDug",
    };

    private static readonly string[] IceTiles =
    {
        "WFFloorThrasciasPackIce", "WFFloorThrasciasRoughIce", "WFFloorThrasciasClearIce",
        "WFFloorThrasciasClearIceSmooth", "WFFloorThrasciasLakeIce",
    };

    private const string Dirt = "WFFloorThrasciasFrozenDirt";
    private const string Crystal = "WFFloorThrasciasCrystalIce";
    private const string Lake = "WFFloorThrasciasLakeIce";
    private const string LakeWater = "WFThrasciasLakeWater";

    private static readonly HashSet<string> Pines = new()
    {
        "ShiptestFloraTreePine", "CoyoteFloraTreeMedPine", "DarkPackTreeVampPine", "VoreFloraTreeSif",
    };

    private static readonly HashSet<string> IceRocks = new()
    {
        "ShiptestFloraRockIcy", "ShiptestRockPileIcy", "AuroraFloraRockIce",
    };

    private static readonly HashSet<string> CrystalGrowth = new()
    {
        "ShiptestFloraRockCrystal", "VorePropRockCrystal", "VorePropRockCrystalDust", "AuroraFloraTreeCrystal",
        "BayCrystal", "ChompQuartz",
    };

    private static readonly HashSet<string> Tundra = new()
    {
        "WFFloraFrostbelle", "GoonShrubSnow", "GoonShrubSnow2",
        "GoonStoneSnow", "GoonStoneSnow2", "GoonStoneSnow3", "GoonStoneSnow4", "AuroraFloraRockSnow",
    };

    private const string Fungus = "WFFloraFrozenFungus";
    private const string IceCrystal = "WFFloraIceCrystal";
    private const string Whipvine = "WFFloraWhipvine";

    private static readonly string[] CrystalPlants = { "WFFloraCrystalBloom", "WFFloraFrostPod" };

    private static readonly string[] Spawners =
    {
        "WFFaunaThrasciasTundra", "WFFaunaThrasciasIce", "WFFaunaThrasciasCrystal", "WFFaunaThrasciasBoreal",
    };

    [Test]
    public async Task ThrasciasRegionsAllOccurOnTheirOwnGround()
    {
        await using var pair = await PoolManager.GetServerClient();
        await EnableFeature(pair);
        var server = pair.Server;
        var entities = server.EntMan;
        var layers = new List<EntityUid>();
        await server.WaitAssertion(() =>
        {
            var surface = server.ResolveDependency<IPrototypeManager>().Index<WFPlanetSurfacePrototype>("WFSurfaceThrascias");
            var network = server.System<WFPlanetNetworkSystem>().BuildNetwork(surface, Vector2.Zero, "Thrascias", null);
            Assert.That(network, Is.Not.Null);
            layers.AddRange(entities.GetComponent<WFPlanetNetworkComponent>(network!.Value).Layers);
            var biome = entities.GetComponent<BiomeComponent>(layers[0]);
            var generator = server.System<SharedBiomeSystem>();
            var tiles = server.ResolveDependency<ITileDefinitionManager>();

            var allowed = new HashSet<string>(SnowTiles.Concat(IceTiles)) { Dirt, Crystal, "WFFloorThrasciasRimeIce" };
            var tileCount = new Dictionary<string, int>();
            var featureCount = new Dictionary<string, int>();
            var decalCount = new Dictionary<string, int>();
            var plasmaOffCrystal = 0;
            var misplaced = new List<string>();
            var sampled = 0;
            var pocketSnow = 0;
            var pocketWhipvines = 0;
            var pocketSpawners = 0;
            var fungusAt = new List<Vector2i>();
            var terrain = server.ResolveDependency<IPrototypeManager>().Index<BiomeTemplatePrototype>("WFThrasciasTerrain");
            var pocket = terrain.Layers.OfType<BiomeMetaLayer>().Single(layer => layer.Template == "WFThrasciasBorealPocket");
            var pocketNoise = new FastNoiseLite();
            server.ResolveDependency<ISerializationManager>().CopyTo(pocket.Noise, ref pocketNoise, notNullableOverride: true);
            pocketNoise.SetSeed(pocketNoise.GetSeed() + biome.Seed);
            pocketNoise.SetFractalOctaves(pocketNoise.GetFractalOctaves());
            var radius = surface.Radius;
            for (var x = -radius; x < radius; x++)
            for (var y = -radius; y < radius; y++)
            {
                if (x * x + y * y > radius * radius)
                    continue;
                var pos = new Vector2i(x, y);
                generator.TryGetBiomeTile(pos, biome.Layers, biome.Seed, (Entity<MapGridComponent>?) null, out var tile);
                Assert.That(tile.HasValue && !tile.Value.IsEmpty, Is.True, "Natural terrain has a hole.");
                var def = (ContentTileDefinition) tiles[tile!.Value.TypeId];
                sampled++;
                tileCount[def.ID] = tileCount.GetValueOrDefault(def.ID) + 1;
                if (!def.Weather && misplaced.Count < 5)
                    misplaced.Add($"{def.ID} hides weather");
                if (!allowed.Contains(def.ID) && misplaced.Count < 5)
                    misplaced.Add($"{def.ID} is not a Thrascias tile");

                var inPocket = SnowTiles.Contains(def.ID) && pocketNoise.GetNoise(x, y) >= pocket.Threshold;
                if (inPocket)
                    pocketSnow++;

                if (generator.TryGetEntity(pos, biome.Layers, tile.Value, biome.Seed,
                        (Entity<MapGridComponent>?) null, out var feature))
                {
                    featureCount[feature] = featureCount.GetValueOrDefault(feature) + 1;
                    if (feature == Fungus)
                        fungusAt.Add(pos);
                    if (inPocket && feature == Whipvine)
                        pocketWhipvines++;
                    if (inPocket && feature == "WFFaunaThrasciasBoreal")
                        pocketSpawners++;
                    if (feature == "FloorLiquidPlasmaEntity" && def.ID != Crystal)
                        plasmaOffCrystal++;
                    var home = feature switch
                    {
                        _ when Pines.Contains(feature) => SnowTiles.Contains(def.ID),
                        _ when IceRocks.Contains(feature) => IceTiles.Contains(def.ID) && def.ID != Lake,
                        "CMRockIce" => IceTiles.Contains(def.ID) && def.ID != Lake,
                        _ when CrystalGrowth.Contains(feature) => def.ID == Crystal,
                        _ when Tundra.Contains(feature) => SnowTiles.Contains(def.ID) || def.ID == Dirt,
                        Fungus => SnowTiles.Contains(def.ID),
                        Whipvine => SnowTiles.Contains(def.ID) && inPocket,
                        IceCrystal => IceTiles.Contains(def.ID) && def.ID != Lake && def.ID != "WFFloorThrasciasClearIceSmooth",
                        _ when CrystalPlants.Contains(feature) => def.ID == Crystal,
                        "WFFaunaThrasciasTundra" => SnowTiles.Contains(def.ID) || def.ID == Dirt,
                        "WFFaunaThrasciasIce" => IceTiles.Contains(def.ID) && def.ID != Lake,
                        "WFFaunaThrasciasCrystal" => def.ID == Crystal,
                        LakeWater => def.ID == Lake,
                        "WFFaunaThrasciasBoreal" => SnowTiles.Contains(def.ID) && inPocket,
                        _ => true,
                    };
                    if (!home && misplaced.Count < 5)
                        misplaced.Add($"{feature} stands on {def.ID}");
                }

                if (generator.TryGetDecals(pos, biome.Layers, biome.Seed, (Entity<MapGridComponent>?) null, out var decals))
                    foreach (var (id, _) in decals)
                        decalCount[id] = decalCount.GetValueOrDefault(id) + 1;
            }

            double Share(int count) => 100.0 * count / sampled;
            int Tiles(params string[] ids) => ids.Sum(id => tileCount.GetValueOrDefault(id));
            int Features(IEnumerable<string> ids) => ids.Sum(id => featureCount.GetValueOrDefault(id));

            TestContext.Out.WriteLine($"Thrascias: {sampled} tiles sampled");
            foreach (var (id, count) in tileCount.OrderByDescending(kv => kv.Value))
                TestContext.Out.WriteLine($"  tile {id}: {Share(count):F2}%");
            foreach (var (id, count) in featureCount.OrderByDescending(kv => kv.Value))
                TestContext.Out.WriteLine($"  entity {id}: {Share(count):F3}%");
            foreach (var (id, count) in decalCount.OrderByDescending(kv => kv.Value))
                TestContext.Out.WriteLine($"  decal {id}: {Share(count):F3}%");

            Assert.That(misplaced, Is.Empty, string.Join("; ", misplaced));

            var snow = Tiles(SnowTiles);
            var packIce = Tiles(IceTiles.Where(id => id != Lake).ToArray());
            var lake = Tiles(Lake);
            var crystal = Tiles(Crystal);
            var pines = Features(Pines);
            var iceWalls = featureCount.GetValueOrDefault("CMRockIce");
            var crystalWalls = Features(new[] { "BayCrystal", "ChompQuartz" });
            var solids = pines + iceWalls + crystalWalls + Features(IceRocks) + Features(CrystalGrowth) + Features(Tundra.Where(id => id.Contains("Stone") || id.Contains("Rock")));

            Assert.That(Share(snow), Is.GreaterThan(55), "Snow should stay the common ground.");
            Assert.That(Share(Tiles("WFFloorThrasciasSnowRough", "WFFloorThrasciasSnowCalm", Dirt)), Is.GreaterThan(10), "Tundra variety is missing.");
            Assert.That(Share(Tiles(Dirt)), Is.GreaterThan(0), "No bare frozen dirt.");
            Assert.That(Share(packIce), Is.GreaterThan(2), "Pack ice should be a major region.");
            Assert.That(Share(packIce + lake), Is.LessThan(22), "Ice should not take over the snow.");
            Assert.That(Share(lake), Is.GreaterThan(0.3), "Frozen lakes are missing.");
            foreach (var id in IceTiles)
                Assert.That(tileCount.GetValueOrDefault(id), Is.GreaterThan(0), $"{id} never appears.");
            Assert.That(crystal, Is.GreaterThan(0), "Crystal fields are missing.");
            Assert.That(Share(crystal), Is.LessThan(8), "Crystal fields should be rare.");

            Assert.That(featureCount.GetValueOrDefault("FloorLiquidPlasmaEntity"), Is.GreaterThan(0), "Plasma pools are missing.");
            Assert.That(plasmaOffCrystal, Is.Zero, "Every plasma pool should sit in a crystal field.");
            Assert.That(featureCount.Keys.Any(id => id.Contains("PlanetmapOre")), Is.True, "Ore outcrops are missing.");
            Assert.That(featureCount.Keys.Any(id => id.Contains("Fauna")), Is.True, "Wildlife spawners are missing.");
            // Its lakes are real water that freezes by the air, over the lake bed tile, and nothing else stands in them.
            Assert.That(featureCount.Keys.Where(id => id.Contains("Water")), Is.EquivalentTo(new[] { LakeWater }), "Thrascias grows only its own lake water.");
            Assert.That(featureCount.GetValueOrDefault(LakeWater), Is.EqualTo(lake), "A lake tile has no water, or water stands off the lake.");

            Assert.That(Features(Tundra), Is.GreaterThan(0), "No tundra growth.");
            Assert.That(featureCount.GetValueOrDefault("WFFloraFrostbelle"), Is.GreaterThan(0), "No frostbelles.");
            Assert.That(featureCount.GetValueOrDefault("GoonShrubSnow") + featureCount.GetValueOrDefault("GoonShrubSnow2"), Is.GreaterThan(0), "No snow shrubs.");
            foreach (var rock in IceRocks.Concat(CrystalGrowth))
                Assert.That(featureCount.GetValueOrDefault(rock), Is.GreaterThan(0), $"{rock} never appears.");
            Assert.That(pines, Is.GreaterThan(0), "No boreal pines.");
            Assert.That(Share(pines), Is.LessThan(4), "Pine pockets should be a minority of the surface.");
            foreach (var pine in Pines)
                Assert.That(featureCount.GetValueOrDefault(pine), Is.GreaterThan(0), $"{pine} never appears.");
            Assert.That(iceWalls, Is.GreaterThan(0), "No ice cliffs.");
            Assert.That(Share(iceWalls), Is.LessThan(1.5), "Ice cliffs should be sparse.");
            Assert.That(featureCount.GetValueOrDefault("AuroraFloraTreeCrystal"), Is.GreaterThan(0), "No crystal trees.");
            Assert.That(Features(new[] { "ShiptestFloraRockCrystal", "VorePropRockCrystal", "VorePropRockCrystalDust" }), Is.GreaterThan(0), "No crystal rocks.");
            Assert.That(crystalWalls, Is.GreaterThan(0), "No crystal wall clusters.");
            Assert.That(Share(crystalWalls), Is.LessThan(0.2), "Crystal walls should be a few small clusters.");
            Assert.That(Share(solids), Is.LessThan(8), "Too much solid scenery for the world to stay walkable.");

            // Living plants and wildlife: each in its own region, at the density the brief sets.
            foreach (var spawner in Spawners)
                Assert.That(featureCount.GetValueOrDefault(spawner), Is.GreaterThan(0), $"{spawner} never appears.");
            Assert.That(featureCount.Keys.Contains("WFFaunaThrascias"), Is.False, "The planet-wide spawner should be split by region.");
            double Of(string id, int tiles) => 100.0 * featureCount.GetValueOrDefault(id) / Math.Max(1, tiles);
            var snowTiles = Tiles(SnowTiles);
            var plainIce = Tiles("WFFloorThrasciasPackIce", "WFFloorThrasciasRoughIce", "WFFloorThrasciasClearIce");
            TestContext.Out.WriteLine($"  density: frostbelle {Of("WFFloraFrostbelle", snowTiles + Tiles(Dirt)):F2}% of snow and dirt, fungus {Of(Fungus, snowTiles):F2}% of snow, " +
                                      $"ice crystal {Of(IceCrystal, plainIce):F2}% of pack ice, bloom {Of("WFFloraCrystalBloom", crystal):F2}% and pod {Of("WFFloraFrostPod", crystal):F2}% of crystal field, " +
                                      $"whipvine {100.0 * pocketWhipvines / Math.Max(1, pocketSnow):F2}% of {pocketSnow} pocket tiles, boreal spawner {100.0 * pocketSpawners / Math.Max(1, pocketSnow):F2}%");
            Assert.That(Of("WFFloraFrostbelle", snowTiles + Tiles(Dirt)), Is.InRange(1.0, 4.0), "Frostbelles should be a common pick.");
            Assert.That(Of(Fungus, snowTiles), Is.InRange(0.5, 4.0), "Frozen fungus should be a common pick.");
            Assert.That(Of(IceCrystal, plainIce), Is.InRange(0.2, 2.0), "Ice crystals should be a common pick.");
            Assert.That(Of("WFFloraCrystalBloom", crystal), Is.InRange(0.3, 2.0), "Crystal blooms should be a common pick.");
            Assert.That(Of("WFFloraFrostPod", crystal), Is.InRange(0.05, 0.6), "Frost pods should be an event, not a wall.");
            Assert.That(100.0 * pocketWhipvines / Math.Max(1, pocketSnow), Is.InRange(0.1, 0.6), "Whipvines should be an event, not a wall.");

            // Fungus grows only where the snowdrift decals would: the same noise, a higher threshold. It takes the place
            // of the drift decal on its own tile, as an entity and a decal can't share one.
            var drift = terrain.Layers.OfType<BiomeDecalLayer>().First(layer => layer.Decals.Contains("AuroraDecalFloorDecalSnowdrift"));
            var driftNoise = new FastNoiseLite();
            server.ResolveDependency<ISerializationManager>().CopyTo(drift.Noise, ref driftNoise, notNullableOverride: true);
            driftNoise.SetSeed(driftNoise.GetSeed() + biome.Seed);
            driftNoise.SetFractalOctaves(driftNoise.GetFractalOctaves());
            Assert.That(fungusAt.All(at => driftNoise.GetNoise(at.X, at.Y) >= drift.Threshold), Is.True,
                "Frozen fungus should sit inside the snowdrifts.");
            Assert.That(featureCount.GetValueOrDefault(Fungus), Is.GreaterThan(0), "No frozen fungus.");
            Assert.That(featureCount.GetValueOrDefault(IceCrystal), Is.GreaterThan(0), "No ice crystals.");
            Assert.That(featureCount.GetValueOrDefault(Whipvine), Is.GreaterThan(0), "No whipvines.");
            Assert.That(pocketWhipvines, Is.GreaterThan(0), "No whipvines in the pine pockets.");
            foreach (var plant in CrystalPlants)
                Assert.That(featureCount.GetValueOrDefault(plant), Is.GreaterThan(0), $"No {plant}.");

            Assert.That(decalCount.GetValueOrDefault("AuroraDecalFloorDecalSnowdrift"), Is.GreaterThan(0), "No snowdrifts.");
            Assert.That(decalCount.GetValueOrDefault("AuroraDecalFloorDecalSnowrocks"), Is.GreaterThan(0), "No snow rock decals.");
            Assert.That(decalCount.GetValueOrDefault("CMDecalRocksIceIce1"), Is.GreaterThan(0), "No ice rock decals.");
        });
        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }
}
