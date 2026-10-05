#nullable enable
using System.Numerics;
using Robust.Shared.Maths;
using Content.Server._WF.Chimera;
using Content.Server.Nutrition.Components;
using Content.Server.Nutrition.EntitySystems;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Maps;
using Content.Shared.Throwing;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Chimera;

[TestFixture, NonParallelizable]
public sealed class FleshPustuleTreeTest
{
    [Test]
    public async Task TreeYieldsOnceAndItemPlantsOnce()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        await server.WaitAssertion(() =>
        {
            var maps = server.System<SharedMapSystem>();
            maps.CreateMap(out var mapId);
            var grid = server.ResolveDependency<IMapManager>().CreateGridEntity(mapId);
            var floor = server.ResolveDependency<ITileDefinitionManager>()["FloorFlesh"];
            for (var x = -2; x <= 3; x++)
            for (var y = -2; y <= 3; y++)
                maps.SetTile(grid, new Vector2i(x, y), new Tile(floor.TileId));
            var user = em.SpawnEntity("MobHuman", new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
            var tree = em.SpawnEntity("WFFleshPustuleTree", new EntityCoordinates(grid, new Vector2(1.5f, 0.5f)));
            var comp = em.GetComponent<WFFleshPustuleTreeComponent>(tree);
            var system = server.System<WFFleshPustuleTreeSystem>();
            Assert.That(system.TryHarvest((tree, comp), user, out var harvested), Is.True);
            Assert.That(comp.Harvested, Is.True);
            Assert.That(system.TryHarvest((tree, comp), user, out _), Is.False);
            var item = harvested!.Value;
            Assert.That(em.HasComponent<FoodComponent>(item), Is.True);
            Assert.That(server.System<SharedSolutionContainerSystem>().TryGetSolution(item, "food", out _, out var solution), Is.True);
            Assert.That(solution!.ContainsReagent("NaturalLetoferol", null), Is.True);
            var itemComp = em.GetComponent<WFFleshPustuleItemComponent>(item);
            Assert.That(system.BurstItem((item, itemComp)), Is.False, "Dropping or harvesting must not arm it.");
            Assert.That(system.TryPlantItem((item, itemComp), user, new EntityCoordinates(grid, new Vector2(9, 9))), Is.False);
            var target = new EntityCoordinates(grid, new Vector2(0.5f, 1.5f));
            Assert.That(system.TryPlantItem((item, itemComp), user, target), Is.True);
            Assert.That(system.TryPlantItem((item, itemComp), user, target), Is.False);
            var nests = 0;
            foreach (var entity in maps.GetAnchoredEntities(grid, grid.Comp, new Vector2i(0, 1)))
                if (em.HasComponent<WFFleshPustuleComponent>(entity)) nests++;
            Assert.That(nests, Is.EqualTo(1));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ThrownPustuleBurstsOnlyOnceAndRespectsTickCap()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        await server.WaitAssertion(() =>
        {
            var maps = server.System<SharedMapSystem>();
            maps.CreateMap(out var mapId);
            var grid = server.ResolveDependency<IMapManager>().CreateGridEntity(mapId);
            maps.SetTile(grid, Vector2i.Zero, new Tile(server.ResolveDependency<ITileDefinitionManager>()["FloorFlesh"].TileId));
            var coords = new EntityCoordinates(grid, new Vector2(0.5f));
            var system = server.System<WFFleshPustuleTreeSystem>();
            for (var i = 0; i < 10; i++)
            {
                var item = em.SpawnEntity("WFFleshPustuleItem", coords);
                var comp = em.GetComponent<WFFleshPustuleItemComponent>(item);
                var thrown = new ThrownEvent(null, item);
                em.EventBus.RaiseLocalEvent(item, ref thrown);
                var land = new LandEvent(null, false);
                em.EventBus.RaiseLocalEvent(item, ref land);
                Assert.That(comp.Consumed, Is.True);
                Assert.That(system.BurstItem((item, comp)), Is.False);
            }
            var ticks = 0;
            var query = em.EntityQueryEnumerator<WFFleshPustuleSpawnedTickComponent>();
            while (query.MoveNext(out _, out _)) ticks++;
            Assert.That(ticks, Is.EqualTo(WFFleshPustuleSystem.MaxTicksPerGround));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task HarvestedPustuleCanBeEatenNormally()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        EntityUid item = default;
        await server.WaitAssertion(() =>
        {
            server.System<SharedMapSystem>().CreateMap(out var mapId);
            var coords = new MapCoordinates(Vector2.Zero, mapId);
            var user = em.SpawnEntity("MobHuman", coords);
            item = em.SpawnEntity("WFFleshPustuleItem", coords);
            var food = em.GetComponent<FoodComponent>(item);
            Assert.That(server.System<FoodSystem>().TryFeed(user, user, item, food).Success, Is.True);
        });
        await pair.RunSeconds(4);
        await server.WaitAssertion(() => Assert.That(em.EntityExists(item), Is.False, "Eating must consume the pustule."));
        await pair.CleanReturnAsync();
    }
}
