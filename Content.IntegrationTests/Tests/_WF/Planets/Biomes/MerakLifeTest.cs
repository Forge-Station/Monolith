#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._WF.Planets;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Parallax;
using Content.Server._WF.LivingWorld;
using Content.Shared._WF.LivingWorld;
using Content.Shared._WF.Planets;
using Content.Shared.Atmos;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.EntityTable;
using Content.Shared.FixedPoint;
using Content.Shared.Maps;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Components;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.Parallax.Biomes;
using Content.Shared.Stunnable;
using Content.Shared.Weapons.Melee;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.Planets.Biomes;

/// <summary>Merak's regional wildlife and living plants sit on their own ground, stay sparse, and do what they say.</summary>
[TestFixture]
public sealed class MerakLifeTest
{
    private const string Surface = "WFSurfaceMerak";

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

    private static readonly string[] OasisTiles = { "WFFloorMerakOasisDirt", "WFFloorMerakOasisGrass" };

    private static readonly string[] OpenGround = DuneTiles.Concat(new[] { "WFFloorMerakRedDesertSand", "WFFloorMerakDesertGravel" }).ToArray();

    private static readonly string[] Tables =
    {
        "WFFaunaMerakDunes", "WFFaunaMerakBadlands", "WFFaunaMerakCactus", "WFFaunaMerakFlats", "WFFaunaMerakOasis", "WFFaunaMerakMimic",
    };

    private static readonly string[] Mobs =
    {
        "WFMobDesertTarantula", "MobLizard", "MobCobraSpace", "MobKangarooPlanet", "MobIronCrab", "MobQuartzCrab", "MobGoat", "MobCrab",
        "WFMobCrateMimic", "WFMobBirdGlassSwift",
    };

    private static readonly string[] Plants =
    {
        "WFMerakBarrelCactus", "WFMerakAgave", "WFMerakStingNettle", "WFMerakDatePalm", "WFMerakTumbleweed", "WFMerakSpittingCactus",
    };

    /// <summary>Where each placed prototype may stand, and the most of all sampled tiles it may take.</summary>
    private static readonly (string Id, string[] Tiles, double MaxShare)[] Placed =
    {
        ("WFFaunaMerakDunes", DuneTiles, 6),
        ("WFFaunaMerakBadlands", BadlandTiles, 6),
        ("WFFaunaMerakCactus", DuneTiles, 6),
        ("WFFaunaMerakFlats", FlatTiles, 6),
        ("WFFaunaMerakOasis", OasisTiles, 6),
        ("WFFaunaMerakMimic", OasisTiles, 0.05),
        ("WFMerakBarrelCactus", DuneTiles, 3),
        ("WFMerakAgave", DuneTiles, 3),
        ("WFMerakStingNettle", DuneTiles, 0.5),
        ("WFMerakDatePalm", OasisTiles, 0.5),
        ("WFMerakTumbleweed", OpenGround, 1.5),
        ("WFMerakSpittingCactus", BadlandTiles, 0.3),
    };

