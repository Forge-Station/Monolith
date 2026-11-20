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

/// <summary>Samples all of Fervidus and checks its ashfields, lava crust, flora and pools turn up where they should.</summary>
[TestFixture]
[TestOf(typeof(SharedBiomeSystem))]
public sealed class FervidusBiomeTest
{
    private const string Basalt = "FloorBasalt";
    private const string Ash = "WFFloorFervidusAshDust";
    private const string AshRock = "WFFloorFervidusAshRock";
    private const string AshDirt = "WFFloorFervidusAshDirt";
    private const string Tar = "WFFloorFervidusTarPool";
    private const string Sulfur = "WFFloorFervidusSulfurPool";

    private const string SlimeGlass = "WFFloorFervidusObsidianGlass";

    private static readonly string[] Ground = { Basalt, Ash, AshRock, AshDirt, Tar, Sulfur, SlimeGlass };

    private static readonly string[] AshFlora =
    {
        "TgFloraAshPlasmaFlower", "TgFloraAshPodsneyzka",
        "NovaAshCapShroomColored", "NovaAshLeafShroomColored", "NovaAshTallShroomColored",
        // The living ones (Planets/Life/fervidus.yml) stand where the decor fire-blossom, ash caps, ash cacti and vahrezjia did.
        "WFFloraFireBlossom", "WFFloraAshLung", "WFFloraAshCapShroom", "WFFloraAshLeafShroom", "WFFloraCoolCactus",
        "CMGrassTallgrassDesert", "ShiptestFloraGrassBrown", "CoyoteFloraBurnedbush3", "CoyoteFloraBurnedbush5",
        "GoonShrubDead", "CoyoteFloraBurnedtree1", "CoyoteFloraBurnedtree2", "CoyoteFloraBurnedtree3",
        "CoyoteFloraBurnedtree5", "ShiptestTreeDeadHell",
    };

    private static readonly string[] Scenery =
    {
        "GoonFakeobjectRockpile", "GoonFakeobjectRockpileBig", "CMRockBlackGroundDense", "CMRockBlackGroundDense3",
        "TgFloraRockVolcano", "TgFloraRockSideriteGrowth",
    };

    [Test]
    public async Task AshfieldsLavaCrustAndPoolsOccur()
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
            var surface = protos.Index<WFPlanetSurfacePrototype>("WFSurfaceFervidus");
            var network = server.System<WFPlanetNetworkSystem>().BuildNetwork(surface, Vector2.Zero, "Fervidus", null);
            Assert.That(network, Is.Not.Null);
            layers.AddRange(entities.GetComponent<WFPlanetNetworkComponent>(network!.Value).Layers);
            var biome = entities.GetComponent<BiomeComponent>(layers[0]);
            var generator = server.System<SharedBiomeSystem>();

            var radius = surface.Radius;
            var tiles = new Dictionary<Vector2i, string>();
            var tileCount = new Dictionary<string, int>();
            var features = new Dictionary<string, int>();
            var ashFloraOffAsh = new HashSet<string>();
            var onPools = 0;
            var open = 0;
            var sampled = 0;
            var crustNextToLava = 0;
            var lavaTiles = 0;
            for (var x = -radius; x < radius; x++)
            for (var y = -radius; y < radius; y++)
            {
                if (x * x + y * y > radius * radius)
                    continue;
                var pos = new Vector2i(x, y);
                Assert.That(generator.TryGetBiomeTile(pos, biome.Layers, biome.Seed, (Entity<MapGridComponent>?) null, out var tile),
                    "Fervidus has a hole in its ground.");
                var def = (ContentTileDefinition) tileDefs[tile!.Value.TypeId];
                sampled++;
                tiles[pos] = def.ID;
                tileCount[def.ID] = tileCount.GetValueOrDefault(def.ID) + 1;
                Assert.That(Ground, Does.Contain(def.ID), $"{def.ID} is not part of Fervidus's ground.");
                Assert.That(def.Weather, Is.True, $"{def.ID} is outdoors on Fervidus and must take weather.");

                generator.TryGetEntity(pos, biome.Layers, tile.Value, biome.Seed, (Entity<MapGridComponent>?) null, out var feature);
                if (feature == null)
                {
                    open++;
                    continue;
                }
                features[feature] = features.GetValueOrDefault(feature) + 1;
                if (def.ID is Tar or Sulfur)
                    onPools++;
                if (AshFlora.Contains(feature) && def.ID == Basalt)
                    ashFloraOffAsh.Add(feature);
                if (feature == "FloorLavaEntity")
                {
                    lavaTiles++;
                    if (def.ID == AshRock)
                        crustNextToLava++;
                }
            }

