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

/// <summary>Checks Carcinoma's surface regions occur, stay on weather tiles and stay inside the planet's allowed set.</summary>
[TestFixture]
[TestOf(typeof(SharedBiomeSystem))]
public sealed class CarcinomaBiomeTest
{
    private static readonly string[] BoneTiles = { "WFFloorFleshBoneMeaty", "WFFloorFleshBoneSpine", "WFFloorFleshBoneStraight" };
    private static readonly string[] MarshTiles = { "WFFloorFleshMarshMud", "WFFloorFleshMarshDirt" };

    /// <summary>Every tile the surface may place. The lake bed is hidden under water, so it needs no weather.</summary>
    private static readonly HashSet<string> AllowedTiles = new()
    {
        "WFFloorFlesh", "WFBloodOceanSeabed", "WFFloorFleshScab", "WFFloorFleshBoneMeaty", "WFFloorFleshBoneSpine",
        "WFFloorFleshBoneStraight", "WFFloorFleshCrust", "WFFloorFleshMarshMud", "WFFloorFleshMarshDirt",
        "WFFloorFleshJelly",
    };

    private static readonly HashSet<string> HiveWalls = new() { "GoonSetpiecesHive", "ChompResin", "PolarisResin" };

    private static readonly HashSet<string> BoneProps = new()
    {
        "WFCarcinomaBoneLong", "WFCarcinomaBoneRibs", "WFCarcinomaBoneRibsTall", "WFCarcinomaBoneSkullGreat",
        "WFCarcinomaBoneLumpOne", "WFCarcinomaBoneLumpTwo", "WFCarcinomaBoneLumpThree", "WFCarcinomaBoneHorn",
        "WFCarcinomaBoneHornTurned", "WFCarcinomaBoneSkull", "WFCarcinomaBoneSkullHalf", "WFCarcinomaBoneSkullHalfTurned",
    };

    private static readonly HashSet<string> DeadGrowth = new()
    {
        "GoonShrubDead", "GoonShrubDead2", "GoonShrubDead3", "GoonShrubDead4", "CoyoteFloraBurnedbush3",
        "CoyoteFloraBurnedbush5", "WFFleshSnagMahogany", "WFFleshSnagYew", "WFFleshSnagWalnut", "WFFleshSnagBarren",
        "WFFleshSnagBurnedOne", "WFFleshSnagBurnedThree",
    };

    /// <summary>Things a person can't walk through.</summary>
    private static readonly HashSet<string> Solid = new(HiveWalls)
    {
        "TauCetiMeatlandWall", "WallMeat", "FleshBlocker", "WFFleshPolyp", "WFFleshTree", "WFFleshPustuleTree",
        "WFCarcinomaBoneLumpOne", "WFCarcinomaBoneLumpTwo", "WFCarcinomaBoneLumpThree", "WFCarcinomaBoneHorn",
        "WFCarcinomaBoneHornTurned", "WFCarcinomaBoneSkull", "WFCarcinomaBoneSkullHalf", "WFCarcinomaBoneSkullHalfTurned",
        "WFFleshSnagMahogany", "WFFleshSnagYew", "WFFleshSnagWalnut", "WFFleshSnagBarren", "WFFleshSnagBurnedOne",
        "WFFleshSnagBurnedThree",
    };

    private static readonly HashSet<string> Existing = new()
    {
        "WFFaunaCarcinoma", "FleshBlocker", "WallMeat", "WFCarcinomaTendons", "WFCarcinomaAssimilationSack", "WFFleshPustule",
        "WFBloodRiver", "WFBloodOcean", "WFFleshTree", "WFFleshPustuleTree", "WFFleshPolyp", "TauCetiMeatlandWall",
        "WFCarcinomaPimple", "WFFaunaCarcinomaBone", "WFFaunaCarcinomaMarsh", "WFFaunaCarcinomaHive", "WFFaunaCarcinomaShore",
        "WFCarcinomaVitaeBlister", "WFCarcinomaBonePile", "WFCarcinomaMeatFruitBush", "WFCarcinomaTendonVine", "WFCarcinomaWeepingPolyp",
        "WFCarcinomaJellyMound", "WFCarcinomaJellyMoundSmall", "WFCarcinomaVent",
    };

