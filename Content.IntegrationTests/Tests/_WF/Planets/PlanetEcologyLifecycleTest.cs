using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._WF.Planets;
using Content.Server.Chemistry.TileReactions;
using Content.Shared._WF.Planets;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Maps;
using Content.Shared.Mobs.Components;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.Planets;

[TestFixture]
public sealed class PlanetEcologyLifecycleTest
{
    [Test]
    public async Task AssimilationSacksShareFaunaCapsAndStopWhenDestroyed()
    {
        await using var pair = await PoolManager.GetServerClient();
        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var server = pair.Server;
        var em = server.EntMan;
        EntityUid sack = default;
        var offspring = new List<EntityUid>();
        var ground = layers[0];
        await server.WaitAssertion(() =>
        {
            var maps = server.System<SharedMapSystem>();
            var grid = em.GetComponent<MapGridComponent>(ground);
            var tile = server.ResolveDependency<ITileDefinitionManager>()["WFFloorFlesh"].TileId;
            for (var x = -5; x <= 5; x++)
            for (var y = -5; y <= 5; y++)
                maps.SetTile(ground, grid, new Vector2i(x, y), new Tile(tile));
            sack = em.CreateEntityUninitialized("WFCarcinomaAssimilationSack", new EntityCoordinates(ground, new Vector2(0.5f)));
            var spawner = em.GetComponent<WFPlanetFaunaSpawnerComponent>(sack);
            Assert.That(spawner.SpawnDelay, Is.EqualTo(240));
            // Accelerate only this fixture; production nests wait four minutes between attempts.
            spawner.SpawnDelay = 0;
            em.InitializeAndStartEntity(sack);
            Assert.That(em.HasComponent<Content.Server.Spawners.Components.TimedSpawnerComponent>(sack), Is.False);
            var forest = em.SpawnEntity("WFCarcinomaTendons", new EntityCoordinates(ground, new Vector2(4.5f)));
            Assert.That(em.HasComponent<Content.Server.Spreader.KudzuComponent>(forest), Is.False);
            var viewers = new[] { new EntityCoordinates(ground, new Vector2(10, 0)) };
            for (var i = 0; i < 12; i++) server.System<WFPlanetFaunaSystem>().RefreshPopulation(viewers);
            var query = em.EntityQueryEnumerator<WFPlanetWildlifeComponent>();
            while (query.MoveNext(out var uid, out var wild))
                if (wild.Ground == ground) offspring.Add(uid);
            Assert.That(offspring.Count, Is.EqualTo(WFPlanetFaunaSystem.MaxNearby));
            Assert.That(em.EntityExists(sack), Is.True, "Registering a persistent nest must not delete it.");
            em.DeleteEntity(sack);
            foreach (var uid in offspring) em.DeleteEntity(uid);
            server.System<WFPlanetFaunaSystem>().RefreshPopulation(viewers);
            var remaining = em.EntityQueryEnumerator<WFPlanetWildlifeComponent>();
            while (remaining.MoveNext(out _, out var wild))
                Assert.That(wild.Ground, Is.Not.EqualTo(ground), "Destroyed sack still spawned wildlife.");
        });
        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ExplorationReplenishesDistantWildlifeButPreservesTouchedAnimals()
    {
        await using var pair = await PoolManager.GetServerClient();
        await EnableFeature(pair);
        var server = pair.Server;
        var em = server.EntMan;
        var layers = new List<EntityUid>();
        EntityUid retired = default, injured = default, captured = default;
        await server.WaitAssertion(() =>
        {
            var proto = server.ResolveDependency<IPrototypeManager>();
            var surfaceId = "WFSurfaceAsclepiu";
            var network = server.System<WFPlanetNetworkSystem>().BuildNetwork(proto.Index<WFPlanetSurfacePrototype>(surfaceId), Vector2.Zero, "test", null)!.Value;
            layers.AddRange(em.GetComponent<WFPlanetNetworkComponent>(network).Layers);
            var ground = layers[0];
            var maps = server.System<SharedMapSystem>();
            var grid = em.GetComponent<MapGridComponent>(ground);
            var tileId = server.ResolveDependency<ITileDefinitionManager>()["FloorFlesh"].TileId;
            // Dense arrival area plus distant terrain: only nearby sites can admit animals.
            foreach (var x in new[] { 0, 8, 16, 24, 32, 40, 48, 1000 })
            {
                maps.SetTile(ground, grid, new Vector2i(x, 0), new Tile(tileId));
                em.SpawnEntity("WFFaunaAsclepiu", new EntityCoordinates(ground, new Vector2(x, 0)));
            }
            var ecology = server.System<WFPlanetFaunaSystem>();
            var viewer = new[] { new EntityCoordinates(ground, new Vector2(20, 40)) };
            for (var i = 0; i < 4; i++)
                ecology.RefreshPopulation(viewer);
            var animals = new List<EntityUid>();
            var query = em.EntityQueryEnumerator<WFPlanetWildlifeComponent>();
            while (query.MoveNext(out var uid, out var wildlife))
                if (wildlife.Ground == ground)
                    animals.Add(uid);
            Assert.That(animals.Count, Is.InRange(3, 8));
            foreach (var uid in animals)
                Assert.That(em.GetComponent<TransformComponent>(uid).LocalPosition.X, Is.LessThan(100));
            retired = animals[0]; injured = animals[1]; captured = animals[2];
            var damage = new DamageSpecifier();
            damage.DamageDict.Add("Blunt", FixedPoint2.New(1));
            server.System<DamageableSystem>().TryChangeDamage(injured, damage);
            server.System<SharedTransformSystem>().SetCoordinates(captured, new EntityCoordinates(layers[^1], Vector2.Zero));
            foreach (var uid in animals)
                em.GetComponent<WFPlanetWildlifeComponent>(uid).LastNearby = server.ResolveDependency<IGameTiming>().CurTime - WFPlanetFaunaSystem.RetirementDelay;
            ecology.RefreshPopulation(new[] { new EntityCoordinates(ground, new Vector2(1030, 0)) });
            Assert.That(em.GetComponent<WFPlanetWildlifeComponent>(injured).Protected, Is.True);
            Assert.That(em.GetComponent<WFPlanetWildlifeComponent>(captured).Protected, Is.True);
            var found = false;
            var next = em.EntityQueryEnumerator<WFPlanetWildlifeComponent, TransformComponent>();
            while (next.MoveNext(out _, out var wild, out var transform))
                if (wild.Ground == ground && transform.ParentUid == ground && transform.LocalPosition.X > 900)
                    found = true;
            Assert.That(found, Is.True, "Exploring new terrain did not produce an encounter.");
        });
        await server.WaitRunTicks(2);
        await server.WaitAssertion(() =>
        {
            Assert.That(em.EntityExists(retired), Is.False);
            Assert.That(em.EntityExists(injured), Is.True);
            Assert.That(em.EntityExists(captured), Is.True);
        });
        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ChimeraBloodCannotSeedPlanetsOrDetachedTerrainButStillWorksElsewhere()
    {
        await using var pair = await PoolManager.GetServerClient();
        await EnableFeature(pair);
        var server = pair.Server;
        var em = server.EntMan;
        var layers = new List<EntityUid>();
        EntityUid refused = default, ordinary = default;
        await server.WaitAssertion(() =>
        {
            var proto = server.ResolveDependency<IPrototypeManager>();
            var surfaceId = "WFSurfaceCarcinoma";
            var network = server.System<WFPlanetNetworkSystem>().BuildNetwork(proto.Index<WFPlanetSurfacePrototype>(surfaceId), Vector2.Zero, "test", null)!.Value;
            layers.AddRange(em.GetComponent<WFPlanetNetworkComponent>(network).Layers);
            var ground = layers[0];
            var map = server.System<SharedMapSystem>();
            var grid = em.GetComponent<MapGridComponent>(ground);
            var tileId = server.ResolveDependency<ITileDefinitionManager>()["FloorFlesh"].TileId;
            map.SetTile(ground, grid, Vector2i.Zero, new Tile(tileId));
            var reaction = new CreateEntityTileReaction { Entity = "ChimeraFleshKudzu" };
            var reagentId = "NaturalLetoferol";
            var used = reaction.TileReact(map.GetTileRef(ground, grid, Vector2i.Zero), proto.Index<ReagentPrototype>(reagentId), FixedPoint2.New(100), em, null);
            Assert.That(used, Is.EqualTo(FixedPoint2.Zero));
            refused = em.SpawnEntity("ChimeraFleshKudzu", new EntityCoordinates(ground, Vector2.Zero));
            // Plain space map has no WF planet marker. Original chimera gameplay is unchanged there.
            var ordinaryMap = map.CreateMap();
            layers.Add(ordinaryMap);
            ordinary = em.SpawnEntity("ChimeraFleshKudzu", new EntityCoordinates(ordinaryMap, Vector2.Zero));
            var terrain = em.SpawnEntity(null, new EntityCoordinates(ordinaryMap, Vector2.Zero));
            em.AddComponent<WFDetachedTerrainComponent>(terrain);
            Assert.That(server.System<WFPlanetBiomassSystem>().IsPlanet(terrain), Is.True);
            em.DeleteEntity(terrain);
        });
        await server.WaitRunTicks(2);
        await server.WaitAssertion(() =>
        {
            Assert.That(em.EntityExists(refused), Is.False);
            Assert.That(em.EntityExists(ordinary), Is.True);
        });
        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }
}
