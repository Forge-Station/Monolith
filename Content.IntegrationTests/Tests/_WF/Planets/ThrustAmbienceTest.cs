#nullable enable
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared.DeviceLinking.Events;
using Content.Shared.Movement.Systems;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Audio.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.Planets;

/// <summary>Thrust ambience follows engines that are actually firing, rather than pilot input.</summary>
[TestFixture]
[TestOf(typeof(Content.Server._WF.Planets.Flight.WFThrustAmbienceSystem))]
public sealed class ThrustAmbienceTest
{
    private const string ThrustClip = "/Audio/_WF/Planets/Flight/thrust_loop.ogg";

    [Test]
    public async Task HeldInputDoesNotPlayForDisabledOrUnpoweredThrusters()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var thrusters = server.System<ThrusterSystem>();
        var receiver = server.System<SharedPowerReceiverSystem>();

        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var orbit = layers[^1];
        var orbitMap = MapId.Nullspace;
        await server.WaitPost(() => orbitMap = entMan.GetComponent<MapComponent>(orbit).MapId);

        var hull = await BuildHull(pair, orbitMap);
        await MapInitHull(pair, hull);

        await server.WaitPost(() =>
        {
            // Every engine must be disabled: leaving one enabled lets held input create a real firing state.
            foreach (var uid in Children(entMan, hull))
            {
                if (!entMan.TryGetComponent<ThrusterComponent>(uid, out var thruster))
                    continue;

                var signal = new SignalReceivedEvent(thruster.OffPort);
                entMan.EventBus.RaiseLocalEvent(uid, ref signal);
                receiver.SetNeedsPower(uid, true);
                receiver.SetLoad(entMan.GetComponent<ApcPowerReceiverComponent>(uid), 1000f);
            }

            var console = FindShuttleConsole(entMan, hull);
            var pilot = entMan.SpawnEntity(ViewerProto, new EntityCoordinates(hull, Vector2.Zero));
            var pilotComp = entMan.EnsureComponent<PilotComponent>(pilot);
            pilotComp.Console = console;
            pilotComp.HeldButtons = ShuttleButtons.StrafeRight;
        });

        await server.WaitRunTicks(pair.SecondsToTicks(1f));
        await server.WaitAssertion(() =>
        {
            foreach (var uid in Children(entMan, hull))
            {
                if (!entMan.TryGetComponent<ThrusterComponent>(uid, out var thruster))
                    continue;

                Assert.That(thruster.Enabled, Is.False,
                    "The thruster off signal did not disable the engine.");
                Assert.That(entMan.TryGetComponent<ApcPowerReceiverComponent>(uid, out var power), Is.True,
                    "The fixture thruster has no power receiver for the unpowered precondition.");
                Assert.That(power!.Powered, Is.False,
                    "The negative case did not leave the fixture thruster unpowered.");
            }
        });
        await server.WaitAssertion(() =>
            Assert.That(CountThrustStreams(entMan), Is.Zero,
                "Pilot input started ambience while every linear thruster was disabled and unpowered."));

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RealFiringWithoutPilotStopsAndCleansUp()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var thrusters = server.System<ThrusterSystem>();
        var receiver = server.System<SharedPowerReceiverSystem>();

        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var orbit = layers[^1];
        var orbitMap = MapId.Nullspace;
        await server.WaitPost(() => orbitMap = entMan.GetComponent<MapComponent>(orbit).MapId);

        var hull = await BuildHull(pair, orbitMap);
        await MapInitHull(pair, hull);

        await server.WaitPost(() =>
        {
            foreach (var uid in Children(entMan, hull))
            {
                if (!entMan.TryGetComponent<ThrusterComponent>(uid, out var thruster))
                    continue;

                receiver.SetNeedsPower(uid, false);

                if (!thruster.IsOn && thrusters.CanEnable(uid, thruster))
                    thrusters.EnableThruster(uid, thruster);
            }

            var shuttle = entMan.GetComponent<ShuttleComponent>(hull);
            thrusters.EnableLinearThrustDirection(shuttle, DirectionFlag.East);
        });

        await server.WaitRunTicks(pair.SecondsToTicks(1f));
        await server.WaitAssertion(() =>
            Assert.That(CountThrustStreams(entMan), Is.EqualTo(1),
                "A powered linear direction fired without a pilot but did not start ambience."));

        await server.WaitPost(() =>
            thrusters.DisableLinearThrustDirection(entMan.GetComponent<ShuttleComponent>(hull), DirectionFlag.East));
        await server.WaitRunTicks(pair.SecondsToTicks(1f));
        await server.WaitAssertion(() =>
            Assert.That(CountThrustStreams(entMan), Is.Zero,
                "The thrust loop continued after ThrusterSystem stopped the firing direction."));

        await server.WaitPost(() =>
            thrusters.EnableLinearThrustDirection(entMan.GetComponent<ShuttleComponent>(hull), DirectionFlag.East));
        await server.WaitRunTicks(pair.SecondsToTicks(1f));
        await server.WaitAssertion(() => Assert.That(CountThrustStreams(entMan), Is.EqualTo(1)));

        await server.WaitPost(() => entMan.DeleteEntity(hull));
        await server.WaitRunTicks(pair.SecondsToTicks(1f));
        await server.WaitAssertion(() =>
            Assert.That(CountThrustStreams(entMan), Is.Zero,
                "Deleting a grid with an active ambience component left its stream alive."));

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    private static int CountThrustStreams(IEntityManager entMan)
    {
        var count = 0;
        var query = entMan.EntityQueryEnumerator<AudioComponent>();
        while (query.MoveNext(out _, out var audio))
        {
            if (audio.FileName == ThrustClip)
                count++;
        }

        return count;
    }
}
