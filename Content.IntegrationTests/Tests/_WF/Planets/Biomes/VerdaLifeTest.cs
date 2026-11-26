#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._WF.LivingWorld;
using Content.Server._WF.Planets;
using Content.Shared._WF.LivingWorld;
using Content.Shared._WF.Planets;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.EntityTable;
using Content.Shared.FixedPoint;
using Content.Shared.Kitchen;
using Content.Shared.Maps;
using Content.Shared.Mobs.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Planets.Biomes;

/// <summary>
/// Verda's wildlife and plants: each grows on its own region's ground at a sane density, every new prototype spawns,
/// the bramble and glowroot react, the produce comes off its plant and a deer bolts from someone close.
/// </summary>
[TestFixture]
public sealed class VerdaLifeTest
{
    private const string Surface = "WFSurfaceAsclepiu";

    private static readonly HashSet<string> Meadow = new() { "FloorPlanetGrass", "FloorPlanetDirt", "WFFloorVerdaMeadowGrass" };
    private static readonly HashSet<string> Forest = new() { "WFFloorVerdaForestGrass" };
    private static readonly HashSet<string> Woodland = new() { "FloorPlanetGrass", "WFFloorVerdaForestGrass" };
    private static readonly HashSet<string> MeadowGrass = new() { "WFFloorVerdaMeadowGrass" };
    private static readonly HashSet<string> LakeShore = new() { "WFFloorVerdaMarshGrass", "WFFloorVerdaMarshMud", "WFFloorVerdaShoreSand" };
    private static readonly HashSet<string> Pond = new() { "WFFloorVerdaPondMud" };
    private static readonly HashSet<string> WaterEdge = new() { "WFFloorVerdaMarshMud", "WFFloorVerdaShoreSand", "WFFloorVerdaPondMud" };
    private static readonly HashSet<string> Mud = new() { "WFFloorVerdaMarshMud", "WFFloorVerdaPondMud" };
    private static readonly HashSet<string> Scar = new()
    {
        "WFFloorVerdaScarDirt", "WFFloorVerdaScarDirtPatch", "WFFloorVerdaScarWiltedGrass", "WFFloorVerdaScarAshDirt",
    };
    private static readonly HashSet<string> Snow = new() { "FloorSnow", "WFFloorVerdaSnowRough" };

    /// <summary>A feature: where it may stand, what tiles its share is measured against, and the share allowed.</summary>
    private sealed record Feature(string Id, HashSet<string> Allowed, HashSet<string> Base, double Min, double Max);

    private static readonly Feature[] Features =
    {
        new("WFVerdaPlantMushroom", Forest, Forest, 0.005, 0.03),
        new("WFVerdaPlantBerryBush", Woodland, Forest, 0.003, 0.03),
        new("WFVerdaPlantBramble", Woodland, Forest, 0.0005, 0.01),
        new("WFVerdaPlantReeds", WaterEdge, WaterEdge, 0.005, 0.05),
        new("WFVerdaPlantShoreSage", WaterEdge, WaterEdge, 0.003, 0.04),
        new("WFVerdaPlantLotus", Mud, Mud, 0.003, 0.05),
        new("WFVerdaPlantGlowroot", Scar, Scar, 0.0005, 0.01),
        new("WFFaunaVerdaMeadow", Meadow, Meadow, 0.0001, 0.02),
        new("WFFaunaVerdaWoods", Forest, Forest, 0.0001, 0.02),
        new("WFFaunaVerdaShore", LakeShore.Union(Pond).ToHashSet(), LakeShore.Union(Pond).ToHashSet(), 0.0001, 0.03),
        new("WFFaunaVerdaScar", Scar, Scar, 0.0001, 0.02),
        new("WFFaunaVerdaSnow", Snow, Snow, 0.0001, 0.02),
        new("WFFaunaVerdaBees", MeadowGrass, MeadowGrass, 0.0001, 0.02),
        new("WFFaunaVerdaGrass", MeadowGrass, MeadowGrass, 0.0001, 0.02),
        new("WFFaunaVerdaRatKing", Scar, Scar, 0, 0.01),
    };

    private static bool IsFlower(string id) =>
        id.StartsWith("CoyoteWildPlantFlower") || id is "DarkPackFloraDarkpackFlowerMarigold" or "CoyoteFloraWildPlantThistle"
            or "CoyoteWildPlantPetuniaBlue";

