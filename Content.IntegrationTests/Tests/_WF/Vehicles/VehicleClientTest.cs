#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Client._WF.Vehicles.Audio;
using Content.Shared._WF.Vehicles.Audio;
using Content.Shared._WF.Vehicles.Grid.Components;
using Content.Client._WF.CombatConsole;
using Content.Client._WF.Vehicles.Ui;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Content.IntegrationTests.Pair;
using Content.Shared._WF.Vehicles.Base;
using Content.Shared._WF.Vehicles.Core;
using Content.Shared.Input;
using Robust.Client.GameObjects;
using Robust.Client.Input;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using SharedMover = Content.Shared._WF.Vehicles.Grid.GridVehicleMoverSystem;

namespace Content.IntegrationTests.Tests._WF.Vehicles;

/// <summary>What the vehicle module needs from the client: input contexts for the keys its systems bind.</summary>
[TestFixture]
public sealed class VehicleClientTest
{
    [TestCase(20f, true, 0.0, false)]
    [TestCase(19f, true, 0.0, true)]
    [TestCase(5f, true, 0.75, true)]
    [TestCase(4f, true, 0.25, true)]
    [TestCase(4f, true, 0.75, false)]
    [TestCase(4f, false, 0.25, false)]
    public void FuelWarningRespectsPowerAndFlashingThreshold(float fuel, bool powered, double seconds, bool lit)
    {
        Assert.That(WFVehicleLamp.FuelWarningLit(fuel, powered, seconds), Is.EqualTo(lit));
    }

    [Test]
    public void InstrumentSelfTestSweepsBeforeShowingTelemetry()
    {
        Assert.That(WFVehicleInstrumentStartup.Reading(40, 100, 0), Is.Zero);
        Assert.That(WFVehicleInstrumentStartup.Reading(40, 100, 0.8f), Is.EqualTo(100).Within(0.001));
        Assert.That(WFVehicleInstrumentStartup.Reading(40, 100, 1.6f), Is.Zero.Within(0.001));
        Assert.That(WFVehicleInstrumentStartup.Reading(40, 100, 2.2f), Is.EqualTo(40).Within(0.001));
    }

