#nullable enable
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._WF.Planets.Flight;
using Content.Server.Shuttles.Components;
using Content.Shared._WF.Planets;
using Content.Shared.Movement.Systems;
using Content.Shared.Shuttles.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.Planets;

/// <summary>Planet drag: flight on a planet layer is capped and damped; damping is restored off it.</summary>
[TestFixture]
[TestOf(typeof(WFPlanetDragSystem))]
public sealed class PlanetDragTest
{
    /// <summary>Slack allowed over the layer's cap when sampling, in m/s, since the clamp runs once a tick.</summary>
    private const float SpeedSlack = 0.75f;

    /// <summary>A shove well past any cap, as a collision or a blast would deliver one.</summary>
    private const float Shove = 40f;

    /// <summary>A hull in orbit stays under the orbit cap whether shoved or under thrust.</summary>
    [Test]
    public async Task ThrustingHullStaysUnderTheOrbitCap()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var physics = server.System<SharedPhysicsSystem>();

        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var orbit = layers[^1];
        var orbitMap = await MapIdOf(pair, orbit);

        var hull = await BuildHull(pair, orbitMap);
        await MapInitHull(pair, hull);

        var cap = 0f;
        await server.WaitPost(() => cap = entMan.GetComponent<WFOrbitLayerComponent>(orbit).MaxSpeed);

        await server.WaitPost(() => physics.SetLinearVelocity(hull, new Vector2(Shove, 0f)));
        await server.WaitRunTicks(pair.SecondsToTicks(1f));

        await server.WaitAssertion(() =>
        {
            Assert.That(Speed(entMan, hull), Is.LessThanOrEqualTo(cap + SpeedSlack),
                "A hull shoved across the orbit layer kept the speed it was given.");
        });

        await HoldThrust(pair, hull);

        // The cap has to hold on every tick the thrusters fire.
        var fastest = 0f;

        for (var i = 0; i < 12; i++)
        {
            await server.WaitRunTicks(pair.SecondsToTicks(0.25f));
            await server.WaitPost(() => fastest = MathF.Max(fastest, Speed(entMan, hull)));
        }

        await server.WaitAssertion(() =>
        {
            Assert.That(fastest, Is.LessThanOrEqualTo(cap + SpeedSlack),
                $"A thrusting hull got to {fastest:F2} m/s in orbit, over the layer's {cap:F2} m/s cap.");
        });

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>Leaving the layer restores the grid's own damping.</summary>
    [Test]
    public async Task DampingIsRestoredWhenTheHullLeavesTheLayer()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var transform = server.System<SharedTransformSystem>();

        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var orbit = layers[^1];

        var origin = await pair.CreateTestMap();
        var hull = await BuildHull(pair, origin.MapId);
        await MapInitHull(pair, hull);

        var own = 0f;
        var orbitDamping = 0f;

        await server.WaitPost(() =>
        {
            own = entMan.GetComponent<PhysicsComponent>(hull).LinearDamping;
            orbitDamping = entMan.GetComponent<WFOrbitLayerComponent>(orbit).LinearDamping;
        });

        await server.WaitPost(() => transform.SetCoordinates(hull, new EntityCoordinates(orbit, Vector2.Zero)));
        await server.WaitRunTicks(pair.SecondsToTicks(1f));

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.GetComponent<PhysicsComponent>(hull).LinearDamping,
                    Is.EqualTo(orbitDamping).Within(0.001f), "The orbit layer's damping never reached the hull.");
                Assert.That(entMan.HasComponent<WFPlanetDragComponent>(hull), Is.True,
                    "The hull is under the layer's damping with nothing remembering its own.");
            }
        });

        await server.WaitPost(() => transform.SetCoordinates(hull, new EntityCoordinates(origin.MapUid, Vector2.Zero)));
        await server.WaitRunTicks(pair.SecondsToTicks(1f));

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.GetComponent<PhysicsComponent>(hull).LinearDamping, Is.EqualTo(own).Within(0.001f),
                    "The hull left the planet still dragging the layer's damping behind it.");
                Assert.That(entMan.HasComponent<WFPlanetDragComponent>(hull), Is.False,
                    "The drag loan outlived the layer it was taken against.");
            }
        });

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    /// <summary>Seats a pilot at the hull's own console holding the forward thrust key.</summary>
    private static async Task HoldThrust(TestPair pair, EntityUid hull)
    {
        var server = pair.Server;
        var entMan = server.EntMan;

        await server.WaitPost(() =>
        {
            var console = FindShuttleConsole(entMan, hull);

            Assert.That(console, Is.Not.EqualTo(EntityUid.Invalid), "The hull has no shuttle console to pilot from.");

            var pilot = entMan.SpawnEntity(ViewerProto, new EntityCoordinates(hull, new Vector2(2.5f, 3.5f)));
            var pilotComp = entMan.EnsureComponent<PilotComponent>(pilot);
            pilotComp.Console = console;
            pilotComp.HeldButtons = ShuttleButtons.StrafeUp;
        });

        await server.WaitRunTicks(1);
    }

    /// <summary>A grid's planar speed right now.</summary>
    private static float Speed(IEntityManager entMan, EntityUid grid)
    {
        return entMan.GetComponent<PhysicsComponent>(grid).LinearVelocity.Length();
    }

    /// <summary>The map id of a layer, which is what a grid is built on.</summary>
    private static async Task<MapId> MapIdOf(TestPair pair, EntityUid layer)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var mapId = MapId.Nullspace;

        await server.WaitPost(() => mapId = entMan.GetComponent<MapComponent>(layer).MapId);
        return mapId;
    }
}
