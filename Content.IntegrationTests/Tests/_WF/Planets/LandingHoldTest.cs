#nullable enable
using System.Numerics;
using Content.IntegrationTests.Tests._WF.Caverns;
using Content.Server._CE.ZLevels.Core;
using Content.Server._CE.ZLevels.Core.Components;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Shuttles.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.Planets;

/// <summary>
/// A hull with lift only comes down on a planet's ground when its pilot holds descend, and however it comes down it
/// isn't left wedged on anything.
/// </summary>
[TestFixture]
[TestOf(typeof(CEZLevelsSystem))]
public sealed class LandingHoldTest
{
    [TestCase(0.2f, 0f)]
    [TestCase(0.6f, 0.5f)]
    public async Task AnIdleHullHoldsOverTheGroundUntilDescendIsHeld(float progress, float speed)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var levels = server.System<CEZLevelsSystem>();

        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var ground = layers[0];
        await LayTiles(pair, ground, new Vector2i(-32, -32), new Vector2i(64, 64));

        var hull = await BuildHull(pair, await CavernFixture.MapIdOf(pair, ground));
        await MapInitHull(pair, hull);
        await AddLandingThrusters(pair, hull, 3);
        var pilot = await HoldVertical(pair, hull, ShuttleButtons.None);

        await server.WaitAssertion(() =>
        {
            Assert.That(levels.WfTryGetLiftRatio(hull, out var ratio) && ratio > 1f, Is.True, "Precondition: the hull has no spare lift.");
            Assert.That(levels.TryEnterTransit((hull, entMan.GetComponent<MapGridComponent>(hull)), progress, preferUpperGap: true), Is.True);
            entMan.GetComponent<CEZGridFallerComponent>(hull).Velocity = speed;
        });

        await server.WaitRunTicks(pair.SecondsToTicks(8));

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.HasComponent<CEZTransitMapComponent>(entMan.GetComponent<TransformComponent>(hull).MapUid), Is.True,
                "An idle hull came down on the ground by itself.");
            Assert.That(entMan.GetComponent<CEZGridFallerComponent>(hull).Velocity, Is.Zero, "An idle hull did not come to a stop.");
            entMan.GetComponent<PilotComponent>(pilot).HeldButtons = ShuttleButtons.DescendZ;
        });

        await server.WaitRunTicks(pair.SecondsToTicks(10));

        await server.WaitAssertion(() =>
            Assert.That(entMan.GetComponent<TransformComponent>(hull).MapUid, Is.EqualTo(ground), "Holding descend did not set the hull down."));

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>A placed tree, which the biome knows nothing of, is broken if the hull comes down overlapping it.</summary>
    [Test]
    public async Task ALandingBreaksWhatItWouldBeWedgedOn()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var levels = server.System<CEZLevelsSystem>();

        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var ground = layers[0];
        await LayTiles(pair, ground, new Vector2i(-8, -8), new Vector2i(24, 24));

        // Spans 0.3 to 5.3 across, so the tree's tile at 5 to 6 overlaps the hull's edge without its trunk under the deck.
        var hull = await BuildDebris(pair, await CavernFixture.MapIdOf(pair, ground), size: 5, offset: new Vector2(0.3f, -0.15f));
        await MapInitHull(pair, hull);

        var tree = EntityUid.Invalid;
        var underWall = EntityUid.Invalid;
        var clearWall = EntityUid.Invalid;

        await server.WaitAssertion(() =>
        {
            var grid = entMan.GetComponent<MapGridComponent>(hull);
            Assert.That(levels.TryEnterTransit((hull, grid), preferUpperGap: true), Is.True, "Precondition: the hull would not lift off.");

            tree = entMan.SpawnEntity("FloraTree", new EntityCoordinates(ground, new Vector2(5.5f, 2.5f)));
            server.System<SharedTransformSystem>().AnchorEntity(tree);
            underWall = entMan.SpawnEntity("WallSolid", new EntityCoordinates(ground, new Vector2(2.5f, 2.5f)));
            clearWall = entMan.SpawnEntity("WallSolid", new EntityCoordinates(ground, new Vector2(2.5f, 5.5f)));

            // What a lift-lost hull does: the hold-off is never asked.
            Assert.That(levels.TryExitTransit((hull, grid)), Is.True, "Precondition: the hull would not set down.");
        });

        await server.WaitRunTicks(10);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(entMan.Deleted(tree), Is.True, "A placed tree the hull overlaps is still standing.");
                Assert.That(entMan.Deleted(underWall), Is.True, "A wall under the hull is still standing.");
                Assert.That(entMan.Deleted(clearWall), Is.False, "A wall clear of the hull was broken.");
            });
        });

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>Pinned between two walls by different depths, a hull stays put instead of being shoved back and forth.</summary>
    [Test]
    public async Task AHullPinnedBetweenWallsDoesNotShake()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var transform = server.System<SharedTransformSystem>();

        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var ground = layers[0];
        await LayTiles(pair, ground, new Vector2i(-8, -8), new Vector2i(24, 24));

        // Built away from the walls so they aren't crushed or bounced off before it is moved in between them.
        var hull = await BuildDebris(pair, await CavernFixture.MapIdOf(pair, layers[^1]), size: 3);
        await MapInitHull(pair, hull);

        await server.WaitAssertion(() =>
        {
            // The hull spans 1.7 to 4.7 up, overlapping the wall below by 0.3 and the one above by 0.7.
            entMan.SpawnEntity("WallSolid", new EntityCoordinates(ground, new Vector2(2.5f, 1.5f)));
            entMan.SpawnEntity("WallSolid", new EntityCoordinates(ground, new Vector2(2.5f, 4.5f)));
            transform.SetCoordinates(hull, new EntityCoordinates(ground, new Vector2(1f, 1.7f)));
            entMan.EnsureComponent<CEZGridFallerComponent>(hull);
            server.System<SharedPhysicsSystem>().SetBodyType(hull, BodyType.Dynamic);
        });

        await server.WaitRunTicks(2);

        var last = Vector2.Zero;
        await server.WaitAssertion(() => last = transform.GetWorldPosition(hull));

        for (var tick = 0; tick < 10; tick++)
        {
            await server.WaitRunTicks(1);
            await server.WaitAssertion(() =>
            {
                var now = transform.GetWorldPosition(hull);
                Assert.That((now - last).Length(), Is.LessThan(0.01f), $"The hull jumped from {last} to {now}.");
                last = now;
            });
        }

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }
}
