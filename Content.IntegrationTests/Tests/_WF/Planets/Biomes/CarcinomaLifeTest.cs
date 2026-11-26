#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._WF.Planets;
using Content.Server.Body.Components;
using Content.Shared._WF.Planets;
using Content.Shared.EntityTable;
using Content.Shared.Maps;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Planets.Biomes;

/// <summary>Carcinoma's region wildlife and plants: where they grow, what they do, and what they give.</summary>
[TestFixture]
public sealed class CarcinomaLifeTest
{
    private static readonly string[] BoneTiles = { "WFFloorFleshBoneMeaty", "WFFloorFleshBoneSpine", "WFFloorFleshBoneStraight" };
    private static readonly string[] MarshTiles = { "WFFloorFleshMarshMud", "WFFloorFleshMarshDirt" };
    private static readonly string[] ShoreTiles = { "WFFloorFleshScab", "WFFloorFleshBoneMeaty" };
    private static readonly string[] FleshTiles = { "WFFloorFlesh", "FloorFlesh" };
    private static readonly string[] HiveWalls = { "GoonSetpiecesHive", "ChompResin", "PolarisResin" };

    private static readonly string[] Spawners =
    {
        "WFFaunaCarcinoma", "WFFaunaCarcinomaBone", "WFFaunaCarcinomaMarsh", "WFFaunaCarcinomaHive", "WFFaunaCarcinomaShore",
    };

    [Test]
    public async Task RegionsHoldTheirOwnLife()
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
            var sampled = 0;
            var tileCounts = new Dictionary<string, int>();
            var featureCounts = new Dictionary<string, int>();
            var at = new Dictionary<Vector2i, (string Tile, string Feature)>();
            for (var x = -radius; x < radius; x++)
            for (var y = -radius; y < radius; y++)
            {
                if (x * x + y * y > radius * radius)
                    continue;
                var pos = new Vector2i(x, y);
                generator.TryGetBiomeTile(pos, biome.Layers, biome.Seed, (Entity<MapGridComponent>?) null, out var tile);
                if (!tile.HasValue || tile.Value.IsEmpty)
                    continue;
                var tileId = ((ContentTileDefinition) tileDefs[tile.Value.TypeId]).ID;
                sampled++;
                tileCounts[tileId] = tileCounts.GetValueOrDefault(tileId) + 1;
                generator.TryGetEntity(pos, biome.Layers, tile.Value, biome.Seed, (Entity<MapGridComponent>?) null, out var feature);
                if (feature == null)
                    continue;
                featureCounts[feature] = featureCounts.GetValueOrDefault(feature) + 1;
                at[pos] = (tileId, feature);
            }

            int Count(string id) => featureCounts.GetValueOrDefault(id);
            int Tiles(IEnumerable<string> ids) => ids.Sum(id => tileCounts.GetValueOrDefault(id));
            double Percent(int count, int of) => 100.0 * count / of;
            var onTile = at.Where(p => true).ToList();

            void OnlyOn(string feature, string[] tiles)
            {
                var stray = onTile.Where(p => p.Value.Feature == feature && !tiles.Contains(p.Value.Tile)).ToList();
                Assert.That(stray, Is.Empty, $"{feature} grows outside its region, e.g. on {stray.FirstOrDefault().Value.Tile}.");
            }

            foreach (var id in Spawners.Concat(new[]
            {
                "WFCarcinomaVitaeBlister", "WFCarcinomaBonePile", "WFCarcinomaMeatFruitBush", "WFCarcinomaTendonVine",
                "WFCarcinomaWeepingPolyp",
            }))
            {
                TestContext.Progress.WriteLine($"  {id}: {Count(id)} ({Percent(Count(id), sampled):F3}% of {sampled} tiles)");
            }

            // Spawners and plants sit on their own region's ground.
            OnlyOn("WFFaunaCarcinomaBone", BoneTiles);
            OnlyOn("WFFaunaCarcinomaMarsh", MarshTiles);
            OnlyOn("WFFaunaCarcinomaShore", ShoreTiles);
            OnlyOn("WFCarcinomaBonePile", BoneTiles);
            OnlyOn("WFCarcinomaVitaeBlister", MarshTiles);
            OnlyOn("WFCarcinomaWeepingPolyp", ShoreTiles);
            OnlyOn("WFCarcinomaTendonVine", FleshTiles);
            OnlyOn("WFCarcinomaMeatFruitBush", FleshTiles);
            OnlyOn("WFFaunaCarcinomaHive", FleshTiles);

