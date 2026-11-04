#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._Mono.Temperature.Components;
using Content.Server._WF.GroundCover;
using Content.Server._WF.Vehicles.Core;
using Content.Server._WF.Warmth;
using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Fluids.EntitySystems;
using Content.Shared._WF.GroundCover;
using Content.Shared.Atmos.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.FixedPoint;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests._WF.GroundCover;

/// <summary>What melts or presses the snow: burning things, atmos hotspots, warm liquids and vehicle treads.</summary>
[TestFixture]
[TestOf(typeof(WFGroundCoverSystem))]
public sealed class SnowInteractionsTest
{
    private const int Deep = 180;

    /// <summary>A burning thing melts the snow near it and warms what is in reach, and stops when the fire is out.</summary>
    [Test]
    public async Task ABurningThingMeltsSnowAndWarms()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var near = new Vector2i(-2, -1);
        var far = new Vector2i(-7, -7);
        EntityUid plank = default;

        await server.WaitPost(() =>
        {
            GroundCoverTest.LayOpenGround(pair, map.Grid);
            server.System<AtmosphereSystem>().SetMapAtmosphere(map.MapUid, false, GroundCoverTest.Air(250f));
            var cover = server.System<WFGroundCoverSystem>();
            cover.SetDepth(map.Grid, near, Deep);
            cover.SetDepth(map.Grid, far, Deep);
            plank = server.EntMan.SpawnEntity("MaterialWoodPlank1", new EntityCoordinates(map.Grid, new Vector2(-1.5f, -0.5f)));
            server.System<FlammableSystem>().Ignite(plank, plank);
            Assert.That(server.EntMan.GetComponent<FlammableComponent>(plank).OnFire, "Precondition: the plank didn't catch.");
        });

        await server.WaitRunTicks(pair.SecondsToTicks(12));
        await server.WaitAssertion(() =>
        {
            var cover = server.System<WFGroundCoverSystem>();
            var entMan = server.EntMan;
            Assert.That(entMan.HasComponent<EntityRadiusHeaterComponent>(plank), Is.True, "The burning plank gave no warmth.");
            Assert.That(cover.GetDepth(map.Grid, near), Is.LessThan(Deep - 30), "The burning plank left the snow beside it.");
            Assert.That(cover.GetDepth(map.Grid, far), Is.EqualTo(Deep), "Snow out of its reach melted.");
            Assert.That(server.System<WFWarmthSystem>().NearFire(entMan.GetComponent<TransformComponent>(plank)), Is.True,
                "Standing at a burning thing wasn't counted as near a fire.");

            server.System<FlammableSystem>().Extinguish(plank);
        });