    private static bool IsTallGrass(string id) => id is "CoyoteFloraTallgrass3" or "CoyoteFloraTallgrass5";

    [Test]
    public async Task LifeGrowsOnItsOwnGroundOnly()
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

            var wanted = Features.Select(f => f.Id).ToHashSet();
            var tileCount = new Dictionary<string, int>();
            var onTile = new Dictionary<string, Dictionary<string, int>>();
            var entityAt = new Dictionary<Vector2i, string>();
            var radius = surface.Radius;
            const int step = 2;
            for (var x = -radius; x < radius; x += step)
            for (var y = -radius; y < radius; y += step)
            {
                if (x * x + y * y > radius * radius)
                    continue;
                var index = new Vector2i(x, y);
                Assert.That(biomes.TryGetBiomeTile(index, biome.Layers, biome.Seed, (Entity<MapGridComponent>?) null, out var tile), Is.True);
                var tileId = ((ContentTileDefinition) tileDefs[tile!.Value.TypeId]).ID;
                tileCount[tileId] = tileCount.GetValueOrDefault(tileId) + 1;
                if (!biomes.TryGetEntity(index, biome.Layers, tile.Value, biome.Seed, (Entity<MapGridComponent>?) null, out var entity))
                    continue;
                entityAt[index] = entity;
                if (!wanted.Contains(entity))
                    continue;
                if (!onTile.TryGetValue(entity, out var tiles))
                    onTile[entity] = tiles = new Dictionary<string, int>();
                tiles[tileId] = tiles.GetValueOrDefault(tileId) + 1;
            }

