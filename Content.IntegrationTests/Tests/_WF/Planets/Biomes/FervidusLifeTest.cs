#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._WF.LivingWorld;
using Content.Server._WF.Planets;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Body.Components;
using Content.Shared._WF.LivingWorld;
using Content.Shared._WF.Planets;
using Content.Shared.Atmos;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Maps;
using Content.Shared.Mobs.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.Planets.Biomes;

/// <summary>Fervidus's living world: each region has its own wildlife and plants, and they work.</summary>
[TestFixture]
public sealed class FervidusLifeTest
{
    private const string Basalt = "FloorBasalt";
    private const string Ash = "WFFloorFervidusAshDust";
    private const string AshRock = "WFFloorFervidusAshRock";
    private const string AshDirt = "WFFloorFervidusAshDirt";

    private static readonly string[] Mobs =
    {
        "WFMobLaserRaptorFervidus", "WFMobAshGolem", "WFMobCinderSlime", "WFMobHellspawnFervidus",
    };

    private static readonly string[] Plants =
    {
        "WFFloraFireBlossom", "WFFloraAshCapShroom", "WFFloraAshLeafShroom", "WFFloraCoolCactus",
        "WFFloraPlasmaSpitter", "WFFloraAshLung",
    };

    /// <summary>What each feature may stand on, and its largest share of those tiles.</summary>
    private static readonly Dictionary<string, (string[] Tiles, double MaxShare)> Features = new()
    {
        ["WFFaunaFervidus"] = (new[] { Basalt }, 0.02),
        ["WFFaunaFervidusAshfields"] = (new[] { Ash, AshDirt }, 0.02),
        ["WFFaunaFervidusCrust"] = (new[] { AshRock }, 0.02),
        ["WFFloraFireBlossom"] = (new[] { Ash, AshDirt }, 0.03),
        ["WFFloraAshCapShroom"] = (new[] { Ash, AshDirt, AshRock }, 0.03),
        ["WFFloraAshLeafShroom"] = (new[] { Ash, AshDirt, AshRock }, 0.03),
        ["WFFloraCoolCactus"] = (new[] { AshDirt }, 0.03),
        ["WFFloraPlasmaSpitter"] = (new[] { AshRock }, 0.005),
        ["WFFloraAshLung"] = (new[] { Ash, AshDirt }, 0.005),
    };