    [Test]
    public async Task EveryProductionHeadlightHasAForwardCone()
    {
        await using var pair = await PoolManager.GetServerClient();
        await pair.Client.WaitAssertion(() =>
        {
            var factory = pair.Client.EntMan.ComponentFactory;
            foreach (var id in new[] { "WFActionVehicleOvercharge", "ActionVehicleToggleView", "ActionVehicleLock",
                         "WFActionVehicleScanner", "WFActionVehicleSteeringMode", "CMUActionVehicleEmergencyLights",
                         "CMUActionCargoVehicleReturn", "CMUActionCargoVehicleToggleBay", "CMUActionCargoVehicleSelfDestruct" })
            {
                var action = pair.Client.ProtoMan.Index<EntityPrototype>(id);
                Assert.That(action.TryGetComponent<Content.Shared.Actions.InstantActionComponent>(out var data, factory), Is.True, id);
                Assert.That(data!.AutoPopulate, Is.False, $"{id} belongs in the vehicle panel, not the hotbar.");
            }
            var checkedVehicles = 0;
            foreach (var proto in pair.Client.ProtoMan.EnumeratePrototypes<EntityPrototype>())
            {
                if (proto.Abstract || VehicleSpawnDefaultsTest.IsTestVehicle(proto.ID) ||
                    !proto.TryGetComponent<VehicleSpotlightComponent>(out _, factory))
                    continue;
                Assert.That(proto.TryGetComponent<PointLightComponent>(out var light, factory), Is.True, proto.ID);
                Assert.That(light!.LightMask?.Id, Is.EqualTo("ConeSingle"), proto.ID);
                Assert.That(light.MaskAutoRotate, Is.True, proto.ID);
                checkedVehicles++;
            }
            Assert.That(checkedVehicles, Is.GreaterThan(10));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task BrakeAudioIgnoresGentleSlowingAndCardinalTravel()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        await pair.Client.WaitAssertion(() =>
        {
            var em = pair.Client.EntMan;
            em.System<SharedMapSystem>().CreateMap(out var map);
            var vehicle = em.SpawnEntity("WFVehicleTestChassis", new Robust.Shared.Map.MapCoordinates(0, 0, map));
            var audio = em.AddComponent<WFVehicleEngineAudioComponent>(vehicle);
            var mover = em.GetComponent<GridVehicleMoverComponent>(vehicle);
            var system = pair.Client.System<WFVehicleAudioSystem>();
            mover.TurnInPlace = false;
            mover.FreeSteering = false;
            mover.CurrentSpeed = 7f;
            audio.LastSpeed = 7f;
            system.FrameUpdate(0.1f);
            Assert.That(audio.SkidCooldown, Is.LessThanOrEqualTo(0f), "Four-way travel is not lateral slip.");
            mover.CurrentSpeed = 5f;
            audio.LastSpeed = 5.2f;
            system.FrameUpdate(0.1f);
            Assert.That(audio.BrakeCooldown, Is.LessThanOrEqualTo(0f), "Gentle slowing should not squeal.");
            audio.LastSpeed = 6f;
            system.FrameUpdate(0.1f);
            Assert.That(audio.BrakeCooldown, Is.EqualTo(10f), "Hard braking should still sound.");
            audio.LastSpeed = 6f;
            system.FrameUpdate(0.1f);
            Assert.That(audio.BrakeCooldown, Is.LessThan(10f), "Continued braking must not retrigger the sound.");
            em.DeleteEntity(vehicle);
            em.System<SharedMapSystem>().DeleteMap(map);
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A vehicle that leaves view is parked in nullspace on the client, and the lobby listens from nullspace: a running
    /// engine left behind would idle over the lobby screen.
    /// </summary>
    [Test]
    public async Task ARunningEngineOutOfViewIsSilent()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        await pair.Client.WaitAssertion(() =>
        {
            var em = pair.Client.EntMan;
            em.System<SharedMapSystem>().CreateMap(out var map);
            var vehicle = em.SpawnEntity("WFVehicleTestChassis", new Robust.Shared.Map.MapCoordinates(0, 0, map));
            var engine = em.EnsureComponent<WFVehicleEngineComponent>(vehicle);
            engine.Electrics = engine.Running = true;
            var audio = em.EnsureComponent<WFVehicleEngineAudioComponent>(vehicle);
            audio.IdleLoop = new Robust.Shared.Audio.SoundPathSpecifier("/Audio/_WF/Vehicles/RCSM/engine/idle_loop.ogg");
            var system = pair.Client.System<WFVehicleAudioSystem>();
            system.FrameUpdate(0.1f);
            Assert.That(audio.IdleStream, Is.Not.Null, "A running engine in view should idle.");

            em.System<SharedTransformSystem>().DetachEntity(vehicle, em.GetComponent<TransformComponent>(vehicle));
            system.FrameUpdate(0.1f);
            Assert.That(audio.IdleStream, Is.Null, "A vehicle parked in nullspace must not be heard.");
            em.DeleteEntity(vehicle);
            em.System<SharedMapSystem>().DeleteMap(map);
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A server delete (a round ending, a wreck cleared) parks the vehicle's local loops in nullspace before the
    /// vehicle goes, while the audio system refuses to stop anything: left there, they idle over the lobby.
    /// </summary>
    [Test]
    public async Task ADeletedVehicleLeavesNoLoopsForTheLobby()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var vehicle = EntityUid.Invalid;
        await server.WaitPost(() =>
        {
            var body = server.EntMan.SpawnEntity("MobObserver", map.GridCoords);
            server.PlayerMan.SetAttachedEntity(pair.Player!, body);
            vehicle = server.EntMan.SpawnEntity("WFVehicleTestChassis", map.GridCoords.Offset(new System.Numerics.Vector2(3f, 0f)));
            var engine = server.EntMan.EnsureComponent<WFVehicleEngineComponent>(vehicle);
            engine.Electrics = engine.Running = true;
            server.EntMan.Dirty(vehicle, engine);
        });
        await pair.RunTicksSync(10);

        var netVehicle = server.EntMan.GetNetEntity(vehicle);
        var streams = new List<EntityUid>();
        await pair.Client.WaitAssertion(() =>
        {
            var em = pair.Client.EntMan;
            Assert.That(em.TryGetEntity(netVehicle, out var seen), Is.True, "The client never saw the vehicle.");
            var audio = em.EnsureComponent<WFVehicleEngineAudioComponent>(seen!.Value);
            audio.IdleLoop = new Robust.Shared.Audio.SoundPathSpecifier("/Audio/_WF/Vehicles/RCSM/engines/patria/idle.ogg");
            audio.TracksLoop = new Robust.Shared.Audio.SoundPathSpecifier("/Audio/_WF/Vehicles/RCSM/wheels/road.ogg");
            pair.Client.System<WFVehicleAudioSystem>().FrameUpdate(0.1f);
            Assert.That(audio.IdleStream, Is.Not.Null, "A running engine in earshot should idle.");
            streams.Add(audio.IdleStream!.Value);
            streams.Add(audio.TracksStream!.Value);
        });

        // The round ends: the vehicle goes while still in view, then the player goes back to the lobby.
        await server.WaitPost(() => server.EntMan.DeleteEntity(vehicle));
        await pair.RunTicksSync(5);
        await server.WaitPost(() => server.PlayerMan.SetAttachedEntity(pair.Player!, null));
        await pair.RunTicksSync(5);

        await pair.Client.WaitAssertion(() =>
        {
            var em = pair.Client.EntMan;
            Assert.That(em.TryGetEntity(netVehicle, out _), Is.False, "The vehicle outlived its server delete.");
            foreach (var stream in streams)
                Assert.That(em.Deleted(stream), Is.True, "A vehicle loop survived its vehicle and plays on in nullspace.");
            var playing = em.EntityQuery<Robust.Shared.Audio.Components.AudioComponent>()
                .Select(audio => audio.FileName)
                .Count(file => file.Contains("/_WF/Vehicles/"));
            Assert.That(playing, Is.Zero, "Vehicle audio is still playing with no vehicle about.");
        });
        await pair.CleanReturnAsync();
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (var child in root.Children)
        {
            yield return child;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }


    [Test]
    public async Task DriverHudAppearsAboveGameplayAndHidesAfterLeaving()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var rig = await VehicleRigs.Build(pair);
        var server = pair.Server;
        var client = pair.Client;
        await server.WaitPost(() =>
        {
            server.EntMan.AddComponent<WFVehicleEngineComponent>(rig.Vehicle);
            server.PlayerMan.SetAttachedEntity(pair.Player!, rig.Driver);
        });
        await VehicleRigs.Seat(rig);
        await pair.RunTicksSync(30);
        await client.WaitAssertion(() =>
        {
            client.System<WFVehicleHudSystem>().FrameUpdate(0f);
            var ui = client.ResolveDependency<IUserInterfaceManager>();
            var panel = ui.PopupRoot.Children.SingleOrDefault(control => control.Name == "WFVehicleHud");
            Assert.That(panel, Is.Not.Null, "The instruments must be above the gameplay viewport, not under it in StateRoot.");
            Assert.That(panel!.Visible, Is.True, "The driver's replicated operator state must reveal the HUD.");
            var controls = Descendants(panel).ToArray();
            Assert.That(controls.OfType<WFVehicleRoundButton>().Any(button => button.Visible && button.Glyph == WFVehicleGlyph.Key), Is.True,
                "The driver needs the key.");
            Assert.That(controls.OfType<WFGlassGauge>().Count(gauge => !gauge.Strip), Is.EqualTo(2));
            Assert.That(controls.OfType<WFGlassGauge>().Count(gauge => gauge.Strip), Is.EqualTo(2));
            Assert.That(controls.OfType<WFGlassGauge>().All(gauge => !gauge.Powered), Is.True,
                "Ignition OFF must suppress illuminated readouts.");
            var hud = client.System<WFVehicleHudSystem>();
            using var weapons = new VehicleWeaponsMenu();
            Assert.That(hud.TryEmbedMenu(weapons, "WEAPONS"), Is.True);
            Assert.That(weapons.IsOpen, Is.True);
            Assert.That(weapons.Visible, Is.False, "Equipment stays folded until its dashboard tab is pressed.");
            Assert.That(Descendants(panel).Contains(weapons), Is.True);
            Assert.That(weapons.Children.Contains(weapons.ContentsContainer), Is.True,
                "The embedded weapon selector must retain its contents without window chrome.");
            weapons.Close();
            using var radar = new Content.Client.Shuttles.UI.RadarConsoleWindow();
            Assert.That(hud.TryEmbedMenu(radar, "RADAR"), Is.True);
            Assert.That(Descendants(panel).Contains(radar), Is.True);
            radar.Close();
            Assert.That(Descendants(panel).Contains(weapons), Is.False);
            var dashboard = (WFVehicleDashboard) panel;
            foreach (var viewport in new[] { new System.Numerics.Vector2(1280, 720), new System.Numerics.Vector2(2048, 1080),
                         new System.Numerics.Vector2(2560, 1440) })
            {
                var hotbar = Robust.Shared.Maths.UIBox2.FromDimensions(new System.Numerics.Vector2((viewport.X - 760) / 2, viewport.Y - 80),
                    new System.Numerics.Vector2(760, 80));
                dashboard.Fit(viewport, hotbar);
                dashboard.Measure(viewport);
                dashboard.Arrange(Robust.Shared.Maths.UIBox2.FromDimensions(System.Numerics.Vector2.Zero, viewport));
                foreach (var (wing, bank) in new[] { (dashboard.LeftBounds, dashboard.LeftBank), (dashboard.RightBounds, dashboard.RightBank) })
                {
                    Assert.That(wing.Right <= hotbar.Left || wing.Left >= hotbar.Right || wing.Bottom <= hotbar.Top, Is.True,
                        $"{viewport}: a wing covers the hand slots.");
                    Assert.That(wing.Left >= 0 && wing.Top >= 0 && wing.Right <= viewport.X, Is.True, $"{viewport}: a wing is off screen.");
                    Assert.That(wing.Height, Is.LessThanOrEqualTo(190), $"{viewport}: the dashboard takes too much of the screen.");
                    Assert.That(bank.DesiredSize.Y, Is.LessThanOrEqualTo(bank.Size.Y + 0.5f), $"{viewport}: a wing's instruments overflow it.");
                }
                Assert.That(dashboard.LeftBounds.Right, Is.LessThan(dashboard.RightBounds.Left));
                Assert.That(controls.OfType<WFGlassGauge>().All(gauge => gauge.Reading.Value == 0), Is.True,
                    "Unpowered needles park at zero instead of losing their dial.");
            }
            Assert.That(controls.OfType<WFVehicleRoundButton>().Where(button => button.Visible)
                .All(button => button.Width >= WFVehicleRoundButton.Size - 0.5f), Is.True, "A round button was squeezed.");
        });
        await server.WaitAssertion(() =>
            Assert.That(server.System<RMCVehicleSystem>().TryRemoveOperator(new Entity<RMCVehicleOperatorComponent?>(rig.Driver, null)), Is.True));
        await pair.RunTicksSync(30);
        await client.WaitAssertion(() =>
        {
            client.System<WFVehicleHudSystem>().FrameUpdate(0f);
            Assert.That(client.ResolveDependency<IUserInterfaceManager>().PopupRoot.Children
                .Any(control => control.Name == "WFVehicleHud"), Is.False, "Leaving the seat must close its instrument window.");
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A vehicle nobody sits inside is driven by a session attached to the vehicle itself (the Bison). The client picks the
    /// input context from the attached entity, and with none it falls back to "common", which has no use-item key: the
    /// horn, bound to that key, could not be sounded.
    /// </summary>
    [Test]
    public async Task AVehicleDrivenFromItselfHasAContextWithTheHornKey()
    {
        await using var pair = await PoolManager.GetServerClient();
        var client = pair.Client;
        var checkedVehicles = new List<string>();

        await client.WaitAssertion(() =>
        {
            var factory = client.EntMan.ComponentFactory;
            var contexts = client.ResolveDependency<IInputManager>().Contexts;

            foreach (var proto in client.ProtoMan.EnumeratePrototypes<EntityPrototype>())
            {
                // A vehicle with a doorway has its driver in a body of their own, which has a context.
                if (proto.Abstract ||
                    VehicleSpawnDefaultsTest.IsTestVehicle(proto.ID) ||
                    !proto.TryComp<RMCVehicleComponent>(out _, factory) ||
                    proto.TryComp<VehicleEnterComponent>(out _, factory) ||
                    !proto.TryComp<VehicleSoundComponent>(out var sound, factory) ||
                    sound.HornSound == null)
                {
                    continue;
                }

                checkedVehicles.Add(proto.ID);

                if (!proto.TryComp<InputComponent>(out var input, factory))
                {
                    Assert.Fail($"{proto.ID} has a horn but no input context, so its pilot's use-item key never reaches it.");
                    continue;
                }

                Assert.That(
                    contexts.GetContext(input.ContextName).FunctionExistsHierarchy(ContentKeyFunctions.UseItemInHand),
                    Is.True,
                    $"{proto.ID} names the input context \"{input.ContextName}\", which has no use-item key to sound the horn with.");
            }
        });

        Assert.That(checkedVehicles, Does.Contain("CMUCargoCarrier"));
        await pair.CleanReturnAsync();
    }
}

/// <summary>
/// The debug overlay commands set static flags on the shared mover, which every pair in the process shares, so this runs
/// with nothing else.
/// </summary>
[TestFixture, NonParallelizable]
public sealed class VehicleDebugFlagsTest
{
    /// <summary>
    /// A disconnect shuts the client's mover system down and builds a new one with every overlay off. The shared mover's
    /// flags must go off with the old one, or it would take its debug path for ever with nothing to show for it.
    /// </summary>
    [Test]
    public async Task ShuttingTheClientMoverDownSwitchesTheSharedDebugFlagsOff()
    {
        // The client has no systems until it has joined a game.
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var client = pair.Client;

        await client.WaitAssertion(() =>
        {
            var mover = client.System<Content.Client._WF.Vehicles.Grid.GridVehicleMoverSystem>();

            try
            {
                Assert.That(mover.ToggleCollisionOverlay(), Is.True);
                Assert.That(mover.ToggleMovementOverlay(), Is.True);
                SharedMover.DebugTestedTiles.Add((EntityUid.Invalid, default));
                Assert.Multiple(() =>
                {
                    Assert.That(SharedMover.CollisionDebugEnabled, Is.True, "Test setup: the collision flag should be on.");
                    Assert.That(SharedMover.MovementDebugEnabled, Is.True, "Test setup: the movement flag should be on.");
                });

                mover.Shutdown();

                Assert.Multiple(() =>
                {
                    Assert.That(SharedMover.CollisionDebugEnabled, Is.False, "The collision flag outlived the system.");
                    Assert.That(SharedMover.MovementDebugEnabled, Is.False, "The movement flag outlived the system.");
                    Assert.That(SharedMover.DebugTestedTiles, Is.Empty, "Debug data outlived the system.");
                });

                // The system is the same object for the rest of this pair's life; it starts again with its overlays off.
                Assert.That(mover.ToggleCollisionOverlay(), Is.True, "The overlay did not start from off after a shutdown.");
            }
            finally
            {
                mover.Shutdown();
            }
        });

        await pair.CleanReturnAsync();
    }
}
