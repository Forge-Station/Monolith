#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._WF.Planets;
using Content.Server.Parallax;
using Content.Shared._WF.Planets;
using Content.Shared.Maps;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Planets.Biomes;

/// <summary>Checks Merak's dunes, badlands, cactus fields, gravel flats and rare oases all occur and stay in bounds.</summary>
[TestFixture]
[TestOf(typeof(BiomeSystem))]
public sealed class MerakBiomeTest
{
    private const string Surface = "WFSurfaceMerak";
    private const string TilePrefix = "WFFloorMerak";

    /// <summary>Real oases (water bodies of at least this many tiles) the world may have: a handful.</summary>
    private const int MinOases = 3;
    private const int MaxOases = 8;
    private const int OasisTiles = 20;

    private static readonly string[] DuneTiles =
    {
        "WFFloorMerakDuneSand", "WFFloorMerakGoldSand", "WFFloorMerakPaleSand",
    };

    private static readonly string[] BadlandTiles =
    {
        "WFFloorMerakRedDesertSand", "WFFloorMerakRedDesertRock", "WFFloorMerakRedDesertRockCracked", "WFFloorMerakDarkSand",
    };

    private static readonly string[] FlatTiles =
    {
        "WFFloorMerakDesertGravel", "WFFloorMerakDesertSandrock", "WFFloorMerakDesertCracked",
    };

    private static readonly string[] OasisGround = { "WFFloorMerakOasisDirt", "WFFloorMerakOasisGrass" };

    private static bool IsCactus(string id) =>
        id.Contains("cacti") || id.Contains("Cactus") || id.Contains("Agave") || id.Contains("Yucca")
        || id.Contains("Mesquite") || id.Contains("Prickly") || id.Contains("Feracactus") || id.Contains("Pinyon");

    private static bool IsPalm(string id) => id.Contains("Palmtree") || id.Contains("palmtree") || id.Contains("FloraTreePalm");

    [Test]
    public async Task MerakHasDunesBadlandsCactusFieldsGravelFlatsAndRareOases()
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
            var biome = new Entity<BiomeComponent>(layers[0], entities.GetComponent<BiomeComponent>(layers[0]));
            var biomes = server.System<BiomeSystem>();
            var radius = surface.Radius;

            var tiles = new Dictionary<string, int>();
            var features = new Dictionary<string, int>();
            var sampled = 0;
            var open = 0;
            var foreign = new HashSet<string>();
            var dry = new HashSet<string>();
            var water = new HashSet<Vector2i>();
            for (var x = -radius; x < radius; x++)
            for (var y = -radius; y < radius; y++)
            {
                if (x * x + y * y > radius * radius)
                    continue;
                var index = new Vector2i(x, y);
                if (biomes.WfIsWater(biome, index))
                    water.Add(index);
                if ((x & 1) != 0 || (y & 1) != 0)
                    continue;

                sampled++;
                Assert.That(biomes.TryGetBiomeTile(index, biome.Comp.Layers, biome.Comp.Seed, (Entity<Robust.Shared.Map.Components.MapGridComponent>?) null, out var tile), Is.True);
                var def = (ContentTileDefinition) tileDefs[tile!.Value.TypeId];
                tiles[def.ID] = tiles.GetValueOrDefault(def.ID) + 1;
                if (!def.ID.StartsWith(TilePrefix))
                    foreign.Add(def.ID);
                if (!def.Weather)
                    dry.Add(def.ID);

                if (biomes.TryGetEntity(index, biome.Comp.Layers, tile.Value, biome.Comp.Seed,
                        (Entity<Robust.Shared.Map.Components.MapGridComponent>?) null, out var feature))
                    features[feature] = features.GetValueOrDefault(feature) + 1;
                else
                    open++;
            }

            double Share(IEnumerable<string> ids) => 100.0 * ids.Sum(id => tiles.GetValueOrDefault(id)) / sampled;
            double FeatureShare(System.Func<string, bool> pick) => 100.0 * features.Where(f => pick(f.Key)).Sum(f => f.Value) / sampled;

            TestContext.Out.WriteLine($"Merak: {sampled} samples, open {100.0 * open / sampled:F1}%");
            foreach (var (id, count) in tiles.OrderByDescending(t => t.Value))
                TestContext.Out.WriteLine($"  tile {id}: {100.0 * count / sampled:F2}%");
            foreach (var (id, count) in features.OrderByDescending(t => t.Value))
                TestContext.Out.WriteLine($"  entity {id}: {100.0 * count / sampled:F3}%");

            var dunes = Share(DuneTiles);
            var badlands = Share(BadlandTiles);
            var flats = Share(FlatTiles);
            var oasisGround = Share(OasisGround);
            var cacti = FeatureShare(IsCactus);
            var mesas = FeatureShare(id => id == "CMRockOrange");
            var palms = FeatureShare(IsPalm);
            TestContext.Out.WriteLine($"  dunes {dunes:F1}%, badlands {badlands:F1}%, flats {flats:F1}%, oasis ground {oasisGround:F2}%, "
                + $"cacti {cacti:F2}%, mesa walls {mesas:F3}%, palms {palms:F3}%");

            var oases = new List<int>();
            var unvisited = new HashSet<Vector2i>(water);
            while (unvisited.Count > 0)
            {
                var start = unvisited.First();
                var pending = new Queue<Vector2i>();
                pending.Enqueue(start);
                unvisited.Remove(start);
                var size = 0;
                while (pending.TryDequeue(out var tile))
                {
                    size++;
                    foreach (var step in new[] { Vector2i.Up, Vector2i.Down, Vector2i.Left, Vector2i.Right })
                    {
                        if (unvisited.Remove(tile + step))
                            pending.Enqueue(tile + step);
                    }
                }
                oases.Add(size);
            }
            var real = oases.Where(size => size >= OasisTiles).OrderByDescending(size => size).ToList();
            TestContext.Out.WriteLine($"  oases: {real.Count} of {OasisTiles}+ tiles ({string.Join(",", real)}), {oases.Count} bodies, {water.Count} water tiles");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(foreign, Is.Empty, "Merak placed tiles that are not its own.");
                Assert.That(dry, Is.Empty, "These tiles would hide the dust storms.");
                Assert.That(dunes, Is.GreaterThan(35), "Dune seas are the common ground.");
                Assert.That(badlands, Is.GreaterThan(2), "The red-rock badlands vanished.");
                Assert.That(flats, Is.GreaterThan(2), "The gravel flats vanished.");
                Assert.That(oasisGround, Is.GreaterThan(0), "No oasis shore.");
                Assert.That(features.Keys.Any(IsCactus), Is.True, "The cactus fields vanished.");
                Assert.That(cacti, Is.LessThan(8), "Cacti flood the desert.");
                Assert.That(mesas, Is.GreaterThan(0), "The badlands lost their mesas.");
                Assert.That(mesas, Is.LessThan(1.5), "Mesa walls must stay small clusters.");
                Assert.That(palms, Is.GreaterThan(0), "Oases have no palms.");
                Assert.That(features.Keys.Any(id => id.Contains("Bone")), Is.True, "The gravel flats lost their bones.");
                Assert.That(features.Keys.Any(id => id.Contains("Spire")), Is.True, "The gravel flats lost their rock spires.");
                Assert.That((double) open / sampled, Is.GreaterThan(0.65), "The ground must stay mostly open.");
                Assert.That(real.Count, Is.InRange(MinOases, MaxOases), "Oases are rare: a handful on the whole world.");
            }
        });
        await PlanetFixture.Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }
}
