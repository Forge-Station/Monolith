#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._CE.ZLevels.Core;
using Content.Server._NF.Shuttles.Components;
using Content.Server._WF.CargoLoad;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._Mono.Ships;
using Content.Shared._WF.CargoLoad;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.IntegrationTests.Tests._WF.CargoLoad;

/// <summary>Bare decks, ports and the expected thrust factor, shared by the cargo load fixtures.</summary>
internal static class CargoDeck
{
    public const string Port = "AirlockShuttle";

    /// <summary>A square dynamic grid of the test map's tile, its corner at <paramref name="origin"/>.</summary>
    public static EntityUid Create(IEntityManager entMan, TestMapData map, Vector2 origin, int size = 10)
    {
        var mapSys = entMan.System<SharedMapSystem>();
        var grid = mapSys.CreateGridEntity(map.MapId);
        var tiles = new List<(Vector2i, Tile)>();
        for (var x = 0; x < size; x++)
        {
            for (var y = 0; y < size; y++)
            {
                tiles.Add((new Vector2i(x, y), map.Tile.Tile));
            }
        }

        mapSys.SetTiles(grid.Owner, grid.Comp, tiles);
        entMan.System<SharedTransformSystem>().SetWorldPosition(grid.Owner, origin);
        entMan.System<SharedPhysicsSystem>().SetBodyType(grid.Owner, BodyType.Dynamic);
        return grid.Owner;
    }

    /// <summary>A port on a grid; 0 degrees faces south (-Y), 90 east, 180 north, -90 west.</summary>
    public static EntityUid AddPort(IEntityManager entMan, EntityUid grid, Vector2 at, float degrees)
    {
        var port = entMan.SpawnEntity(Port, new EntityCoordinates(grid, at));
        entMan.System<SharedTransformSystem>().SetLocalRotation(port, Angle.FromDegrees(degrees));
        return port;
    }

    /// <summary>Docks two ports the way a finished docking leaves them.</summary>
    public static void Dock(IEntityManager entMan, EntityUid portA, EntityUid portB)
    {
        entMan.System<DockingSystem>().Dock((portA, entMan.GetComponent<DockingComponent>(portA)),
            (portB, entMan.GetComponent<DockingComponent>(portB)));
    }

    /// <summary>The share of thrust a set of grids keeps with a load: hull / (hull + load), down to the floor.</summary>
    public static float Factor(IEntityManager entMan, WFCargoLoadPrototype profile, float load, params EntityUid[] set)
    {
        if (load <= 0f)
            return 1f;

        var hull = set.Sum(grid => entMan.GetComponent<PhysicsComponent>(grid).FixturesMass);
        return System.Math.Max(profile.ThrustFloor, hull / (hull + load));
    }
}

/// <summary>
/// Cargo sets: docked grids share one load and one thrust factor whatever their FTL lock, anchors and maps bound a set,
/// and cargo in a container still weighs.
/// </summary>
[TestFixture]
[TestOf(typeof(WFCargoLoadSystem))]
public sealed class CargoSetTest
{
    private const string BulkCrate = "TradeGoodMetals";
    private const string LightCrate = "TradeGoodLuxuries";

    /// <summary>
    /// A loaded barge docked to a tug: each crate counts once, both carry the set factor, a barge with its FTL lock off
    /// still slows the tug (but no longer jumps or lifts with it), and undocking frees the tug.
    /// </summary>
    [Test]
    public async Task DockedPairSharesOneFactor()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var cargo = entMan.System<WFCargoLoadSystem>();
        var levels = entMan.System<CEZLevelsSystem>();
        var profile = server.ProtoMan.Index(WFCargoLoadSystem.DefaultProfile);

        EntityUid tug = default, barge = default, tugPort = default, tugCrate = default;
        float tugLoad = 0f, bargeLoad = 0f;