            double Share(string id) => (double) tileCount.GetValueOrDefault(id) / sampled;
            TestContext.Out.WriteLine("Fervidus tiles: " + string.Join(", ",
                Ground.Select(id => $"{id} {100 * Share(id):F2}% ({tileCount.GetValueOrDefault(id)})")));
            TestContext.Out.WriteLine($"Open {100.0 * open / sampled:F1}% of {sampled}, lava {lavaTiles} (on crust {crustNextToLava})");
            TestContext.Out.WriteLine("Fervidus features: " + string.Join(", ",
                features.OrderByDescending(f => f.Value).Select(f => $"{f.Key} {f.Value}")));

            Assert.That(Share(Basalt), Is.GreaterThan(0.25), "Black basalt flats are still most of the ground.");
            Assert.That(Share(Ash), Is.GreaterThan(0.08), "Ash flats should cover a major share.");
            Assert.That(Share(AshRock), Is.GreaterThan(0.03), "The lava crust and ash rock are missing.");
            Assert.That(Share(AshDirt), Is.GreaterThan(0.02), "Scrub ground is missing.");
            Assert.That(tileCount.GetValueOrDefault(Tar), Is.GreaterThan(0), "No tar pools.");
            Assert.That(tileCount.GetValueOrDefault(Sulfur), Is.GreaterThan(0), "No sulfur pools.");
            Assert.That(Share(Tar) + Share(Sulfur), Is.LessThan(0.01), "Pools should stay small and rare.");
            Assert.That(onPools, Is.Zero, "Nothing grows or stands in a pool.");
            Assert.That(ashFloraOffAsh, Is.Empty, "Ash flora grows on ash ground only.");
            Assert.That((double) open / sampled, Is.GreaterThan(0.60), "Most of the surface should stay open.");

            Assert.That(lavaTiles, Is.GreaterThan(0), "The lava channels are gone.");
            Assert.That(crustNextToLava, Is.GreaterThan(lavaTiles / 2), "Lava should lie in a crust of cooled rock.");

            var missing = AshFlora.Concat(Scenery).Where(id => !features.ContainsKey(id)).ToList();
            Assert.That(missing, Is.Empty, $"These never turn up on Fervidus: {string.Join(", ", missing)}.");

            // Pools: connected runs of pool tiles stay a few tiles across.
            var pending = tiles.Where(t => t.Value is Tar or Sulfur).Select(t => t.Key).ToHashSet();
            var widest = 0;
            while (pending.Count > 0)
            {
                var start = pending.First();
                var queue = new Queue<Vector2i>();
                queue.Enqueue(start);
                pending.Remove(start);
                var size = 0;
                while (queue.TryDequeue(out var current))
                {
                    size++;
                    foreach (var step in new[] { Vector2i.Up, Vector2i.Down, Vector2i.Left, Vector2i.Right })
                    {
                        if (pending.Remove(current + step))
                            queue.Enqueue(current + step);
                    }
                }
                widest = System.Math.Max(widest, size);
            }
            TestContext.Out.WriteLine($"Largest pool {widest} tiles");
            Assert.That(widest, Is.LessThanOrEqualTo(60), "A tar or sulfur pool is too big.");
        });
        await PlanetFixture.Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }
}