    /// <summary>A plating floor with the plant at the centre and a human at each distance along +x.</summary>
    private static async Task<(EntityUid Plant, EntityUid[] Mobs)> Rig(TestPair pair, string plant, params float[] distances)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var maps = server.System<SharedMapSystem>();
        EntityUid plantUid = default;
        var mobs = new EntityUid[distances.Length];
        await server.WaitPost(() =>
        {
            var air = new GasMixture(Atmospherics.CellVolume) { Temperature = Atmospherics.T20C };
            air.AdjustMoles(Gas.Oxygen, Atmospherics.OxygenMolesStandard);
            air.AdjustMoles(Gas.Nitrogen, Atmospherics.NitrogenMolesStandard);
            server.System<AtmosphereSystem>().SetMapAtmosphere(map.MapUid, false, air);
            var plating = server.ResolveDependency<ITileDefinitionManager>()["Plating"].TileId;
            for (var x = -8; x <= 8; x++)
            for (var y = -8; y <= 8; y++)
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), new Tile(plating));
            plantUid = entMan.SpawnEntity(plant, new EntityCoordinates(map.Grid.Owner, 0.5f, 0.5f));
            for (var i = 0; i < distances.Length; i++)
                mobs[i] = entMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid.Owner, 0.5f + distances[i], 0.5f));
        });
        return (plantUid, mobs);
    }

    private static FixedPoint2 Damage(TestPair pair, EntityUid uid, string type)
    {
        return pair.Server.EntMan.GetComponent<DamageableComponent>(uid).Damage.DamageDict.GetValueOrDefault(type);
    }

    [Test]
    public async Task EveryNewPrototypeSpawnsAndTheTablesDeliverTheirAnimals()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        await server.WaitAssertion(() =>
        {
            var protos = server.ProtoMan;
            var tables = server.System<EntityTableSystem>();
            var seen = new HashSet<string>();
            foreach (var table in Tables)
            {
                var spawns = new HashSet<string>();
                for (var i = 0; i < 400; i++)
                    foreach (var id in tables.GetSpawns(protos.Index<EntityTablePrototype>(table).Table))
                        spawns.Add(id);
                TestContext.Out.WriteLine($"{table}: {string.Join(", ", spawns)}");
                Assert.That(spawns, Is.Not.Empty, $"{table} never yields an animal.");
                seen.UnionWith(spawns);
                foreach (var id in spawns)
                    Assert.That(protos.Index<EntityPrototype>(id).Components.ContainsKey("MobState"), Is.True, $"{id} is not a mob; the fauna system deletes it.");
                Assert.That(protos.HasIndex<EntityPrototype>(table), Is.True, $"No spawner entity named like the table {table}.");
            }
            Assert.That(seen, Is.EquivalentTo(Mobs), "The region tables should hold exactly Merak's animals.");

            foreach (var id in Mobs.Concat(Plants).Concat(new[] { "WFCactusWater", "WFAgaveSap", "WFMerakDates", "WFMerakCactusSpine" }))
                entMan.DeleteEntity(entMan.SpawnEntity(id, new EntityCoordinates(map.Grid.Owner, 0.5f, 0.5f)));

            var tarantula = protos.Index<EntityPrototype>("WFMobDesertTarantula");
            var spider = protos.Index<EntityPrototype>("MobSpiderSpace");
            var (tarSpeed, spiderSpeed) = (Walk(tarantula), Walk(spider));
            var (tarHit, spiderHit) = (Hit(tarantula), Hit(spider));
            Assert.That(tarSpeed, Is.LessThan(spiderSpeed), "The ambusher is slower than the space spider.");
            Assert.That(tarHit, Is.GreaterThan(spiderHit), "The ambusher hits harder than the space spider.");
        });

        // Wildlife must not fight itself.
        var mobs = new List<EntityUid>();
        await server.WaitPost(() =>
        {
            foreach (var id in Mobs)
                mobs.Add(entMan.SpawnEntity(id, new EntityCoordinates(map.Grid.Owner, 0.5f, 0.5f)));
        });
        await server.WaitAssertion(() =>
        {
            var factions = server.System<NpcFactionSystem>();
            var clashes = new List<string>();
            foreach (var a in mobs)
            foreach (var b in mobs)
            {
                var (fa, fb) = (entMan.GetComponent<NpcFactionMemberComponent>(a).Factions, entMan.GetComponent<NpcFactionMemberComponent>(b).Factions);
                foreach (var x in fa)
                foreach (var y in fb)
                    if (factions.IsFactionHostile(x, y))
                        clashes.Add($"{entMan.GetComponent<MetaDataComponent>(a).EntityPrototype!.ID} ({x}) vs {entMan.GetComponent<MetaDataComponent>(b).EntityPrototype!.ID} ({y})");
            }
            Assert.That(clashes, Is.Empty, "Merak's animals fight each other.");
        });
        await pair.CleanReturnAsync();

        float Walk(EntityPrototype proto) => proto.Components.TryGetComponent("MovementSpeedModifier", out var comp)
            ? ((MovementSpeedModifierComponent) comp).BaseWalkSpeed
            : 0f;
        float Hit(EntityPrototype proto) => proto.Components.TryGetComponent("MeleeWeapon", out var comp)
            ? ((MeleeWeaponComponent) comp).Damage.GetTotal().Float()
            : 0f;
    }

    [Test]
    public async Task NettleStingsAWalkerButNotAnAnimal()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var (_, mobs) = await Rig(pair, "WFMerakStingNettle", 0.8f, 5f);
        EntityUid goat = default;
        await server.WaitPost(() => goat = entMan.SpawnEntity("MobGoat", new EntityCoordinates(entMan.GetComponent<TransformComponent>(mobs[0]).ParentUid, 1f, 0.5f)));
        await pair.RunSeconds(1.5f);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(Damage(pair, mobs[0], "Poison"), Is.GreaterThan(FixedPoint2.Zero), "The nettle stings whoever walks past.");
                Assert.That(entMan.HasComponent<StunnedComponent>(mobs[0]), Is.True, "The sting stuns for a moment.");
                Assert.That(Damage(pair, mobs[1], "Poison"), Is.EqualTo(FixedPoint2.Zero), "Out of range.");
                Assert.That(Damage(pair, goat, "Poison"), Is.EqualTo(FixedPoint2.Zero), "Wildlife is left alone.");
            });
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SpittingCactusHitsAWalkerInSight()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var (_, mobs) = await Rig(pair, "WFMerakSpittingCactus", 3f, 7.5f);
        await pair.RunSeconds(2f);

        await server.WaitAssertion(() =>
        {
            Assert.That(Damage(pair, mobs[0], "Piercing"), Is.GreaterThan(FixedPoint2.Zero), "The needle should hit the mob in range.");
            Assert.That(Damage(pair, mobs[1], "Piercing"), Is.EqualTo(FixedPoint2.Zero), "Beyond its range.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PickedPlantsGiveWaterSapAndDates()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var plants = new Dictionary<string, EntityUid>();
        await server.WaitPost(() =>
        {
            foreach (var id in new[] { "WFMerakBarrelCactus", "WFMerakAgave", "WFMerakDatePalm" })
                plants[id] = entMan.SpawnEntity(id, new EntityCoordinates(map.Grid.Owner, 0.5f, 0.5f));
        });

        int Count(string id) => entMan.EntityQuery<MetaDataComponent>().Count(meta => meta.EntityPrototype?.ID == id);
        await server.WaitAssertion(() =>
        {
            var harvest = server.System<WFRegrowingHarvestSystem>();
            foreach (var (_, plant) in plants)
                harvest.Harvest((plant, entMan.GetComponent<WFRegrowingHarvestComponent>(plant)), null);

            Assert.That(Count("WFCactusWater"), Is.EqualTo(1), "A barrel cactus gives one cup of water.");
            Assert.That(Count("WFAgaveSap"), Is.InRange(1, 2));
            Assert.That(Count("WFMerakDates"), Is.InRange(2, 4));

            var solutions = server.System<SharedSolutionContainerSystem>();
            var cup = entMan.EntityQuery<MetaDataComponent>().First(meta => meta.EntityPrototype?.ID == "WFCactusWater").Owner;
            Assert.That(solutions.TryGetSolution(cup, "drink", out _, out var drink), Is.True);
            Assert.That(drink!.GetTotalPrototypeQuantity("Water"), Is.GreaterThan(FixedPoint2.Zero), "The cup holds water.");

            var sap = entMan.EntityQuery<MetaDataComponent>().First(meta => meta.EntityPrototype?.ID == "WFAgaveSap").Owner;
            Assert.That(solutions.TryGetSolution(sap, "food", out _, out var food), Is.True);
            Assert.That(food!.GetTotalPrototypeQuantity("Sugar"), Is.GreaterThan(FixedPoint2.Zero), "The sap is sugar.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task LifeSitsOnItsOwnRegionStaysSparseAndMimicsAreRare()
    {
        await using var pair = await PoolManager.GetServerClient();
        await PlanetFixture.EnableFeature(pair);
        var server = pair.Server;
        var entities = server.EntMan;
        var layers = new List<EntityUid>();
        await server.WaitAssertion(() =>
        {
            var protos = server.ProtoMan;
            var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
            var surface = protos.Index<WFPlanetSurfacePrototype>(Surface);
            var network = server.System<WFPlanetNetworkSystem>().BuildNetwork(surface, Vector2.Zero, Surface, null);
            Assert.That(network, Is.Not.Null);
            layers.AddRange(entities.GetComponent<WFPlanetNetworkComponent>(network!.Value).Layers);
            var biome = new Entity<BiomeComponent>(layers[0], entities.GetComponent<BiomeComponent>(layers[0]));
            var biomes = server.System<BiomeSystem>();
            var radius = surface.Radius;

            var sampled = 0;
            var counts = new Dictionary<string, int>();
            var grounds = new Dictionary<string, HashSet<string>>();
            var crabs = new List<Vector2i>();
            var ore = new HashSet<Vector2i>();
            var water = new HashSet<Vector2i>();
            for (var x = -radius; x < radius; x++)
            for (var y = -radius; y < radius; y++)
            {
                if (x * x + y * y > radius * radius)
                    continue;
                var index = new Vector2i(x, y);
                if (biomes.WfIsWater(biome, index))
                    water.Add(index);
                sampled++;
                if (!biomes.TryGetBiomeTile(index, biome.Comp.Layers, biome.Comp.Seed, (Entity<MapGridComponent>?) null, out var tile))
                    continue;
                if (!biomes.TryGetEntity(index, biome.Comp.Layers, tile.Value, biome.Comp.Seed, (Entity<MapGridComponent>?) null, out var feature))
                    continue;
                counts[feature] = counts.GetValueOrDefault(feature) + 1;
                if (!grounds.TryGetValue(feature, out var on))
                    grounds[feature] = on = new HashSet<string>();
                on.Add(((ContentTileDefinition) tileDefs[tile.Value.TypeId]).ID);
                if (feature == "WFFaunaMerakFlats")
                    crabs.Add(index);
                else if (feature == "MonoPlanetmapOreSandRich")
                    ore.Add(index);
            }

            using (Assert.EnterMultipleScope())
            {
                foreach (var (id, allowed, max) in Placed)
                {
                    var count = counts.GetValueOrDefault(id);
                    var share = 100.0 * count / sampled;
                    TestContext.Out.WriteLine($"  {id}: {count} ({share:F3}%) on {string.Join(", ", grounds.GetValueOrDefault(id) ?? new HashSet<string>())}");
                    Assert.That(count, Is.GreaterThan(0), $"{id} never appears.");
                    Assert.That(grounds.GetValueOrDefault(id) ?? new HashSet<string>(), Is.SubsetOf(allowed), $"{id} grows outside its region.");
                    Assert.That(share, Is.LessThan(max), $"{id} is too common.");
                }

                var wf = counts.Where(c => c.Key.StartsWith("WFMerak") && c.Key != "WFMerakDatePalm").Sum(c => c.Value);
                TestContext.Out.WriteLine($"  living plants: {wf} of {sampled}");

                // Crab sites cluster round the outcrops: most are within a few tiles of ore.
                var cells = new HashSet<Vector2i>(ore.Select(o => new Vector2i(o.X >> 3, o.Y >> 3)));
                var near = crabs.Count(c =>
                {
                    for (var dx = -1; dx <= 1; dx++)
                    for (var dy = -1; dy <= 1; dy++)
                        if (cells.Contains(new Vector2i((c.X >> 3) + dx, (c.Y >> 3) + dy)))
                            return true;
                    return false;
                });
                TestContext.Out.WriteLine($"  crab sites {crabs.Count}, within about 8 tiles of ore {near}, ore markers {ore.Count}");
                Assert.That(near, Is.GreaterThan(crabs.Count / 2), "Crab sites should sit near the ore outcrops.");

                // A handful of oases, and no more mimic sites than oases.
                var oases = 0;
                var unvisited = new HashSet<Vector2i>(water);
                while (unvisited.Count > 0)
                {
                    var start = unvisited.First();
                    var pending = new Queue<Vector2i>();
                    pending.Enqueue(start);
                    unvisited.Remove(start);
                    var size = 0;
                    while (pending.TryDequeue(out var t))
                    {
                        size++;
                        foreach (var step in new[] { Vector2i.Up, Vector2i.Down, Vector2i.Left, Vector2i.Right })
                            if (unvisited.Remove(t + step))
                                pending.Enqueue(t + step);
                    }
                    if (size >= 20)
                        oases++;
                }
                var mimics = counts.GetValueOrDefault("WFFaunaMerakMimic");
                TestContext.Out.WriteLine($"  oases {oases}, mimic sites {mimics}");
                Assert.That(mimics, Is.LessThanOrEqualTo(oases), "At most one mimic site per oasis.");
            }
        });
        await PlanetFixture.Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }
}
