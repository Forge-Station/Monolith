#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._CE.ZLevels.Core;
using Content.Server._CE.ZLevels.Core.Components;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Shuttles.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.Planets;

/// <summary>The orbit layer holds a hull up however it arrived, and the console descent is the only way down.</summary>
[TestFixture]
[TestOf(typeof(CEZLevelsSystem))]
public sealed class OrbitArrivalTest
{
    /// <summary>FTL travel time for the arrival test, in seconds.</summary>
    private const float FtlStartup = 0.2f;

    private const float FtlTravel = 0.5f;

    /// <summary>A gravgen-less hull arriving in orbit by FTL is still on the orbit layer ten seconds later.</summary>
    [Test]
    public async Task FtlArrivalStaysInOrbit()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var orbit = layers[^1];

        var origin = await pair.CreateTestMap();
        var hull = await BuildHull(pair, origin.MapId);
        await OpenOriginTile(pair, hull);
        await MapInitHull(pair, hull);

        await FtlTo(pair, hull, orbit);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<TransformComponent>(hull).MapUid, Is.EqualTo(orbit),
                "Precondition: the FTL did not put the hull on the orbit layer at all.");
        });

        await server.WaitRunTicks(pair.SecondsToTicks(10f));

        await server.WaitAssertion(() =>
        {
            var mapUid = entMan.GetComponent<TransformComponent>(hull).MapUid;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(mapUid, Is.EqualTo(orbit),
                    $"The hull left the orbit layer for {entMan.ToPrettyString(mapUid)} without being told to descend.");
                Assert.That(entMan.HasComponent<CEZTransitMapComponent>(mapUid), Is.False,
                    "The hull started falling out of orbit on its own.");
            }
        });

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>A gravgen-less hull spawned onto the orbit layer and woken stays put.</summary>
    [Test]
    public async Task SpawnedHullStaysInOrbit()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var zLevels = server.System<CEZLevelsSystem>();

        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var orbit = layers[^1];
        var orbitMapId = await MapIdOf(pair, orbit);

        var hull = await BuildHull(pair, orbitMapId);
        await OpenOriginTile(pair, hull);
        await MapInitHull(pair, hull);
        await Nudge(pair, hull);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.GetComponent<TransformComponent>(hull).MapUid, Is.EqualTo(orbit),
                    "Precondition: the hull did not start on the orbit layer.");
                Assert.That(entMan.HasComponent<CEZGridFallerComponent>(hull), Is.True,
                    "Precondition: the hull is not on a z-network at all, so nothing is being tested.");
                Assert.That(entMan.GetComponent<CEZPhysicsComponent>(hull).CachedGroundHeight, Is.LessThan(0f),
                    "Precondition: the hull still reads solid ground under itself, so the sinking path is not armed " +
                    "and this would pass without the orbit hold.");
                Assert.That(zLevels.WfIsParkedInOrbit((hull, entMan.GetComponent<MapGridComponent>(hull))), Is.True,
                    "The orbit hold is not engaged on the hull.");
            }
        });

        await server.WaitRunTicks(pair.SecondsToTicks(10f));

        await server.WaitAssertion(() =>
        {
            var mapUid = entMan.GetComponent<TransformComponent>(hull).MapUid;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(mapUid, Is.EqualTo(orbit),
                    $"The hull sank off the orbit layer to {entMan.ToPrettyString(mapUid)} with no descend input.");
                Assert.That(entMan.HasComponent<CEZTransitMapComponent>(mapUid), Is.False,
                    "The hull started falling out of orbit on its own.");
            }
        });

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>The console descent puts the hull into transit below orbit; with no lift it must confirm.</summary>
    [Test]
    public async Task DescendFromOrbitEntersTransit()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var orbit = layers[^1];
        var ground = layers[0];
        var topAir = layers[^2];
        var orbitMapId = await MapIdOf(pair, orbit);

        var hull = await BuildHull(pair, orbitMapId);
        await OpenOriginTile(pair, hull);
        await MapInitHull(pair, hull);

        var refusal = await EnterAtmosphere(pair, hull);

        Assert.That(refusal, Is.Null, $"The confirmed descent was refused: {refusal}");

        await server.WaitAssertion(() =>
        {
            var mapUid = entMan.GetComponent<TransformComponent>(hull).MapUid;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(mapUid, Is.Not.EqualTo(ground),
                    "The descend dropped the hull straight onto the ground map, skipping every layer between.");
                Assert.That(mapUid, Is.Not.EqualTo(topAir),
                    "The descend hopped the hull directly onto the air layer instead of flying it down through the gap.");
                Assert.That(entMan.HasComponent<CEZTransitMapComponent>(mapUid), Is.True,
                    $"The descending hull is on {entMan.ToPrettyString(mapUid)}, which is not a transit map.");
            }
        });

        await server.WaitAssertion(() =>
        {
            var transit = entMan.GetComponent<CEZTransitMapComponent>(
                entMan.GetComponent<TransformComponent>(hull).MapUid!.Value);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(transit.UpperMap, Is.EqualTo(orbit),
                    "The hull is in a gap that does not hang off the orbit layer, so it skipped a level on the way in.");
                Assert.That(transit.LowerMap, Is.EqualTo(topAir),
                    "The hull is in a gap whose floor is not the layer directly below orbit.");
            }
        });

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>Clears the tile under the grid origin, so the ground-height walk finds no floor.</summary>
    private static async Task OpenOriginTile(TestPair pair, EntityUid hull)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();

        await server.WaitPost(() =>
        {
            var grid = entMan.GetComponent<MapGridComponent>(hull);
            maps.SetTiles(hull, grid, new List<(Vector2i, Tile)> { (Vector2i.Zero, Tile.Empty) });
        });

        await server.WaitRunTicks(1);
    }

    /// <summary>The map id of a z-layer, for the spawners that want one.</summary>
    private static async Task<MapId> MapIdOf(TestPair pair, EntityUid layer)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var mapId = MapId.Nullspace;

        await server.WaitPost(() => mapId = entMan.GetComponent<MapComponent>(layer).MapId);
        return mapId;
    }

    /// <summary>Flies a hull to a layer through the real FTL machinery.</summary>
    private static async Task FtlTo(TestPair pair, EntityUid hull, EntityUid layer)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var shuttles = server.System<ShuttleSystem>();

        await server.WaitPost(() =>
        {
            var shuttle = entMan.GetComponent<ShuttleComponent>(hull);
            shuttles.FTLToCoordinates(hull,
                shuttle,
                new EntityCoordinates(layer, Vector2.Zero),
                Angle.Zero,
                FtlStartup,
                FtlTravel);
        });

        // Arrival time is a server cvar, so wait for it rather than timing it.
        var arrived = false;
        for (var i = 0; i < 60 && !arrived; i++)
        {
            await server.WaitRunTicks(pair.SecondsToTicks(0.5f));
            await server.WaitPost(() =>
                arrived = entMan.GetComponent<TransformComponent>(hull).MapUid == layer);
        }
    }

    /// <summary>Nudges a hull so its z-physics body is awake; a grid that never moves is never active.</summary>
    private static async Task Nudge(TestPair pair, EntityUid hull)
    {
        var server = pair.Server;
        var transform = server.System<SharedTransformSystem>();

        await server.WaitPost(() => transform.SetLocalPosition(hull, new Vector2(0.5f, 0.5f)));
        await server.WaitRunTicks(1);
    }
}