            var hive = at.Where(p => HiveWalls.Contains(p.Value.Feature)).Select(p => p.Key).ToHashSet();
            var spawnersNearHive = 0;
            foreach (var (pos, (_, feature)) in at)
            {
                if (feature != "WFFaunaCarcinomaHive")
                    continue;
                var near = false;
                for (var dx = -20; dx <= 20 && !near; dx++)
                for (var dy = -20; dy <= 20 && !near; dy++)
                    near = hive.Contains(new Vector2i(pos.X + dx, pos.Y + dy));
                if (near)
                    spawnersNearHive++;
            }
            TestContext.Progress.WriteLine($"  hive spawners within 20 tiles of a hive wall: {spawnersNearHive} of {Count("WFFaunaCarcinomaHive")}");
            Assert.That(Count("WFFaunaCarcinomaHive"), Is.GreaterThan(0), "Hive pockets have no spiders.");
            Assert.That(spawnersNearHive, Is.GreaterThanOrEqualTo(Count("WFFaunaCarcinomaHive") * 0.8), "Hive spiders spawn away from the hive.");

            // A vine takes a tendon's tile: a tendon, or another vine, stands next to every one.
            var vinesOnTendons = 0;
            foreach (var (pos, (_, feature)) in at)
            {
                if (feature != "WFCarcinomaTendonVine")
                    continue;
                var strand = false;
                for (var dx = -1; dx <= 1 && !strand; dx++)
                for (var dy = -1; dy <= 1 && !strand; dy++)
                    strand = (dx != 0 || dy != 0) && at.TryGetValue(new Vector2i(pos.X + dx, pos.Y + dy), out var other)
                        && other.Feature is "WFCarcinomaTendons" or "WFCarcinomaTendonVine";
                if (strand)
                    vinesOnTendons++;
            }
            TestContext.Progress.WriteLine($"  vines next to tendons: {vinesOnTendons} of {Count("WFCarcinomaTendonVine")}");
            Assert.That(vinesOnTendons, Is.GreaterThanOrEqualTo(Count("WFCarcinomaTendonVine") * 0.9),
                "Vines stand away from the tendons.");

            // Fruit bushes grow in the groves, among the trees.
            var trees = at.Where(p => p.Value.Feature is "WFFleshTree" or "WFFleshPustuleTree").Select(p => p.Key).ToList();
            var cells = new HashSet<Vector2i>(trees.Select(p => new Vector2i(p.X / 12, p.Y / 12)));
            var bushesByTrees = 0;
            foreach (var (pos, (_, feature)) in at)
            {
                if (feature != "WFCarcinomaMeatFruitBush")
                    continue;
                var cell = new Vector2i(pos.X / 12, pos.Y / 12);
                var near = false;
                for (var dx = -1; dx <= 1 && !near; dx++)
                for (var dy = -1; dy <= 1 && !near; dy++)
                    near = cells.Contains(new Vector2i(cell.X + dx, cell.Y + dy));
                if (near)
                    bushesByTrees++;
            }
            Assert.That(bushesByTrees, Is.GreaterThanOrEqualTo(Count("WFCarcinomaMeatFruitBush") * 0.9), "Fruit bushes grow away from the groves.");

            // Densities: reacting plants are rare, picking plants can be common, nothing floods a region.
            Assert.That(Count("WFCarcinomaTendonVine"), Is.GreaterThan(20), "No grasping tendons.");
            Assert.That(Percent(Count("WFCarcinomaTendonVine"), sampled), Is.InRange(0.03, 0.5));
            Assert.That(Count("WFCarcinomaWeepingPolyp"), Is.GreaterThan(5), "No weeping polyps.");
            Assert.That(Percent(Count("WFCarcinomaWeepingPolyp"), Tiles(ShoreTiles)), Is.InRange(0.1, 1.0));
            Assert.That(Count("WFCarcinomaBonePile"), Is.GreaterThan(10), "No bone piles.");
            Assert.That(Percent(Count("WFCarcinomaBonePile"), Tiles(BoneTiles)), Is.InRange(0.3, 3.0));
            Assert.That(Count("WFCarcinomaVitaeBlister"), Is.GreaterThan(5), "No vitae blisters.");
            Assert.That(Percent(Count("WFCarcinomaVitaeBlister"), Tiles(MarshTiles)), Is.InRange(0.1, 1.5));
            Assert.That(Count("WFCarcinomaMeatFruitBush"), Is.GreaterThan(10), "No meat-fruit bushes.");
            Assert.That(Percent(Count("WFCarcinomaMeatFruitBush"), sampled), Is.InRange(0.05, 3.0));