            using (Assert.EnterMultipleScope())
            {
                foreach (var feature in Features)
                {
                    var tiles = onTile.GetValueOrDefault(feature.Id) ?? new Dictionary<string, int>();
                    var total = tiles.Values.Sum();
                    var baseCount = feature.Base.Sum(id => tileCount.GetValueOrDefault(id));
                    var share = baseCount == 0 ? 0 : (double) total / baseCount;
                    TestContext.Out.WriteLine($"  {feature.Id}: {total} of {baseCount} base tiles = {100 * share:F3}% on "
                        + string.Join(", ", tiles.Select(kv => $"{kv.Key} {kv.Value}")));
                    foreach (var id in tiles.Keys)
                        Assert.That(feature.Allowed, Does.Contain(id), $"{feature.Id} grows on {id}, not its own ground.");
                    Assert.That(share, Is.InRange(feature.Min, feature.Max), $"{feature.Id} covers {100 * share:F3}% of its ground.");
                }

                // Bees hang about wildflowers and snakes at tall grass: most of their spawners have one within four tiles.
                double Near(string spawner, System.Func<string, bool> plant)
                {
                    var found = 0;
                    var all = 0;
                    foreach (var (index, id) in entityAt)
                    {
                        if (id != spawner)
                            continue;
                        all++;
                        var close = false;
                        for (var dx = -2; dx <= 2 && !close; dx++)
                        for (var dy = -2; dy <= 2 && !close; dy++)
                            close = entityAt.TryGetValue(index + new Vector2i(dx * step, dy * step), out var near) && plant(near);
                        if (close)
                            found++;
                    }
                    return all == 0 ? 0 : (double) found / all;
                }

                var bees = Near("WFFaunaVerdaBees", IsFlower);
                var snakes = Near("WFFaunaVerdaGrass", IsTallGrass);
                TestContext.Out.WriteLine($"  bee spawners with a flower in four tiles: {100 * bees:F0}%, snake spawners with tall grass: {100 * snakes:F0}%");
                Assert.That(bees, Is.GreaterThan(0.5), "Bees should gather by the wildflowers.");
                Assert.That(snakes, Is.GreaterThan(0.5), "Snakes should lie in the tall grass.");

                // The spawners as a whole stay near the one-per-ground density they replaced.
                var main = new[] { "WFFaunaVerdaMeadow", "WFFaunaVerdaWoods", "WFFaunaVerdaShore", "WFFaunaVerdaScar", "WFFaunaVerdaSnow" }
                    .Sum(id => (onTile.GetValueOrDefault(id) ?? new Dictionary<string, int>()).Values.Sum());
                var extra = new[] { "WFFaunaVerdaBees", "WFFaunaVerdaGrass", "WFFaunaVerdaRatKing" }
                    .Sum(id => (onTile.GetValueOrDefault(id) ?? new Dictionary<string, int>()).Values.Sum());
                TestContext.Out.WriteLine($"  regional spawner tiles {main}, special spawner tiles {extra}");
                Assert.That(extra, Is.LessThan(main), "The special spawners should not outnumber the regional ones.");
            }
        });
        await PlanetFixture.Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>Every mob and plant Verda adds spawns, and every animal a table rolls is a mob the fauna system can use.</summary>
    [Test]
    public async Task EveryNewPrototypeSpawns()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var tables = new List<EntityTablePrototype>();
        var mobIds = new HashSet<string>();
        var otherIds = new HashSet<string>();

        await server.WaitAssertion(() =>
        {
            var protos = server.ProtoMan;
            var entityTables = server.System<EntityTableSystem>();
            foreach (var table in protos.EnumeratePrototypes<EntityTablePrototype>().Where(t => t.ID.StartsWith("WFFaunaVerda")))
            {
                tables.Add(table);
                for (var i = 0; i < 300; i++)
                    foreach (var spawn in entityTables.GetSpawns(table.Table))
                        mobIds.Add(spawn.Id);
            }
            foreach (var entity in protos.EnumeratePrototypes<EntityPrototype>().Where(e => e.ID.StartsWith("WFMobVerda") || e.ID.StartsWith("WFVerda") && !e.Abstract))
                otherIds.Add(entity.ID);
            Assert.That(tables, Has.Count.EqualTo(9), "WFFaunaVerdaDeerHerd and the eight regional tables.");
            foreach (var id in new[] { "WFFaunaVerdaMeadow", "WFFaunaVerdaWoods", "WFFaunaVerdaBees", "WFFaunaVerdaGrass", "WFFaunaVerdaShore",
                         "WFFaunaVerdaScar", "WFFaunaVerdaRatKing", "WFFaunaVerdaSnow" })
                Assert.That(protos.HasIndex<EntityPrototype>(id), Is.True, $"{id} has a table but no spawner.");
        });

        await server.WaitAssertion(() =>
        {
            var coords = new EntityCoordinates(map.Grid.Owner, 0.5f, 0.5f);
            foreach (var id in mobIds.Concat(otherIds).Distinct())
            {
                var uid = entMan.SpawnEntity(id, coords);
                if (mobIds.Contains(id))
                    Assert.That(entMan.HasComponent<MobStateComponent>(uid), Is.True, $"{id} is rolled as an animal but is not a mob.");
            }
            foreach (var expected in new[] { "WFMobVerdaDeer", "WFMobVerdaDoe", "WFMobVerdaDeerCharger", "WFMobVerdaDeerTwoHeaded",
                         "WFMobVerdaScarRat", "WFMobVerdaRatKing", "WFMobVerdaFrostBear", "MobFox", "MobBee", "MobSnake", "MobFrog",
                         "MobCrab", "MobPenguin" })
                Assert.That(mobIds, Does.Contain(expected), $"No table rolls a {expected}.");
        });
        await pair.RunSeconds(2);
        await pair.CleanReturnAsync();
    }

    /// <summary>A plating floor with the plant at the centre and a mob at each distance along +x.</summary>
    private static async Task<(EntityUid Plant, EntityUid[] Mobs)> Rig(TestPair pair, string plant, string mobProto, params float[] distances)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var maps = server.System<SharedMapSystem>();
        EntityUid plantUid = default;
        var mobs = new EntityUid[distances.Length];
        await server.WaitPost(() =>
        {
            var plating = server.ResolveDependency<ITileDefinitionManager>()["Plating"].TileId;
            for (var x = -8; x <= 8; x++)
            for (var y = -8; y <= 8; y++)
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), new Tile(plating));
            plantUid = entMan.SpawnEntity(plant, new EntityCoordinates(map.Grid.Owner, 0.5f, 0.5f));
            for (var i = 0; i < distances.Length; i++)
                mobs[i] = entMan.SpawnEntity(mobProto, new EntityCoordinates(map.Grid.Owner, 0.5f + distances[i], 0.5f));
        });
        return (plantUid, mobs);
    }

    private static FixedPoint2 Damage(TestPair pair, EntityUid uid, string type) =>
        pair.Server.EntMan.GetComponent<DamageableComponent>(uid).Damage.DamageDict.GetValueOrDefault(type);

    [Test]
    public async Task BrambleLashesAPersonButNotTheWildlife()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var (plant, mobs) = await Rig(pair, "WFVerdaPlantBramble", "MobHuman", 0.9f, 5f);
        EntityUid deer = default;
        await server.WaitPost(() => deer = entMan.SpawnEntity("WFMobVerdaDeer", new EntityCoordinates(entMan.GetComponent<TransformComponent>(plant).ParentUid, -0.6f, 0.5f)));
        await pair.RunSeconds(1.5f);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(Damage(pair, mobs[0], "Slash"), Is.GreaterThan(FixedPoint2.Zero), "The bramble should lash the person beside it.");
                Assert.That(Damage(pair, mobs[0], "Slash"), Is.LessThanOrEqualTo(FixedPoint2.New(8)), "Its lash should be light.");
                Assert.That(Damage(pair, mobs[1], "Slash"), Is.EqualTo(FixedPoint2.Zero), "A person well away is left alone.");
                Assert.That(Damage(pair, deer, "Slash"), Is.EqualTo(FixedPoint2.Zero), "The wildlife is spared.");
            });
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task GlowrootIrradiatesWhoeverIsClose()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var (plant, mobs) = await Rig(pair, "WFVerdaPlantGlowroot", "MobHuman", 1f, 5f);
        await pair.RunSeconds(2.5f);

        await server.WaitAssertion(() =>
        {
            Assert.That(Damage(pair, mobs[0], "Radiation"), Is.GreaterThan(FixedPoint2.Zero), "Someone beside it should be irradiated.");
            Assert.That(Damage(pair, mobs[1], "Radiation"), Is.EqualTo(FixedPoint2.Zero), "Someone five tiles off is not.");
            Assert.That(server.EntMan.GetComponent<WFReactiveFloraComponent>(plant).Behaviour, Is.EqualTo(WFReactiveFloraBehaviour.Irradiate));
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>Each plant gives its produce once and then nothing until it regrows; the bramble wants a knife.</summary>
    [TestCase("WFVerdaPlantMushroom", "WFVerdaMushroom")]
    [TestCase("WFVerdaPlantBerryBush", "WFVerdaDuskberries")]
    [TestCase("WFVerdaPlantReeds", "WFPlantFibre")]
    [TestCase("WFVerdaPlantShoreSage", "WFVerdaShoreSage")]
    [TestCase("WFVerdaPlantLotus", "WFVerdaLotusBlossom")]
    [TestCase("WFVerdaPlantBramble", "WFVerdaBrambleThorn")]
    [TestCase("WFVerdaPlantGlowroot", "WFVerdaGlowroot")]
    public async Task PlantsGiveTheirProduce(string plant, string produce)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid uid = default;
        await server.WaitPost(() => uid = entMan.SpawnEntity(plant, new EntityCoordinates(map.Grid.Owner, 0.5f, 0.5f)));

        int Count() => entMan.EntityQuery<MetaDataComponent>().Count(meta => meta.EntityPrototype?.ID == produce);
        await server.WaitAssertion(() =>
        {
            var harvest = server.System<WFRegrowingHarvestSystem>();
            var comp = entMan.GetComponent<WFRegrowingHarvestComponent>(uid);
            Assert.That(comp.Harvested, Is.False, "It starts ripe.");
            harvest.Harvest((uid, comp), null);
            Assert.That(Count(), Is.GreaterThan(0), "Picking should give the produce.");
            var first = Count();
            harvest.Harvest((uid, comp), null);
            Assert.That(Count(), Is.EqualTo(first), "A bare plant gives nothing.");
            Assert.That(plant == "WFVerdaPlantBramble", Is.EqualTo(comp.Tool != null), "Only the bramble needs a tool.");
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>Forest mushrooms have their own soup; lotus, sage and glowroot carry what they are for.</summary>
    [Test]
    public async Task ProduceCooksAndHeals()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        await server.WaitAssertion(() =>
        {
            var protos = server.ProtoMan;
            var recipe = protos.Index<FoodRecipePrototype>("RecipeWFVerdaMushroomSoup");
            Assert.That(recipe.Result, Is.EqualTo("WFFoodSoupVerdaMushroom"));
            Assert.That(recipe.IngredientsSolids.GetValueOrDefault("WFVerdaMushroom"), Is.EqualTo(FixedPoint2.New(2)));
            Assert.That(protos.Index<FoodRecipePrototype>("RecipeWFWildMushroomSoup").IngredientsSolids.ContainsKey("WFVerdaMushroom"),
                Is.False, "The generic wild mushroom soup must not take the forest mushroom's place.");

            var solutions = server.System<SharedSolutionContainerSystem>();
            foreach (var (id, reagent) in new[] { ("WFVerdaLotusBlossom", "Tricordrazine"), ("WFVerdaShoreSage", "Bicaridine"),
                         ("WFVerdaGlowroot", "Uranium"), ("WFFoodSoupVerdaMushroom", "Nutriment") })
            {
                var uid = entMan.SpawnEntity(id, new EntityCoordinates(map.Grid.Owner, 0.5f, 0.5f));
                Assert.That(solutions.TryGetSolution(uid, "food", out _, out var food), Is.True, $"{id} has no food solution.");
                Assert.That(food!.GetTotalPrototypeQuantity(reagent), Is.GreaterThan(FixedPoint2.Zero), $"{id} holds no {reagent}.");
            }
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>A mutated deer breaks into a run of hops when someone with hands gets close.</summary>
    [Test]
    public async Task DeerBoltsFromSomeoneClose()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        await server.WaitPost(() => server.CfgMan.SetCVar(Content.Shared.CCVar.CCVars.NPCPauseWhenNoPlayersInRange, false));
        var map = await pair.CreateTestMap();
        var maps = server.System<SharedMapSystem>();
        EntityUid deer = default;
        EntityUid person = default;
        await server.WaitPost(() =>
        {
            var plating = server.ResolveDependency<ITileDefinitionManager>()["Plating"].TileId;
            for (var x = -30; x <= 30; x++)
            for (var y = -12; y <= 12; y++)
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), new Tile(plating));
            person = entMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid.Owner, 0.5f, 0.5f));
            deer = entMan.SpawnEntity("WFMobVerdaDeer", new EntityCoordinates(map.Grid.Owner, 4.5f, 0.5f));
        });
        var xforms = server.System<SharedTransformSystem>();
        var start = Vector2.Zero;
        await server.WaitPost(() => start = xforms.GetWorldPosition(deer));
        var log = new System.Text.StringBuilder();
        await pair.RunSeconds(0.5f);
        await server.WaitPost(() =>
        {
            var board = entMan.GetComponent<Content.Server.NPC.HTN.HTNComponent>(deer).Blackboard;
            var found = server.System<Content.Server.NPC.Systems.NPCUtilitySystem>().GetEntities(board, "WFVerdaNearbyThreats").GetHighest();
            log.Append($"\n  threat query finds {(found.IsValid() ? entMan.ToPrettyString(found).ToString() : "nobody")}; startle range {board.GetValueOrDefault<float>("WFVerdaStartleRange", entMan)}");
        });
        var farthest = 0f;
        for (var second = 1; second <= 8; second++)
        {
            await pair.RunSeconds(1);
            await server.WaitPost(() =>
            {
                var moved = Vector2.Distance(start, xforms.GetWorldPosition(deer));
                farthest = MathF.Max(farthest, moved);
                var active = entMan.HasComponent<Content.Shared.NPC.ActiveNPCComponent>(deer);
                var htn = entMan.GetComponent<Content.Server.NPC.HTN.HTNComponent>(deer);
                var task = (htn.Plan?.CurrentTask.Operator.GetType().Name ?? "none") + " branches " + string.Join("/", htn.Plan?.BranchTraversalRecord ?? new List<int>());
                var key = htn.Blackboard.TryGetValue<EntityUid>("Target", out var target, entMan) ? entMan.ToPrettyString(target).ToString() : "no target";
                log.Append($"\n  {second} s: moved {moved:F1}, {Vector2.Distance(xforms.GetWorldPosition(person), xforms.GetWorldPosition(deer)):F1} from the person, active {active}, task {task}, {key}");
            });
        }

        await server.WaitAssertion(() =>
        {
            TestContext.Out.WriteLine($"Deer run:{log}");
            // Hops are at least 2.5 tiles in a random direction, so a slow box may see only one or two.
            Assert.That(farthest, Is.GreaterThan(2), "The deer should have dashed away from where it stood.");
        });
        await pair.CleanReturnAsync();
    }
}