        await server.WaitAssertion(() =>
        {
            tug = CargoDeck.Create(entMan, map, new Vector2(30f, 0f));
            barge = CargoDeck.Create(entMan, map, new Vector2(30f, 10f));
            tugPort = CargoDeck.AddPort(entMan, tug, new Vector2(4.5f, 9.5f), 180f);
            var bargePort = CargoDeck.AddPort(entMan, barge, new Vector2(4.5f, 0.5f), 0f);

            tugCrate = entMan.SpawnEntity(LightCrate, new EntityCoordinates(tug, 1.5f, 1.5f));
            tugLoad = cargo.Weight(tugCrate);
            for (var i = 0; i < 2; i++)
            {
                var crate = entMan.SpawnEntity(BulkCrate, new EntityCoordinates(barge, 1.5f + i * 2, 4.5f));
                bargeLoad += cargo.Weight(crate);
            }

            CargoDeck.Dock(entMan, tugPort, bargePort);
        });

        await pair.RunTicksSync(90);

        await server.WaitAssertion(() =>
        {
            var set = cargo.GetCargoSet(tug);
            Assert.That(set, Is.EquivalentTo(new[] { tug, barge }), "Docked grids form one cargo set.");
            Assert.That(cargo.GetCargoSet(barge), Is.EquivalentTo(set), "The set is the same from either end.");
            Assert.That(cargo.GetLoad(tug), Is.EqualTo(tugLoad).Within(0.01f));
            Assert.That(cargo.GetLoad(barge), Is.EqualTo(bargeLoad).Within(0.01f));
            Assert.That(cargo.GetSetLoad(set), Is.EqualTo(tugLoad + bargeLoad).Within(0.01f), "Each crate counts once.");

            var expected = CargoDeck.Factor(entMan, profile, tugLoad + bargeLoad, tug, barge);
            Assert.That(expected, Is.LessThan(1f));
            foreach (var member in new[] { tug, barge })
            {
                Assert.That(entMan.GetComponent<WFCargoLoadComponent>(member).SetLoad, Is.EqualTo(tugLoad + bargeLoad).Within(0.01f));
                Assert.That(cargo.GetSpaceFactor(member), Is.EqualTo(expected).Within(0.001f), "Both members carry the set factor.");
            }

            Assert.That(levels.WfGetSetLiftLoad(tug), Is.EqualTo(tugLoad + bargeLoad).Within(0.01f),
                "Planet lift counts each crate of a locked pair once.");
            Assert.That(cargo.SpoolPenalty(tug, new FTLDriveComponent()),
                Is.EqualTo(System.Math.Min(profile.SpoolCap, profile.SpoolPerTonne * (tugLoad + bargeLoad))).Within(0.001f),
                "A locked barge jumps with the tug, so its cargo spools too.");

            entMan.System<ShuttleConsoleSystem>().SetFTLLock(barge, false);
        });

        await pair.RunTicksSync(90);

        await server.WaitAssertion(() =>
        {
            var expected = CargoDeck.Factor(entMan, profile, tugLoad + bargeLoad, tug, barge);
            Assert.That(cargo.GetCargoSet(tug), Is.EquivalentTo(new[] { tug, barge }), "The thrust set ignores the FTL lock.");
            Assert.That(cargo.GetSpaceFactor(tug), Is.EqualTo(expected).Within(0.001f), "A barge with its lock off still slows the tug.");
            Assert.That(levels.WfGetSetLiftLoad(tug), Is.EqualTo(tugLoad).Within(0.01f), "Lift judges an unlocked barge as its own set.");
            Assert.That(cargo.SpoolPenalty(tug, new FTLDriveComponent()),
                Is.EqualTo(System.Math.Min(profile.SpoolCap, profile.SpoolPerTonne * tugLoad)).Within(0.001f),
                "An unlocked barge stays behind, so its cargo adds no spool.");

            // Empty the tug and cast off.
            entMan.DeleteEntity(tugCrate);
            entMan.System<DockingSystem>().Undock((tugPort, entMan.GetComponent<DockingComponent>(tugPort)));
        });

        await pair.RunTicksSync(90);

