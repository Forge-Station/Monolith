#nullable enable
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._WF.Chimera;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Fluids.Components;
using Content.Shared.Maps;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Chimera;

[TestFixture, NonParallelizable]
public sealed class FleshPustuleTest
{
    [Test]
    public async Task BurstSpawnsExactlyThreeOnceAndSpillsLetoferol()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var maps = server.System<SharedMapSystem>();
        EntityUid gridUid = default;
        EntityUid pustule = default;

        await server.WaitAssertion(() =>
        {
            maps.CreateMap(out var mapId);
            var grid = server.ResolveDependency<IMapManager>().CreateGridEntity(mapId);
            gridUid = grid;
            var floor = server.ResolveDependency<ITileDefinitionManager>()["FloorFlesh"];
            maps.SetTile(grid, Vector2i.Zero, new Tile(floor.TileId));
            pustule = em.SpawnEntity("WFFleshPustule", maps.GridTileToLocal(grid, grid.Comp, Vector2i.Zero));

            var system = server.System<WFFleshPustuleSystem>();
            Assert.That(system.TryBurst((pustule, em.GetComponent<WFFleshPustuleComponent>(pustule))), Is.True);
            Assert.That(system.TryBurst((pustule, em.GetComponent<WFFleshPustuleComponent>(pustule))), Is.False);
            Assert.That(em.GetComponent<WFFleshPustuleComponent>(pustule).Bursted, Is.True);

            var spawned = 0;
            var query = em.EntityQueryEnumerator<WFFleshPustuleSpawnedTickComponent>();
            while (query.MoveNext(out _, out _))
                spawned++;
            Assert.That(spawned, Is.EqualTo(WFFleshPustuleSystem.TicksPerBurst));

            var foundLetoferol = false;
            foreach (var uid in maps.GetAnchoredEntities(grid, grid.Comp, Vector2i.Zero))
            {
                if (!em.TryGetComponent<PuddleComponent>(uid, out var puddle) ||
                    !server.System<SharedSolutionContainerSystem>().TryGetSolution(uid, puddle.SolutionName, out _, out var solution))
                    continue;
                foundLetoferol |= solution.ContainsReagent("NaturalLetoferol", null);
            }
            Assert.That(foundLetoferol, Is.True);
        });

        await server.WaitPost(() => em.DeleteEntity(gridUid));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PlantingRequiresReachableBiomassAndRejectsStacking()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var maps = server.System<SharedMapSystem>();
        EntityUid gridUid = default;

        await server.WaitAssertion(() =>
        {
            maps.CreateMap(out var mapId);
            var grid = server.ResolveDependency<IMapManager>().CreateGridEntity(mapId);
            gridUid = grid;
            var floor = server.ResolveDependency<ITileDefinitionManager>()["FloorFlesh"];
            maps.SetTile(grid, Vector2i.Zero, new Tile(floor.TileId));
            maps.SetTile(grid, new Vector2i(1, 0), new Tile(floor.TileId));

            var center = maps.GridTileToLocal(grid, grid.Comp, Vector2i.Zero);
            em.SpawnEntity("ChimeraFleshKudzu", center);
            var chimera = em.SpawnEntity("MobLetoferolHorror", new EntityCoordinates(grid, new Vector2(0, -1)));
            var system = server.System<WFFleshPustuleSystem>();

            Assert.That(system.TryPlant(chimera, maps.GridTileToLocal(grid, grid.Comp, new Vector2i(1, 0))), Is.False,
                "A bare flesh floor accepted a pustule.");
            Assert.That(system.TryPlant(chimera, center), Is.True);
            Assert.That(system.TryPlant(chimera, center), Is.False, "A second pustule stacked on the first.");
        });

        await server.WaitPost(() => em.DeleteEntity(gridUid));
        await pair.CleanReturnAsync();
    }
}
