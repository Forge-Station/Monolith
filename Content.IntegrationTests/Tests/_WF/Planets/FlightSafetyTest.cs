#nullable enable
using Content.Shared.Shuttles.Components;
using System.Numerics;
using System.Collections.Generic;
using Content.IntegrationTests.Pair;
using Content.Server._CE.ZLevels.Core;
using Content.Server._CE.ZLevels.Core.Components;
using Content.Server._WF.Planets.Flight;
using Content.Server.Shuttles.Components;
using Content.Shared.Gravity;
using Content.Shared._CE.ZLevels.Core.EntitySystems;
using Content.Shared.Movement.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.Planets;

[TestFixture]
public sealed class FlightSafetyTest
{
    [TestCase(0)]
    [TestCase(1)]
    public async Task GravgenCannotLaunchAnUnderLiftedHull(int landingThrusters)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var ground = layers[0];
        await LayTiles(pair, ground, new Vector2i(-8, -8), new Vector2i(24, 24));
        var hull = await BuildHull(pair, await MapIdOf(pair, ground));
        await MapInitHull(pair, hull);
        await AddLandingThrusters(pair, hull, landingThrusters);
        await server.WaitPost(() =>
        {
            var gravity = entMan.EnsureComponent<GravityComponent>(hull);
            gravity.Enabled = true;
            gravity.Inherent = true;
        });
        var pilot = await HoldVertical(pair, hull, ShuttleButtons.None);
        var console = FindShuttleConsole(entMan, hull);
        var levels = server.System<CEZLevelsSystem>();
        var started = true;
        string? reason = null;
        await server.WaitPost(() => started = levels.WfTryBeginLiftoff(hull, console, pilot, out reason));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(started, Is.False, "The Liftoff button accepted a hull without sufficient landing lift.");
            Assert.That(reason, Is.Not.Null, "The rejected Liftoff request did not explain the insufficient lift.");
            Assert.That(entMan.HasComponent<WFLiftoffComponent>(hull), Is.False,
                "A rejected Liftoff request still latched ascent.");
        }

        await server.WaitPost(() => entMan.GetComponent<PilotComponent>(pilot).HeldButtons = ShuttleButtons.AscendZ);
        for (var second = 0; second < 6; second++)
        {
            await server.WaitRunTicks(pair.SecondsToTicks(1));
            await server.WaitAssertion(() => Assert.That(entMan.GetComponent<TransformComponent>(hull).MapUid,
                Is.EqualTo(ground), "A gravgen launched a hull without sufficient landing lift."));
        }
        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TakingOffEndsTheSkidBeforeItCanDamageTheHull()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var ground = layers[0];
        await LayTiles(pair, ground, new Vector2i(-8, -8), new Vector2i(24, 24));
        var hull = await BuildHull(pair, await MapIdOf(pair, ground));
        await MapInitHull(pair, hull);
        await AddLandingThrusters(pair, hull, 3);
        var count = 0;
        await server.WaitAssertion(() =>
        {
            var grid = entMan.GetComponent<MapGridComponent>(hull);
            server.System<WFFlightSystem>().BeginSkid(hull, thud: false);
            server.System<SharedPhysicsSystem>().SetLinearVelocity(hull, new Vector2(8, 0));
            count = TileCount(pair, hull);
            Assert.That(server.System<CEZLevelsSystem>().TryEnterTransit((hull, grid), preferUpperGap: true), Is.True);
        });
        await server.WaitRunTicks(pair.SecondsToTicks(0.5f));
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.HasComponent<WFSkidComponent>(hull), Is.False);
            Assert.That(TileCount(pair, hull), Is.EqualTo(count));
        });
        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SkidOnlyPloughsWallsTouchingActualHullTiles()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();
        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var ground = layers[0];
        await LayTiles(pair, ground, new Vector2i(-8, -8), new Vector2i(24, 24));
        var hit = EntityUid.Invalid;
        var missed = EntityUid.Invalid;
        await server.WaitPost(() =>
        {
            hit = entMan.SpawnEntity("WallSolid", new EntityCoordinates(ground, new Vector2(0.5f, 0.5f)));
            missed = entMan.SpawnEntity("WallSolid", new EntityCoordinates(ground, new Vector2(5.5f, 5.5f)));
            Assert.That(entMan.GetComponent<TransformComponent>(hit).Anchored, Is.True);
            Assert.That(entMan.GetComponent<TransformComponent>(missed).Anchored, Is.True);
        });
        // Build away from the obstacles so CE cannot bounce the still-square hull before we cut out its corner.
        var hull = await BuildDebris(pair, await MapIdOf(pair, layers[^1]), size: 9);
        await MapInitHull(pair, hull);
        await server.WaitPost(() =>
        {
            var grid = entMan.GetComponent<MapGridComponent>(hull);
            // An L-shaped footprint: the distant wall is inside its bounding box but outside all hull tiles.
            for (var x = 1; x < 9; x++)
            for (var y = 1; y < 9; y++)
                maps.SetTile(hull, grid, new Vector2i(x, y), Tile.Empty);
            server.System<SharedTransformSystem>().SetCoordinates(hull, new EntityCoordinates(ground, Vector2.Zero));
            entMan.EnsureComponent<CEZGridFallerComponent>(hull);
            var physics = server.System<SharedPhysicsSystem>();
            physics.SetBodyType(hull, BodyType.Dynamic);
            physics.SetLinearVelocity(hull, new Vector2(3, 0));
            var skid = entMan.EnsureComponent<WFSkidComponent>(hull);
            // Isolate wall ploughing from the separate periodic skid crush pass.
            skid.NextBite = TimeSpan.MaxValue;
            var zLevels = server.System<CEZLevelsSystem>();
            Assert.That(zLevels.WfHasSkidGround(hull), Is.True, "Fixture must touch terrain.");
            var contacts = new List<CESharedZLevelsSystem.CEWallContact>();
            zLevels.GetWallContacts(hull, contacts);
            Assert.That(contacts.Exists(contact => contact.Wall == hit), Is.True,
                "Fixture must produce a CE wall contact.");
            Assert.That(contacts.Exists(contact => contact.Wall == missed), Is.False,
                "Fixture's distant wall must be outside the actual hull tiles.");
        });
        await server.WaitRunTicks(5);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.EntityExists(hit), Is.False, "The wall touching the hull was not ploughed.");
            Assert.That(entMan.EntityExists(missed), Is.True, "An untouched wall in the hull's empty corner was destroyed.");
        });
        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    private static async Task<MapId> MapIdOf(TestPair pair, EntityUid map)
    {
        var result = MapId.Nullspace;
        await pair.Server.WaitPost(() => result = pair.Server.EntMan.GetComponent<MapComponent>(map).MapId);
        return result;
    }

    private static int TileCount(TestPair pair, EntityUid hull)
    {
        var grid = pair.Server.EntMan.GetComponent<MapGridComponent>(hull);
        var tiles = pair.Server.System<SharedMapSystem>().GetAllTilesEnumerator(hull, grid);
        var count = 0;
        while (tiles.MoveNext(out _))
            count++;
        return count;
    }
}