        await server.WaitRunTicks(pair.SecondsToTicks(6));
        await server.WaitAssertion(() =>
        {
            var entMan = server.EntMan;
            Assert.That(entMan.HasComponent<EntityRadiusHeaterComponent>(plank), Is.False, "The heater stayed on a fire that was out.");
            Assert.That(entMan.HasComponent<WFBurnWarmthComponent>(plank), Is.False);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>An atmos hotspot melts the snow on its tile fastest, and some on the tiles beside it.</summary>
    [Test]
    public async Task AHotspotMeltsTheSnowUnderIt()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var centre = new Vector2i(0, 0);
        var beside = new Vector2i(1, 0);
        var far = new Vector2i(5, 5);

        await server.WaitPost(() =>
        {
            GroundCoverTest.LayOpenGround(pair, map.Grid);
            var cover = server.System<WFGroundCoverSystem>();
            foreach (var tile in new[] { centre, beside, far })
            {
                cover.SetDepth(map.Grid, tile, Deep);
            }
        });

        await server.WaitRunTicks(5);
        await server.WaitAssertion(() =>
        {
            var cover = server.System<WFGroundCoverSystem>();
            cover.MeltAround(map.Grid, centre, 24f);
            Assert.That(cover.GetDepth(map.Grid, centre), Is.LessThan(Deep), "A hotspot left the snow on its tile.");
            Assert.That(cover.GetDepth(map.Grid, beside), Is.LessThan(Deep), "A hotspot left the snow beside it.");
            Assert.That(cover.GetDepth(map.Grid, centre), Is.LessThan(cover.GetDepth(map.Grid, beside)), "The tile beside melted as fast as the hot one.");
            Assert.That(cover.GetDepth(map.Grid, far), Is.EqualTo(Deep), "A hotspot melted snow away from it.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Warm blood melts some of the snow it lands on, once; cold water melts none.</summary>
    [Test]
    public async Task WarmLiquidMeltsSnowOnce()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var blood = new Vector2i(-3, 0);
        var water = new Vector2i(3, 0);

        await server.WaitPost(() =>
        {
            GroundCoverTest.LayOpenGround(pair, map.Grid);
            server.System<AtmosphereSystem>().SetMapAtmosphere(map.MapUid, false, GroundCoverTest.Air(250f));
            var cover = server.System<WFGroundCoverSystem>();
            cover.SetDepth(map.Grid, blood, Deep);
            cover.SetDepth(map.Grid, water, Deep);
            var puddles = server.System<PuddleSystem>();
            Assert.That(puddles.TrySpillAt(new EntityCoordinates(map.Grid, new Vector2(-2.5f, 0.5f)),
                new Solution("Blood", FixedPoint2.New(20)), out _, false), "Precondition: the blood didn't pool.");
            Assert.That(puddles.TrySpillAt(new EntityCoordinates(map.Grid, new Vector2(3.5f, 0.5f)),
                new Solution("Water", FixedPoint2.New(20)), out _, false), "Precondition: the water didn't pool.");
        });

        await server.WaitRunTicks(pair.SecondsToTicks(5));
        var melted = 0;
        await server.WaitAssertion(() =>
        {
            var cover = server.System<WFGroundCoverSystem>();
            melted = cover.GetDepth(map.Grid, blood);
            Assert.That(melted, Is.LessThan(Deep), "Warm blood left the snow it landed on.");
            Assert.That(cover.GetDepth(map.Grid, water), Is.EqualTo(Deep), "Cold water melted snow.");
        });

        await server.WaitRunTicks(pair.SecondsToTicks(10));
        await server.WaitAssertion(() =>
            Assert.That(server.System<WFGroundCoverSystem>().GetDepth(map.Grid, blood), Is.EqualTo(melted),
                "The same puddle kept melting snow."));

        await pair.CleanReturnAsync();
    }

    /// <summary>Treads press a level of snow flat under each side of the hull, once for each tile they enter.</summary>
    [Test]
    public async Task VehicleTreadsPressTheSnowFlat()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        EntityUid vehicle = default;
        var tiles = new List<Vector2i>();
        for (var x = 0; x <= 3; x++)
        {
            tiles.Add(new Vector2i(x, 0));
            tiles.Add(new Vector2i(x, 3));
        }

        await server.WaitPost(() =>
        {
            GroundCoverTest.LayOpenGround(pair, map.Grid);
            var cover = server.System<WFGroundCoverSystem>();
            foreach (var tile in tiles)
            {
                cover.SetDepth(map.Grid, tile, Deep);
            }

            vehicle = server.EntMan.SpawnEntity(null, new EntityCoordinates(map.Grid, Vector2.Zero));
        });

        await server.WaitAssertion(() =>
        {
            var cover = server.System<WFGroundCoverSystem>();
            Treads(server, vehicle, map.Grid, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 3.5f));
            Assert.That(cover.GetDepth(map.Grid, new Vector2i(0, 0)), Is.EqualTo(Deep - WFGroundCoverSystem.TrackCompression),
                "The left tread didn't press the snow.");
            Assert.That(cover.GetDepth(map.Grid, new Vector2i(0, 3)), Is.EqualTo(Deep - WFGroundCoverSystem.TrackCompression),
                "The right tread didn't press the snow.");

            Treads(server, vehicle, map.Grid, new Vector2(0.7f, 0.9f), new Vector2(0.7f, 3.9f));
            Assert.That(cover.GetDepth(map.Grid, new Vector2i(0, 0)), Is.EqualTo(Deep - WFGroundCoverSystem.TrackCompression),
                "A tread pressed the same tile twice in one pass.");

            Treads(server, vehicle, map.Grid, new Vector2(1.5f, 0.5f), new Vector2(1.5f, 3.5f));
            Assert.That(cover.GetDepth(map.Grid, new Vector2i(1, 0)), Is.EqualTo(Deep - WFGroundCoverSystem.TrackCompression),
                "The next tile wasn't pressed.");
            Assert.That(cover.GetDepth(map.Grid, new Vector2i(2, 0)), Is.EqualTo(Deep), "A tile the treads didn't reach was pressed.");
        });

        await pair.CleanReturnAsync();
    }

    private static void Treads(RobustIntegrationTest.ServerIntegrationInstance server, EntityUid vehicle, EntityUid grid,
        Vector2 left, Vector2 right)
    {
        var ev = new WFVehicleTreadsEvent(vehicle, grid, left, right);
        server.EntMan.EventBus.RaiseEvent(EventSource.Local, ref ev);
    }
}
