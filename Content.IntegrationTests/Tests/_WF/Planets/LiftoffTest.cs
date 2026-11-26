#nullable enable
using Robust.Shared.Map.Components;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._CE.ZLevels.Core;
using Content.Server._WF.Planets.Flight;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Shuttles.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.Planets;

/// <summary>Grounded planet ascent through the shuttle console's latched Liftoff control.</summary>
[TestFixture]
[TestOf(typeof(CEZLevelsSystem))]
public sealed class LiftoffTest
{
    /// <summary>The latch drives the existing CE spool and every upward transit until the hull reaches orbit.</summary>
    [Test]
    public async Task LatchedLiftoffClimbsFromGroundIntoTheAir()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var levels = server.System<CEZLevelsSystem>();

        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var ground = layers[0];
        var air = layers[1];
        var orbit = layers[^1];
        await LayTiles(pair, ground, new Vector2i(-2, -2), new Vector2i(18, 18));

        var hull = await BuildHull(pair, await MapIdOf(pair, ground));
        await MapInitHull(pair, hull);
        await AddLandingThrusters(pair, hull, 3);
        var pilot = await HoldVertical(pair, hull, ShuttleButtons.None);
        var console = FindShuttleConsole(entMan, hull);

        await server.WaitAssertion(() =>
            Assert.That(levels.WfTryBeginLiftoff(hull, console, pilot, out var reason), Is.True,
                $"A grounded, powered hull was refused liftoff: {reason}"));

        var reached = false;
        var trail = "";
        for (var second = 0; second < 45 && !reached; second++)
        {
            await server.WaitRunTicks(pair.SecondsToTicks(1f));
            await server.WaitPost(() =>
            {
                var x = entMan.GetComponent<TransformComponent>(hull);
                var z = entMan.GetComponent<Content.Shared._CE.ZLevels.Core.Components.CEZPhysicsComponent>(hull);
                trail += $" [{second}: map={x.MapUid} h={z.LocalPosition:F2} v={z.Velocity:F2} latch={entMan.HasComponent<WFLiftoffComponent>(hull)}]";
                reached = x.MapUid == air;
            });
        }

        Assert.That(reached, Is.True, $"The latched liftoff never carried the hull off the ground into the air. ground={ground} air={air} orbit={orbit}{trail}");
        await server.WaitRunTicks(2);
        await server.WaitAssertion(() =>
            Assert.That(entMan.HasComponent<WFLiftoffComponent>(hull), Is.False,
                "The liftoff latch remained engaged once the hull was airborne."));

        // With nobody holding the key, the hull stays in the air layer.
        await server.WaitRunTicks(pair.SecondsToTicks(8f));
        await server.WaitAssertion(() =>
            Assert.That(entMan.GetComponent<TransformComponent>(hull).MapUid, Is.Not.EqualTo(orbit),
                "Liftoff carried the hull all the way to orbit."));

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>A grounded ascend key is consumed; it neither starts the spool nor creates a latch.</summary>
    [Test]
    public async Task GroundedAscendKeyDoesNotStartLiftoff()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var ground = layers[0];
        await LayTiles(pair, ground, new Vector2i(-2, -2), new Vector2i(18, 18));

        var hull = await BuildHull(pair, await MapIdOf(pair, ground));
        await MapInitHull(pair, hull);
        await AddLandingThrusters(pair, hull, 3);
        await HoldVertical(pair, hull, ShuttleButtons.AscendZ);
        await server.WaitRunTicks(pair.SecondsToTicks(3f));

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.GetComponent<TransformComponent>(hull).MapUid, Is.EqualTo(ground),
                    "Grounded R bypassed the Liftoff button.");
                Assert.That(entMan.HasComponent<WFLiftoffComponent>(hull), Is.False,
                    "Manual ascend created a liftoff latch.");
            }
        });

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>Manual descend cancels an engaged latch before the normal CE input pass can spool upward.</summary>
    [Test]
    public async Task ManualDescendCancelsLatchedLiftoff()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var levels = server.System<CEZLevelsSystem>();

        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var ground = layers[0];
        await LayTiles(pair, ground, new Vector2i(-2, -2), new Vector2i(18, 18));

        var hull = await BuildHull(pair, await MapIdOf(pair, ground));
        await MapInitHull(pair, hull);
        await AddLandingThrusters(pair, hull, 3);
        var pilot = await HoldVertical(pair, hull, ShuttleButtons.None);
        var console = FindShuttleConsole(entMan, hull);

        await server.WaitAssertion(() =>
            Assert.That(levels.WfTryBeginLiftoff(hull, console, pilot, out _), Is.True));
        await server.WaitPost(() =>
            entMan.GetComponent<PilotComponent>(pilot).HeldButtons = ShuttleButtons.DescendZ);
        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.HasComponent<WFLiftoffComponent>(hull), Is.False,
                    "Manual descend left the liftoff latch engaged.");
                Assert.That(entMan.GetComponent<TransformComponent>(hull).MapUid, Is.EqualTo(ground),
                    "The cancelled latch still launched the hull.");
            }
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
}