            // Wildlife: each region has its spawner, and the planet's total stays near the single-table days.
            var totalSites = Spawners.Sum(Count);
            TestContext.Progress.WriteLine($"  wildlife spawner sites: {totalSites} ({Percent(totalSites, sampled):F3}% of tiles)");
            // The single-table days placed 381 sites; the regions add about a tenth.
            Assert.That(totalSites, Is.InRange(330, 440), "The planet's wildlife density drifted from what it was.");
            foreach (var id in Spawners.Skip(1))
                Assert.That(Count(id), Is.GreaterThan(0), $"{id} never appears on the surface.");
        });
        await PlanetFixture.Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RegionTablesDealTheirOwnAnimalsAndTheUsualOnes()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        await server.WaitAssertion(() =>
        {
            var proto = server.ProtoMan;
            var tables = server.System<EntityTableSystem>();
            var usual = new HashSet<string>(Roll(tables, proto, "WFFaunaCarcinoma", 400));
            Assert.That(usual, Is.Not.Empty);
            foreach (var (table, own) in new Dictionary<string, string>
            {
                ["WFFaunaCarcinomaBone"] = "WFMobCarcinomaBurrower",
                ["WFFaunaCarcinomaHive"] = "WFMobCarcinomaSpider",
                ["WFFaunaCarcinomaShore"] = "WFMobCarcinomaSlurva",
            })
            {
                var dealt = new HashSet<string>(Roll(tables, proto, table, 600));
                Assert.That(dealt, Does.Contain(own), $"{table} never deals {own}.");
                Assert.That(dealt.Intersect(usual), Is.Not.Empty, $"{table} lost the planet's usual wildlife.");
                Assert.That(dealt.Where(id => proto.HasIndex<EntityPrototype>(id)).Count(), Is.EqualTo(dealt.Count));
            }

            var marsh = Roll(tables, proto, "WFFaunaCarcinomaMarsh", 600).ToList();
            var newborns = new HashSet<string>(Roll(tables, proto, "WFCarcinomaNewbornFlesh", 400));
            Assert.That(marsh.Count(id => newborns.Contains(id)), Is.GreaterThan(marsh.Count / 2), "The marsh should deal mostly flesh newborns.");
        });
        await pair.CleanReturnAsync();
    }

    private static IEnumerable<string> Roll(EntityTableSystem tables, IPrototypeManager proto, string table, int times)
    {
        var selector = proto.Index<EntityTablePrototype>(table).Table;
        for (var i = 0; i < times; i++)
        {
            foreach (var id in tables.GetSpawns(selector))
                yield return id.Id;
        }
    }

    [Test]
    public async Task NewMobsAreFleshAndLeaveFleshAlone()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var mobs = new List<EntityUid>();
        EntityUid aberrant = default, chimera = default;
        await server.WaitPost(() =>
        {
            aberrant = entMan.SpawnEntity("MobFleshJaredExpeditions", new EntityCoordinates(map.Grid.Owner, 3.5f, 0.5f));
            chimera = entMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid.Owner, 4.5f, 0.5f));
            server.System<NpcFactionSystem>().AddFaction(chimera, "Chimera");
            foreach (var id in new[] { "WFMobCarcinomaBurrower", "WFMobCarcinomaSpider", "WFMobCarcinomaSlurva" })
                mobs.Add(entMan.SpawnEntity(id, new EntityCoordinates(map.Grid.Owner, 0.5f, 0.5f)));
        });
        await pair.RunSeconds(0.5f);

        await server.WaitAssertion(() =>
        {
            var factions = server.System<NpcFactionSystem>();
            foreach (var mob in mobs)
            {
                var id = entMan.GetComponent<MetaDataComponent>(mob).EntityPrototype!.ID;
                Assert.That(entMan.GetComponent<NpcFactionMemberComponent>(mob).Factions.Select(f => f.Id).ToArray(),
                    Is.EqualTo(new[] { "AberrantFleshExpeditionNF" }), $"{id} should be of the flesh alone.");
                Assert.That(factions.IsEntityFriendly(mob, aberrant) && factions.IsEntityFriendly(aberrant, mob), Is.True, $"{id} fights the flesh.");
                Assert.That(factions.IsEntityFriendly(mob, chimera) && factions.IsEntityFriendly(chimera, mob), Is.True, $"{id} fights the chimeras.");
                Assert.That(entMan.GetComponent<BloodstreamComponent>(mob).BloodReagent.Id, Is.EqualTo("Blood"), $"{id} should bleed blood.");
            }
            Assert.That(entMan.HasComponent<Content.Server.NPC.HTN.HTNComponent>(mobs[1]), Is.True, "The hive spider should hunt.");
            var burrower = entMan.GetComponent<Content.Shared.Nutrition.Components.ButcherableComponent>(mobs[0]);
            Assert.That(burrower.SpawnedEntities.Select(e => e.PrototypeId), Does.Not.Contain("FoodMeatXeno"));
        });
        await pair.CleanReturnAsync();
    }
}