    [Test]
    public async Task EachRegionHasItsOwnLifeAtASaneDensity()
    {
        await using var pair = await PoolManager.GetServerClient();
        await EnableFeature(pair);
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
            var tileCount = new Dictionary<string, int>();
            var onTile = new Dictionary<string, Dictionary<string, int>>();
            var spots = new Dictionary<string, List<Vector2i>>();
            var lava = new HashSet<Vector2i>();
            for (var x = -radius; x < radius; x++)
            for (var y = -radius; y < radius; y++)
            {
                if (x * x + y * y > radius * radius)
                    continue;
                var pos = new Vector2i(x, y);
                Assert.That(generator.TryGetBiomeTile(pos, biome.Layers, biome.Seed, (Entity<MapGridComponent>?) null, out var tile));
                var id = ((ContentTileDefinition) tileDefs[tile!.Value.TypeId]).ID;
                tileCount[id] = tileCount.GetValueOrDefault(id) + 1;
                if (!generator.TryGetEntity(pos, biome.Layers, tile.Value, biome.Seed, (Entity<MapGridComponent>?) null, out var feature))
                    continue;
                if (feature == "FloorLavaEntity")
                    lava.Add(pos);
                if (!Features.ContainsKey(feature))
                    continue;
                if (!onTile.TryGetValue(feature, out var tiles))
                    onTile[feature] = tiles = new Dictionary<string, int>();
                tiles[id] = tiles.GetValueOrDefault(id) + 1;
                if (!spots.TryGetValue(feature, out var list))
                    spots[feature] = list = new List<Vector2i>();
                list.Add(pos);
            }

            foreach (var (feature, (allowed, maxShare)) in Features)
            {
                Assert.That(onTile.ContainsKey(feature), Is.True, $"{feature} never turns up on Fervidus.");
                var total = onTile[feature].Values.Sum();
                var region = allowed.Sum(t => tileCount.GetValueOrDefault(t));
                var stray = onTile[feature].Keys.Where(t => !allowed.Contains(t)).ToList();
                TestContext.Out.WriteLine($"{feature}: {total} on {region} region tiles ({100.0 * total / region:F3}%), " +
                                          string.Join(", ", onTile[feature].Select(t => $"{t.Key} {t.Value}")));
                Assert.That(stray, Is.Empty, $"{feature} stands on {string.Join(", ", stray)}, outside its region.");
                Assert.That((double) total / region, Is.LessThanOrEqualTo(maxShare), $"{feature} is too dense.");
                Assert.That(total, Is.GreaterThan(5), $"{feature} is too rare to notice.");
            }

            // The crust things stay in the band round the lava, wherever it is.
            foreach (var feature in new[] { "WFFaunaFervidusCrust", "WFFloraPlasmaSpitter" })
            {
                var far = 0;
                foreach (var pos in spots[feature])
                {
                    var near = false;
                    for (var dx = -24; dx <= 24 && !near; dx++)
                    for (var dy = -24; dy <= 24 && !near; dy++)
                        near = lava.Contains(pos + new Vector2i(dx, dy));
                    if (!near)
                        far++;
                }
                TestContext.Out.WriteLine($"{feature}: {far} of {spots[feature].Count} more than 24 tiles from lava");
                Assert.That(far, Is.Zero, $"{feature} grows away from the lava crust.");
            }
        });
        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>A plating floor with Fervidus's air: hot, no oxygen.</summary>
    private static async Task<TestMapData> Rig(TestPair pair)
    {
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var maps = server.System<SharedMapSystem>();
        await server.WaitPost(() =>
        {
            var air = new GasMixture(2500) { Temperature = 373.15f };
            air.AdjustMoles(Gas.Nitrogen, 75);
            air.AdjustMoles(Gas.CarbonDioxide, 25);
            server.System<AtmosphereSystem>().SetMapAtmosphere(map.MapUid, false, air);
            var plating = server.ResolveDependency<ITileDefinitionManager>()["Plating"].TileId;
            for (var x = -8; x <= 8; x++)
            for (var y = -8; y <= 8; y++)
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), new Tile(plating));
        });
        return map;
    }

    private static FixedPoint2 Damage(TestPair pair, EntityUid uid, string type)
    {
        return pair.Server.EntMan.GetComponent<DamageableComponent>(uid).Damage.DamageDict.GetValueOrDefault(type);
    }

    [Test]
    public async Task EveryMobAndPlantSpawnsAndTheMobsLiveInFervidusAir()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await Rig(pair);
        var spawned = new List<EntityUid>();
        await server.WaitPost(() =>
        {
            var x = -6f;
            foreach (var id in Mobs.Concat(Plants))
            {
                spawned.Add(entMan.SpawnEntity(id, new EntityCoordinates(map.Grid.Owner, x, 0.5f)));
                x += 2f;
            }
        });
        await pair.RunSeconds(15);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                for (var i = 0; i < Mobs.Length; i++)
                {
                    var mob = spawned[i];
                    Assert.That(entMan.Deleted(mob), Is.False, $"{Mobs[i]} was deleted.");
                    Assert.That(entMan.GetComponent<MobStateComponent>(mob).CurrentState, Is.EqualTo(Content.Shared.Mobs.MobState.Alive),
                        $"{Mobs[i]} did not survive 15 s of Fervidus air.");
                    Assert.That(entMan.HasComponent<RespiratorComponent>(mob), Is.False, $"{Mobs[i]} breathes, and Fervidus has no air for it.");
                    Assert.That(Damage(pair, mob, "Asphyxiation"), Is.EqualTo(FixedPoint2.Zero), $"{Mobs[i]} suffocates.");
                    Assert.That(Damage(pair, mob, "Heat"), Is.EqualTo(FixedPoint2.Zero), $"{Mobs[i]} burns in the heat.");
                }
            });
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task FireBlossomSinglesWhoStepOnItAndSparesNatives()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await Rig(pair);
        EntityUid plant = default, near = default, native = default, far = default;
        await server.WaitPost(() =>
        {
            plant = entMan.SpawnEntity("WFFloraFireBlossom", new EntityCoordinates(map.Grid.Owner, 0.5f, 0.5f));
            near = entMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid.Owner, 0.5f, 0.9f));
            native = entMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid.Owner, 0.9f, 0.5f));
            far = entMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid.Owner, 4.5f, 0.5f));
            server.System<NpcFactionSystem>().AddFaction(native, "Xeno");
        });
        await pair.RunSeconds(1.5f);

        await server.WaitAssertion(() =>
        {
            Assert.That(Damage(pair, near, "Heat"), Is.GreaterThan(Damage(pair, far, "Heat")), "Standing in it should burn.");
            Assert.That(Damage(pair, native, "Heat"), Is.EqualTo(Damage(pair, far, "Heat")), "The planet's natives are spared.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task EmberFlowerSpitsAtTheNearbyAndAshLungPuffsAtThePassing()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await Rig(pair);
        EntityUid target = default, bystander = default;
        await server.WaitPost(() =>
        {
            entMan.SpawnEntity("WFFloraPlasmaSpitter", new EntityCoordinates(map.Grid.Owner, 0.5f, 0.5f));
            target = entMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid.Owner, 3.5f, 0.5f));
            bystander = entMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid.Owner, -7.5f, -7.5f));
        });
        await pair.RunSeconds(2.5f);

        await server.WaitAssertion(() =>
        {
            Assert.That(Damage(pair, target, "Heat"), Is.GreaterThan(Damage(pair, bystander, "Heat")), "The ember should hit.");
        });

        await server.WaitPost(() =>
        {
            entMan.SpawnEntity("WFFloraAshLung", new EntityCoordinates(map.Grid.Owner, -4.5f, 4.5f));
            entMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid.Owner, -4.5f, 5.5f));
        });
        await pair.RunSeconds(1.5f);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.EntityQuery<SmokeComponent>().Any(), Is.True, "The ash lung should puff.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PlantsYieldProduceAndTheProduceIsWhatItShouldBe()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await Rig(pair);
        var harvest = server.System<WFRegrowingHarvestSystem>();
        var solutions = server.System<SharedSolutionContainerSystem>();

        int Count(string id) => entMan.EntityQuery<MetaDataComponent>().Count(m => m.EntityPrototype?.ID == id);
        FixedPoint2 Reagent(EntityUid item, string reagent) =>
            solutions.TryGetSolution(item, "food", out _, out var food) ? food.GetTotalPrototypeQuantity(reagent) : FixedPoint2.Zero;

        await server.WaitAssertion(() =>
        {
            var expected = new Dictionary<string, string>
            {
                ["WFFloraFireBlossom"] = "WFFireBlossomBloom",
                ["WFFloraAshCapShroom"] = "WFAshShroom",
                ["WFFloraAshLeafShroom"] = "WFAshShroom",
                ["WFFloraCoolCactus"] = "WFCactusWaterPod",
            };
            foreach (var (plantId, produce) in expected)
            {
                var plant = entMan.SpawnEntity(plantId, new EntityCoordinates(map.Grid.Owner, 0.5f, 0.5f));
                var before = Count(produce);
                harvest.Harvest((plant, entMan.GetComponent<WFRegrowingHarvestComponent>(plant)), null);
                Assert.That(Count(produce), Is.GreaterThan(before), $"{plantId} should yield {produce}.");
            }

            var shroom = entMan.SpawnEntity("WFAshShroom", new EntityCoordinates(map.Grid.Owner, 1.5f, 0.5f));
            Assert.That(Reagent(shroom, "Toxin"), Is.GreaterThan(FixedPoint2.Zero), "Raw ash shrooms are mildly toxic.");
            var soup = entMan.SpawnEntity("WFFoodSoupAshcap", new EntityCoordinates(map.Grid.Owner, 1.5f, 1.5f));
            Assert.That(Reagent(soup, "Toxin"), Is.EqualTo(FixedPoint2.Zero), "The soup is safe.");
            var pod = entMan.SpawnEntity("WFCactusWaterPod", new EntityCoordinates(map.Grid.Owner, 2.5f, 0.5f));
            Assert.That(Reagent(pod, "Water"), Is.GreaterThan(FixedPoint2.Zero), "The cactus holds water.");
            var bloom = entMan.SpawnEntity("WFFireBlossomBloom", new EntityCoordinates(map.Grid.Owner, 2.5f, 1.5f));
            Assert.That(bloom, Is.Not.EqualTo(EntityUid.Invalid));

            var protos = server.ResolveDependency<IPrototypeManager>();
            var fizz = protos.Index<Content.Shared.Kitchen.FoodRecipePrototype>("RecipeWFBlossomFizz");
            Assert.That(fizz.IngredientsSolids.ContainsKey("WFFireBlossomBloom"), Is.True);
            Assert.That(entMan.SpawnEntity(fizz.Result, new EntityCoordinates(map.Grid.Owner, 3.5f, 0.5f)), Is.Not.EqualTo(EntityUid.Invalid));
            var soupRecipe = protos.Index<Content.Shared.Kitchen.FoodRecipePrototype>("RecipeWFAshShroomSoup");
            Assert.That(soupRecipe.IngredientsSolids.ContainsKey("WFAshShroom"), Is.True);
        });
        await pair.CleanReturnAsync();
    }
}