        await server.WaitAssertion(() =>
        {
            Assert.That(cargo.GetCargoSet(tug), Is.EquivalentTo(new[] { tug }));
            Assert.That(cargo.GetSpaceFactor(tug), Is.EqualTo(1f), "Undocked and empty, the tug gets full thrust back.");
            Assert.That(entMan.HasComponent<WFCargoLoadComponent>(tug), Is.False);
            Assert.That(cargo.GetSpaceFactor(barge), Is.EqualTo(CargoDeck.Factor(entMan, profile, bargeLoad, barge)).Within(0.001f),
                "The barge keeps its own load.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Anchors bound sets: a loaded ship docked to a force-anchored station reads its undocked factor, two ships at one
    /// station keep separate factors, and the station carries its own load alone.
    /// </summary>
    [Test]
    public async Task AnchorsKeepSetsApart()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var cargo = entMan.System<WFCargoLoadSystem>();
        var physics = entMan.System<SharedPhysicsSystem>();
        var profile = server.ProtoMan.Index(WFCargoLoadSystem.DefaultProfile);

        EntityUid station = default, heavy = default, light = default;
        EntityUid stationWest = default, stationEast = default, heavyPort = default, lightPort = default;
        float heavyLoad = 0f, lightLoad = 0f, stationLoad = 0f;
        float heavyAlone = 0f, lightAlone = 0f;

        await server.WaitAssertion(() =>
        {
            station = CargoDeck.Create(entMan, map, new Vector2(60f, 0f));
            heavy = CargoDeck.Create(entMan, map, new Vector2(50f, 0f));
            light = CargoDeck.Create(entMan, map, new Vector2(70f, 0f));
            entMan.AddComponent<ForceAnchorComponent>(station);

            // Ports face each other across touching hull edges, so the welds pull nothing.
            stationWest = CargoDeck.AddPort(entMan, station, new Vector2(0.5f, 4.5f), -90f);
            stationEast = CargoDeck.AddPort(entMan, station, new Vector2(9.5f, 4.5f), 90f);
            heavyPort = CargoDeck.AddPort(entMan, heavy, new Vector2(9.5f, 4.5f), 90f);
            lightPort = CargoDeck.AddPort(entMan, light, new Vector2(0.5f, 4.5f), -90f);

            for (var i = 0; i < 3; i++)
            {
                heavyLoad += cargo.Weight(entMan.SpawnEntity(BulkCrate, new EntityCoordinates(heavy, 2.5f + i * 2, 2.5f)));
            }

            lightLoad = cargo.Weight(entMan.SpawnEntity(LightCrate, new EntityCoordinates(light, 4.5f, 2.5f)));
            stationLoad = cargo.Weight(entMan.SpawnEntity(LightCrate, new EntityCoordinates(station, 4.5f, 2.5f)));
        });

        await pair.RunTicksSync(90);

        await server.WaitAssertion(() =>
        {
            Assert.That(cargo.IsAnchor(station), Is.True, "A force-anchored grid is an anchor.");
            Assert.That(cargo.IsAnchor(heavy), Is.False);
            heavyAlone = cargo.GetSpaceFactor(heavy);
            lightAlone = cargo.GetSpaceFactor(light);
            Assert.That(heavyAlone, Is.EqualTo(CargoDeck.Factor(entMan, profile, heavyLoad, heavy)).Within(0.001f));
            Assert.That(lightAlone, Is.EqualTo(CargoDeck.Factor(entMan, profile, lightLoad, light)).Within(0.001f));
            Assert.That(heavyAlone, Is.LessThan(lightAlone), "The heavier ship is slower.");

            CargoDeck.Dock(entMan, heavyPort, stationWest);
            CargoDeck.Dock(entMan, lightPort, stationEast);
        });

        await pair.RunTicksSync(90);

        await server.WaitAssertion(() =>
        {
            Assert.That(cargo.GetCargoSet(heavy), Is.EquivalentTo(new[] { heavy }), "A set stops at an anchor.");
            Assert.That(cargo.GetCargoSet(light), Is.EquivalentTo(new[] { light }));
            Assert.That(cargo.GetCargoSet(station), Is.EquivalentTo(new[] { station }), "An anchor's set is itself.");

            Assert.That(cargo.GetSpaceFactor(heavy), Is.EqualTo(heavyAlone).Within(0.001f), "Docking at a station doesn't change a ship's factor.");
            Assert.That(cargo.GetSpaceFactor(light), Is.EqualTo(lightAlone).Within(0.001f), "Two ships at one station keep separate factors.");

            Assert.That(cargo.GetLoad(station), Is.EqualTo(stationLoad).Within(0.01f), "An anchor still carries its own load.");
            Assert.That(cargo.GetSpaceFactor(station), Is.EqualTo(CargoDeck.Factor(entMan, profile, stationLoad, station)).Within(0.001f),
                "A loaded anchor carries its own one-grid factor.");

            // A static body is an anchor too, without the component.
            physics.SetBodyType(light, BodyType.Static);
            Assert.That(cargo.IsAnchor(light), Is.True);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A crate inside a container on the ship still weighs it down.</summary>
    [Test]
    public async Task CargoInAContainerCounts()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var cargo = entMan.System<WFCargoLoadSystem>();
        var containers = entMan.System<SharedContainerSystem>();

        EntityUid ship = default;
        float load = 0f;

        await server.WaitAssertion(() =>
        {
            ship = CargoDeck.Create(entMan, map, new Vector2(30f, 0f));
            var holder = entMan.SpawnEntity(null, new EntityCoordinates(ship, 2.5f, 2.5f));
            var hold = containers.EnsureContainer<Container>(holder, "wf-test-hold");
            var crate = entMan.SpawnEntity(BulkCrate, new EntityCoordinates(ship, 5.5f, 5.5f));
            load = cargo.Weight(crate);

            Assert.That(containers.Insert(crate, hold), Is.True, "The crate should go into the hold.");
            Assert.That(containers.IsEntityInContainer(crate), Is.True);
            Assert.That(entMan.GetComponent<TransformComponent>(crate).GridUid, Is.EqualTo(ship), "A contained crate is still on the ship's grid.");
        });

        await pair.RunTicksSync(90);

        await server.WaitAssertion(() =>
        {
            Assert.That(load, Is.GreaterThan(0f));
            Assert.That(cargo.GetLoad(ship), Is.EqualTo(load).Within(0.01f), "A crate in a container counts.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A map that is itself a grid (a planet) carries no load for crates on its ground, and a ship landed and docked to it
    /// keeps a one-grid set.
    /// </summary>
    [Test]
    public async Task MapGridsCarryNoLoad()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var cargo = entMan.System<WFCargoLoadSystem>();
        var mapSys = entMan.System<SharedMapSystem>();

        EntityUid ship = default, groundCrate = default;
        DockingComponent shipDock = default!, groundDock = default!;
        float shipLoad = 0f;

        await server.WaitAssertion(() =>
        {
            var ground = entMan.EnsureComponent<MapGridComponent>(map.MapUid);
            for (var x = 40; x < 45; x++)
            {
                for (var y = 40; y < 45; y++)
                {
                    mapSys.SetTile(map.MapUid, ground, new Vector2i(x, y), map.Tile.Tile);
                }
            }

            groundCrate = entMan.SpawnEntity(BulkCrate, new EntityCoordinates(map.MapUid, 41.5f, 41.5f));
            var groundPort = entMan.SpawnEntity(CargoDeck.Port, new EntityCoordinates(map.MapUid, 44.5f, 42.5f));

            ship = CargoDeck.Create(entMan, map, new Vector2(46f, 40f), size: 5);
            var shipPort = entMan.SpawnEntity(CargoDeck.Port, new EntityCoordinates(ship, 0.5f, 2.5f));
            shipLoad = cargo.Weight(entMan.SpawnEntity(LightCrate, new EntityCoordinates(ship, 2.5f, 2.5f)));

            Assert.That(entMan.GetComponent<TransformComponent>(groundCrate).GridUid, Is.EqualTo(map.MapUid), "The crate sits on the map's ground.");
            Assert.That(entMan.GetComponent<TransformComponent>(groundPort).GridUid, Is.EqualTo(map.MapUid));

            // Wire the ports as docked without a weld joint to the map body.
            shipDock = entMan.GetComponent<DockingComponent>(shipPort);
            groundDock = entMan.GetComponent<DockingComponent>(groundPort);
            shipDock.DockedWith = groundPort;
            groundDock.DockedWith = shipPort;
        });

        await pair.RunTicksSync(90);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.HasComponent<WFCargoLoadComponent>(map.MapUid), Is.False, "A map grid gets no load.");
            Assert.That(cargo.GetLoad(map.MapUid), Is.EqualTo(0f));
            Assert.That(cargo.GetCargoSet(ship), Is.EquivalentTo(new[] { ship }), "A landed ship's set never takes in the map.");
            Assert.That(cargo.GetLoad(ship), Is.EqualTo(shipLoad).Within(0.01f));

            shipDock.DockedWith = null;
            groundDock.DockedWith = null;
        });

        await pair.CleanReturnAsync();
    }
}