    [Test]
    public async Task RegionsOccurOnWeatherTiles()
    {
        await using var pair = await PoolManager.GetServerClient();
        await PlanetFixture.EnableFeature(pair);
        var server = pair.Server;
        var entities = server.EntMan;
        var layers = new List<EntityUid>();
        await server.WaitAssertion(() =>
        {
            var surface = server.ResolveDependency<IPrototypeManager>().Index<WFPlanetSurfacePrototype>("WFSurfaceCarcinoma");
            var network = server.System<WFPlanetNetworkSystem>().BuildNetwork(surface, Vector2.Zero, "Carcinoma", null);
            Assert.That(network, Is.Not.Null);
            layers.AddRange(entities.GetComponent<WFPlanetNetworkComponent>(network!.Value).Layers);
            var biome = entities.GetComponent<BiomeComponent>(layers[0]);
            var generator = server.System<SharedBiomeSystem>();
            var tileDefs = server.ResolveDependency<ITileDefinitionManager>();

            var radius = surface.Radius;
            var tileCounts = new Dictionary<string, int>();
            var featureCounts = new Dictionary<string, int>();
            var featuresByTile = new Dictionary<(string Tile, string Feature), int>();
            var hive = new HashSet<Vector2i>();
            var blocked = new HashSet<Vector2i>();
            var open = new List<Vector2i>();
            var sampled = 0;
            for (var x = -radius; x < radius; x++)
            for (var y = -radius; y < radius; y++)
            {
                if (x * x + y * y > radius * radius)
                    continue;
                var pos = new Vector2i(x, y);
                generator.TryGetBiomeTile(pos, biome.Layers, biome.Seed, (Entity<MapGridComponent>?) null, out var tile);
                Assert.That(tile.HasValue && !tile.Value.IsEmpty, Is.True, "Natural terrain has a hole.");
                var def = (ContentTileDefinition) tileDefs[tile!.Value.TypeId];
                sampled++;
                tileCounts[def.ID] = tileCounts.GetValueOrDefault(def.ID) + 1;
                Assert.That(AllowedTiles, Does.Contain(def.ID), $"{def.ID} is not one of Carcinoma's tiles.");
                if (def.ID != "WFBloodOceanSeabed")
                    Assert.That(def.Weather, Is.True, $"{def.ID} is outdoors and must take weather.");

                generator.TryGetEntity(pos, biome.Layers, tile.Value, biome.Seed, (Entity<MapGridComponent>?) null, out var feature);
                if (feature != null)
                {
                    featureCounts[feature] = featureCounts.GetValueOrDefault(feature) + 1;
                    featuresByTile[(def.ID, feature)] = featuresByTile.GetValueOrDefault((def.ID, feature)) + 1;
                    if (HiveWalls.Contains(feature))
                        hive.Add(pos);
                }
                if (feature == null || (!Solid.Contains(feature) && feature != "WFBloodOcean"))
                    open.Add(pos);
                else
                    blocked.Add(pos);
            }

            double Share(IEnumerable<string> ids) => 100.0 * ids.Sum(id => tileCounts.GetValueOrDefault(id)) / sampled;
            int Count(IEnumerable<string> ids) => ids.Sum(id => featureCounts.GetValueOrDefault(id));
            TestContext.Out.WriteLine($"Carcinoma: {sampled} tiles sampled");
            foreach (var (id, count) in tileCounts.OrderByDescending(pair => pair.Value))
                TestContext.Out.WriteLine($"  tile {id}: {count} ({100.0 * count / sampled:F2}%)");
            foreach (var (id, count) in featureCounts.OrderByDescending(pair => pair.Value))
                TestContext.Out.WriteLine($"  feature {id}: {count} ({100.0 * count / sampled:F2}%)");

            var scab = Share(new[] { "WFFloorFleshScab" });
            var bone = Share(BoneTiles);
            var marsh = Share(MarshTiles);
            var crust = Share(new[] { "WFFloorFleshCrust" });
            TestContext.Out.WriteLine($"  shares: scab {scab:F2}%, bone {bone:F2}%, marsh {marsh:F2}%, crust {crust:F2}%");
            Assert.That(scab, Is.GreaterThan(0.5), "Lake shores lost their scab band.");
            Assert.That(bone, Is.GreaterThan(2), "Bone fields are missing.");
            Assert.That(bone, Is.LessThan(15), "Bone fields should be a minority region.");
            Assert.That(marsh, Is.GreaterThan(2), "The meat marsh is missing.");
            Assert.That(marsh, Is.LessThan(15), "The meat marsh should be a minority region.");
            Assert.That(crust, Is.GreaterThan(2), "Grove edges lost their dried crust.");

            // Features sit on the tile their region owns.
            foreach (var ((tile, feature), _) in featuresByTile)
            {
                if (BoneProps.Contains(feature))
                    Assert.That(BoneTiles.Contains(tile), Is.True, $"{feature} outside the bone fields, on {tile}.");
                if (DeadGrowth.Contains(feature))
                    Assert.That(tile, Is.EqualTo("WFFloorFleshCrust"), $"{feature} outside the crust, on {tile}.");
                if (feature == "TauCetiMeatlandWall")
                    Assert.That(tile, Does.StartWith("WFFloorFlesh"), "Blood walls belong on the scabbed shore.");
            }
            var fromRegions = new HashSet<string>(Existing);
            fromRegions.UnionWith(HiveWalls);
            fromRegions.UnionWith(BoneProps);
            fromRegions.UnionWith(DeadGrowth);
            Assert.That(featureCounts.Keys.Where(id => !fromRegions.Contains(id)), Is.Empty,
                "Something outside Carcinoma's known set grows on its surface.");

            Assert.That(Count(HiveWalls), Is.GreaterThan(0), "No hive pockets.");
            Assert.That(Count(BoneProps), Is.GreaterThan(0), "Bone fields have no bones.");
            Assert.That(Count(DeadGrowth), Is.GreaterThan(0), "Crust edges have no dead growth.");
            Assert.That(featureCounts.GetValueOrDefault("TauCetiMeatlandWall"), Is.GreaterThan(0), "No blood clots on the shore.");
            Assert.That(featureCounts.GetValueOrDefault("WallMeat"), Is.GreaterThan(0), "Flesh walls vanished.");

            // Pockets stay a few tiles wide and sit in the tendon forest, not across the surface.
            var largest = LargestGroup(hive, true);
            TestContext.Out.WriteLine($"  hive: {hive.Count} wall tiles, largest pocket {largest}");
            Assert.That(largest, Is.LessThanOrEqualTo(50), "A hive pocket grew past a few tiles.");
            Assert.That(100.0 * hive.Count / sampled, Is.LessThan(0.5), "Hive walls are too common.");

            // Water is lakes and ponds only: the square river channels are gone.
            Assert.That(featureCounts.GetValueOrDefault("WFBloodRiver"), Is.Zero, "Carcinoma still has river channels.");
            Assert.That(featureCounts.GetValueOrDefault("WFBloodOcean"), Is.GreaterThan(0), "Carcinoma has no lakes.");

            // The marsh is wetter than ordinary ground.
            var marshPools = featuresByTile.Where(f => MarshTiles.Contains(f.Key.Tile) && f.Key.Feature == "WFBloodOcean").Sum(f => f.Value);
            var plainPools = featuresByTile.GetValueOrDefault(("WFFloorFlesh", "WFBloodOcean"));
            var marshTiles = MarshTiles.Sum(id => tileCounts.GetValueOrDefault(id));
            var marshDensity = (double) marshPools / marshTiles;
            var plainDensity = (double) plainPools / tileCounts["WFFloorFlesh"];
            TestContext.Out.WriteLine($"  pools: marsh {100 * marshDensity:F2}% of tiles, plain flesh {100 * plainDensity:F2}%");
            Assert.That(marshDensity, Is.GreaterThan(plainDensity * 2), "The marsh should hold denser blood pools than plain flesh.");
            var marshPlants = featuresByTile.Where(f => MarshTiles.Contains(f.Key.Tile)
                && f.Key.Feature is "WFFleshPolyp" or "WFFleshPustule").Sum(f => f.Value);
            Assert.That(marshPlants, Is.GreaterThan(0), "The marsh pools have no polyps or pustules round them.");

            // The new walls and props leave the surface walkable.
            var passable = new HashSet<Vector2i>(open);
            var main = LargestGroup(passable, false);
            TestContext.Out.WriteLine($"  walkable: {main}/{passable.Count} open tiles in the largest connected area, {blocked.Count} blocked");
            Assert.That((double) main / passable.Count, Is.GreaterThan(0.9), "Solid scenery cuts the surface into islands.");
        });
        await PlanetFixture.Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>The size of the biggest connected group of tiles; <paramref name="diagonals"/> also joins corner neighbours.</summary>
    private static int LargestGroup(HashSet<Vector2i> tiles, bool diagonals)
    {
        var unvisited = new HashSet<Vector2i>(tiles);
        var best = 0;
        while (unvisited.Count > 0)
        {
            var start = unvisited.First();
            unvisited.Remove(start);
            var pending = new Queue<Vector2i>();
            pending.Enqueue(start);
            var size = 0;
            while (pending.TryDequeue(out var tile))
            {
                size++;
                for (var dx = -1; dx <= 1; dx++)
                for (var dy = -1; dy <= 1; dy++)
                {
                    if ((dx == 0 && dy == 0) || (!diagonals && dx != 0 && dy != 0))
                        continue;
                    var next = new Vector2i(tile.X + dx, tile.Y + dy);
                    if (unvisited.Remove(next))
                        pending.Enqueue(next);
                }
            }
            best = System.Math.Max(best, size);
        }
        return best;
    }
}
