using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Robust.Shared.Maths;
using Content.Server._WF.Planets.Infestation;
using Content.Server._WF.Planets;
using Content.Server.Spreader;
using Content.Shared.Doors.Systems;
using Content.Shared._WF.Planets.Flight;
using Content.Shared._WF.Planets;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.Planets;

[TestFixture]
public sealed class CarcinomaInfestationTest
{
    [Test]
    public async Task TendrilsHoldHullUntilCutAndGrowthStaysBounded()
    {
        await using var pair = await PoolManager.GetServerClient();
        await EnableFeature(pair);
        var layers = new List<EntityUid>();
        var server = pair.Server;
        var em = server.EntMan;
        await server.WaitPost(() =>
        {
            var surface = server.ResolveDependency<IPrototypeManager>().Index<WFPlanetSurfacePrototype>("WFSurfaceCarcinoma");
            var network = server.System<WFPlanetNetworkSystem>().BuildNetwork(surface, Vector2.Zero, "Carcinoma", null)!.Value;
            layers.AddRange(em.GetComponent<WFPlanetNetworkComponent>(network).Layers);
        });
        await LayTiles(pair, layers[0], new Vector2i(-8, -8), new Vector2i(20, 20));
        var hull = await BuildDebris(pair, await MapIdOf(pair, layers[0]), 7);
        await MapInitHull(pair, hull);
        await server.WaitPost(() => server.System<SharedPhysicsSystem>().SetBodyType(hull, BodyType.Dynamic));
        EntityUid door = default;
        await server.WaitPost(() =>
        {
            for (var x = 0; x < 7; x++)
            for (var y = 0; y < 7; y++)
            {
                if (x != 0 && x != 6 && y != 0 && y != 6) continue;
                var opening = x == 3 && y == 0;
                var wall = em.SpawnEntity(opening ? "Airlock" : "WallSolid", new EntityCoordinates(hull, new Vector2(x + 0.5f, y + 0.5f)));
                em.RunMapInit(wall, em.GetComponent<MetaDataComponent>(wall));
                if (opening) door = wall;
            }
        });
        await pair.RunSeconds(3);
        await server.WaitAssertion(() => Assert.That(em.HasComponent<WFCarcinomaInfestationComponent>(hull), Is.True));
        for (var i = 0; i < 6; i++)
        {
            if (i == 1)
            {
                await server.WaitAssertion(() =>
                {
                    Assert.That(server.System<WFPlanetBiomassSystem>().HullCount(hull), Is.Zero, "Closed hull must exclude biomass.");
                    server.System<SharedDoorSystem>().StartOpening(door);
                });
                await pair.RunSeconds(1);
            }
            await server.WaitPost(() => em.GetComponent<WFCarcinomaInfestationComponent>(hull).NextGrowth = TimeSpan.Zero);
            await pair.RunSeconds(2.1f);
        }
        await server.WaitAssertion(() =>
        {
            var state = em.GetComponent<WFCarcinomaInfestationComponent>(hull);
            Assert.That(server.System<WFPlanetBiomassSystem>().HullCount(hull), Is.GreaterThan(0), "Open door must admit real chimera biomass.");
            Assert.That(state.Tendrils.Count, Is.EqualTo(WFCarcinomaInfestationSystem.MaxTendrils));
            Assert.That(em.GetComponent<PhysicsComponent>(hull).BodyType, Is.EqualTo(BodyType.Static));
            Assert.That(em.EntityExists(door), Is.True, "Tendrils must not replace exterior doors.");
            var walls = em.EntityQueryEnumerator<MetaDataComponent, TransformComponent>();
            var meatWalls = 0;
            while (walls.MoveNext(out _, out var metadata, out var transform))
                if (transform.GridUid == hull && metadata.EntityPrototype?.ID == "WallMeat") meatWalls++;
            Assert.That(meatWalls, Is.GreaterThan(0), "Tendrils must convert nearby hull walls to meat.");
            var attempt = new WFLiftoffAttemptEvent();
            em.EventBus.RaiseLocalEvent(hull, ref attempt);
            Assert.That(attempt.Cancelled, Is.True);
            Assert.That(attempt.Reason, Does.Contain("tendril"));
            foreach (var tendril in state.Tendrils.ToArray())
            {
                var damage = new DamageSpecifier();
                damage.DamageDict["Slash"] = 50;
                server.System<DamageableSystem>().TryChangeDamage(tendril, damage, ignoreResistances: true);
            }
        });
        await pair.RunTicksSync(3);
        await server.WaitAssertion(() =>
        {
            var state = em.GetComponent<WFCarcinomaInfestationComponent>(hull);
            Assert.That(state.Tendrils, Is.Empty);
            Assert.That(em.GetComponent<PhysicsComponent>(hull).BodyType, Is.EqualTo(BodyType.Dynamic));
            Assert.That(state.NextGrowth, Is.GreaterThan(server.ResolveDependency<IGameTiming>().CurTime));
            var attempt = new WFLiftoffAttemptEvent();
            em.EventBus.RaiseLocalEvent(hull, ref attempt);
            Assert.That(attempt.Cancelled, Is.False);
        });
        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ActualChimeraBiomassSpreadsOnHullButCannotJumpToTerrain()
    {
        await using var pair = await PoolManager.GetServerClient();
        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        await LayTiles(pair, layers[0], new Vector2i(-8, -8), new Vector2i(20, 20));
        var hull = await BuildDebris(pair, await MapIdOf(pair, layers[0]), 7);
        await MapInitHull(pair, hull);
        var server = pair.Server;
        var em = server.EntMan;
        EntityUid flesh = default;
        await server.WaitAssertion(() =>
        {
            Assert.That(server.System<WFPlanetBiomassSystem>().IsPlanet(hull), Is.False);
            flesh = em.SpawnEntity("ChimeraFleshKudzu", new EntityCoordinates(hull, new Vector2(3.5f)));
            em.RunMapInit(flesh, em.GetComponent<MetaDataComponent>(flesh));
            var kudzu = em.GetComponent<KudzuComponent>(flesh);
            kudzu.GrowthLevel = 3;
            kudzu.SpreadChance = 1;
            var map = server.System<SharedMapSystem>();
            var grid = em.GetComponent<MapGridComponent>(hull);
            var terrain = em.GetComponent<MapGridComponent>(layers[0]);
            var ev = new SpreadNeighborsEvent
            {
                Updates = 4,
                Neighbors = new(),
                NeighborFreeTiles = new()
                {
                    (grid, map.GetTileRef(hull, grid, new Vector2i(4, 3))),
                    (terrain, map.GetTileRef(layers[0], terrain, new Vector2i(3, 3)))
                }
            };
            em.EventBus.RaiseLocalEvent(flesh, ref ev);
            Assert.That(ev.NeighborFreeTiles.Count, Is.EqualTo(1));
        });
        await pair.RunTicksSync(2);
        await server.WaitAssertion(() =>
        {
            Assert.That(em.EntityExists(flesh), Is.True);
            Assert.That(server.System<WFPlanetBiomassSystem>().HullCount(hull), Is.GreaterThanOrEqualTo(2));
        });
        // Saturating a hull must bound actual entities, not merely postpone the next spread tick.
        await server.WaitPost(() =>
        {
            for (var i = 0; i < WFPlanetBiomassSystem.MaxHullBiomass + 8; i++)
            {
                var extra = em.SpawnEntity("ChimeraFleshKudzu", new EntityCoordinates(hull, new Vector2(2.5f)));
                em.RunMapInit(extra, em.GetComponent<MetaDataComponent>(extra));
            }
        });
        await pair.RunTicksSync(3);
        await server.WaitAssertion(() =>
            Assert.That(server.System<WFPlanetBiomassSystem>().HullCount(hull), Is.EqualTo(WFPlanetBiomassSystem.MaxHullBiomass)));
        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }
    /// <summary>Off a planet a hive grows as upstream has it: past the per-hull cap, and across onto a docked grid.</summary>
    [Test]
    public async Task AHiveOffAPlanetIsNotCappedOrKeptToOneGrid()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var hull = await BuildDebris(pair, map.MapId, 7, new Vector2(20f, 20f));
        var other = await BuildDebris(pair, map.MapId, 7, new Vector2(40f, 20f));
        await MapInitHull(pair, hull);
        await MapInitHull(pair, other);

        await server.WaitAssertion(() =>
        {
            var flesh = em.SpawnEntity("ChimeraFleshKudzu", new EntityCoordinates(hull, new Vector2(3.5f)));
            em.RunMapInit(flesh, em.GetComponent<MetaDataComponent>(flesh));
            var maps = server.System<SharedMapSystem>();
            var grid = em.GetComponent<MapGridComponent>(hull);
            var otherGrid = em.GetComponent<MapGridComponent>(other);
            var ev = new SpreadNeighborsEvent
            {
                Updates = 0,
                Neighbors = new(),
                NeighborFreeTiles = new()
                {
                    (grid, maps.GetTileRef(hull, grid, new Vector2i(4, 3))),
                    (otherGrid, maps.GetTileRef(other, otherGrid, new Vector2i(3, 3)))
                }
            };
            em.EventBus.RaiseLocalEvent(flesh, ref ev);
            Assert.That(ev.NeighborFreeTiles.Count, Is.EqualTo(2), "Off a planet the hive was kept from spreading onto another grid.");

            for (var i = 0; i < WFPlanetBiomassSystem.MaxHullBiomass + 8; i++)
            {
                var extra = em.SpawnEntity("ChimeraFleshKudzu", new EntityCoordinates(hull, new Vector2(2.5f)));
                em.RunMapInit(extra, em.GetComponent<MetaDataComponent>(extra));
            }
        });
        await pair.RunTicksSync(3);

        await server.WaitAssertion(() =>
            Assert.That(server.System<WFPlanetBiomassSystem>().HullCount(hull), Is.GreaterThan(WFPlanetBiomassSystem.MaxHullBiomass),
                "Off a planet the hive was capped."));

        await pair.CleanReturnAsync();
    }

    private static async Task<MapId> MapIdOf(TestPair pair, EntityUid layer)
    {
        var mapId = MapId.Nullspace;
        await pair.Server.WaitPost(() => mapId = pair.Server.EntMan.GetComponent<MapComponent>(layer).MapId);
        return mapId;
    }
}
