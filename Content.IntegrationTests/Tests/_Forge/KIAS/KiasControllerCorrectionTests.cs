#pragma warning disable RA0002
using System.Collections.Generic;
using System.Linq;
using Content.Client.UserInterface.Systems.Chat;
using Content.Server._Forge.KIAS;
using Content.Server._Forge.KIAS.Controllers;
using Content.Server.DeviceLinking.Systems;
using Content.Server.Power.Components;
using Content.Shared._Forge.KIAS;
using Content.Shared._Forge.KIAS.Controllers;
using Content.Shared.Chat;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Atmos.Monitor;
using Content.Shared.Access.Components;
using Content.Shared.DeviceNetwork;
using Content.Server.Atmos.Components;
using Content.Server.Atmos.Monitor.Components;
using Content.Server.Atmos.Piping.Components;
using Robust.Shared.Prototypes;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.DeviceNetwork.Components;
using Content.Shared.Interaction;
using Content.Shared.Mind;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Light.Components;
using Robust.Client.UserInterface;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._Forge.KIAS;

[TestFixture]
public sealed class KiasControllerCorrectionTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task LightingProfilesRestoreIntegratedFixtures(bool direct)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        var fixtures = new List<EntityUid>();
        EntityUid rack = default, card = default, cable = default, group = default;
        await pair.Server.WaitAssertion(() =>
        {
            var coordinates = new EntityCoordinates(map.Grid, .5f, .5f);
            EntityUid Spawn(string id)
            {
                var uid = em.SpawnEntity(id, coordinates);
                em.System<SharedPowerReceiverSystem>().SetNeedsPower(uid, false);
                return uid;
            }
            var actor = Spawn("MobHuman");
            Spawn("KiasCore"); cable = Spawn("KiasDataCable");
            rack = Spawn("KiasControllerRack"); group = Spawn("KiasLightController");
            em.System<KiasSystem>().Rebuild(map.Grid);
            for (var i = 0; i < 2; i++)
            {
                var fixture = Spawn("Poweredlight"); fixtures.Add(fixture);
                var kit = Spawn("KiasIntegrationKit");
                var install = new AfterInteractEvent(actor, kit, fixture, coordinates, true);
                em.EventBus.RaiseLocalEvent(kit, install);
                Assert.That(install.Handled, Is.True);
                em.EnsureComponent<KiasLightGroupComponent>(fixture).Group = "CABIN";
            }
            card = Spawn("KiasProgrammableController");
            var program = em.GetComponent<KiasControllerCardComponent>(card).Program;
            program.Nodes.Add(new() { Id = 1, Kind = KiasNodeKind.BoolConstant });
            program.Nodes.Add(new() { Id = 2, Kind = KiasNodeKind.All, Profile = direct ? "Lighting" : "LightController" });
            program.Wires.Add(new() { FromNode = 1, FromPort = "Value", ToNode = 2, ToPort = "Set" });
        });
        await pair.RunTicksSync(30);
        await pair.Server.WaitAssertion(() =>
        {
            em.System<KiasSystem>().Rebuild(map.Grid);
            var io = em.System<KiasControllerIoSystem>();
            Assert.That(io.Devices(map.Grid, "Lighting"), Is.EquivalentTo(fixtures));
            Assert.That(io.Devices(map.Grid, "LightController"), Is.EquivalentTo(new[] { group }));
            Assert.That(em.System<ItemSlotsSystem>().TryInsert(rack, KiasControllerRackComponent.SlotId(0), card, null), Is.True);
        });
        await pair.RunTicksSync(40);
        await pair.Server.WaitAssertion(() =>
        {
            foreach (var fixture in fixtures)
            {
                if (direct) Assert.That(em.GetComponent<ApcPowerReceiverComponent>(fixture).Powered, Is.False);
                Assert.That(em.GetComponent<PoweredLightComponent>(fixture).On, Is.False);
                Assert.That(em.System<KiasSystem>().IsOnline(fixture), Is.True);
            }
            var component = em.GetComponent<KiasControllerCardComponent>(card);
            Assert.That(em.System<ItemSlotsSystem>().TryEject(rack, KiasControllerRackComponent.SlotId(0), null, out _), Is.True);
            component.Program.Nodes[0].Config.Bool = true; component.Revision++;
            Assert.That(em.System<ItemSlotsSystem>().TryInsert(rack, KiasControllerRackComponent.SlotId(0), card, null), Is.True);
        });
        await pair.RunTicksSync(40);
        await pair.Server.WaitAssertion(() =>
        {
            foreach (var fixture in fixtures)
            {
                Assert.That(em.GetComponent<ApcPowerReceiverComponent>(fixture).Powered, Is.True);
                Assert.That(em.GetComponent<PoweredLightComponent>(fixture).On, Is.True);
            }
            em.System<SharedPowerReceiverSystem>().SetPowerDisabled(group, true);
        });
        await pair.RunTicksSync(40);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.System<KiasSystem>().IsOnline(group), Is.False);
            em.DeleteEntity(cable); em.System<KiasSystem>().Rebuild(map.Grid);
            foreach (var fixture in fixtures) Assert.That(em.System<KiasSystem>().IsOnline(fixture), Is.False);
        });
        await pair.CleanReturnAsync();
    }

    [TestCase(false, "graph-speaker-broadcast")]
    [TestCase(true, "graph-speaker-broadcast")]
    [TestCase(false, "Внимание! В отсеке № 3 опасная фауна. Ёж, щит, подъём.")]
    [TestCase(true, "Внимание! В отсеке № 3 опасная фауна. Ёж, щит, подъём.")]
    public async Task GraphSelectorsSpeakFromEverySelectedSpeakerAndKeepCooldown(bool broadcast, string text)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        var received = new List<ChatMessage>();
        EntityUid rack = default, card = default, programmer = default, actor = default;
        NetEntity programmerNet = default;
        var speakers = new List<NetEntity>();
        var excluded = new List<NetEntity>();
        await pair.Client.WaitAssertion(() => pair.Client.ResolveDependency<IUserInterfaceManager>().GetUIController<ChatUIController>().MessageAdded += received.Add);
        await pair.Server.WaitAssertion(() =>
        {
            var coordinates = new EntityCoordinates(map.Grid, .5f, .5f);
            EntityUid Spawn(string id)
            {
                var uid = em.SpawnEntity(id, coordinates);
                em.RemoveComponent<ApcPowerReceiverComponent>(uid);
                return uid;
            }
            var session = pair.Server.ResolveDependency<IPlayerManager>().Sessions.Single();
            var mind = em.System<SharedMindSystem>().CreateMind(session.UserId);
            actor = Spawn("MobHuman");
            em.System<SharedMindSystem>().TransferTo(mind, actor);
            Spawn("KiasCore"); Spawn("KiasDataCable");
            rack = Spawn("KiasControllerRack");
            programmer = Spawn("KiasControllerProgrammer"); programmerNet = em.GetNetEntity(programmer);
            for (var i = 0; i < 3; i++)
            {
                var speaker = Spawn("KiasSpeaker");
                em.GetComponent<KiasSpeakerComponent>(speaker).Group = i == 2 ? "OTHER" : "TEST";
                if (i == 0 || broadcast && i == 1) speakers.Add(em.GetNetEntity(speaker));
                else excluded.Add(em.GetNetEntity(speaker));
            }
            card = Spawn("KiasProgrammableController");
            var program = em.GetComponent<KiasControllerCardComponent>(card).Program;
            program.Nodes.AddRange(new KiasControllerNode[]
            {
                new() { Id = 1, Kind = KiasNodeKind.StringConstant, Config = new() { Text = string.Empty } },
                new() { Id = 2, Kind = KiasNodeKind.OnStart },
                new() { Id = 3, Kind = broadcast ? KiasNodeKind.All : KiasNodeKind.Specific,
                    Profile = "Speaker", Binding = broadcast ? null : em.GetEntity(speakers[0]), Group = broadcast ? "TEST" : "STALE" }
            });
            program.Wires.Add(new() { FromNode = 1, FromPort = "Value", ToNode = 3, ToPort = "Message" });
            program.Wires.Add(new() { FromNode = 2, FromPort = "Started", ToNode = 3, ToPort = "Announce" });
            em.System<KiasSystem>().Rebuild(map.Grid);
            Assert.That(em.System<ItemSlotsSystem>().TryInsert(programmer, KiasControllerProgrammerComponent.SlotId, card, actor), Is.True);
            Assert.That(em.System<Robust.Server.GameObjects.UserInterfaceSystem>().TryOpenUi(programmer, KiasControllerUiKey.Programmer, actor), Is.True);
        });
        await pair.RunTicksSync(20);
        async Task ClientEdit(KiasGraphEdit action) => await pair.Client.WaitAssertion(() =>
        {
            var clientEntities = pair.Client.ResolveDependency<IEntityManager>();
            var uid = clientEntities.GetEntity(programmerNet);
            var ui = clientEntities.System<Robust.Client.GameObjects.UserInterfaceSystem>();
            Assert.That(ui.TryGetUiState<KiasControllerEditorState>(uid, KiasControllerUiKey.Programmer, out var state), Is.True);
            ui.ClientSendUiMessage(uid, KiasControllerUiKey.Programmer, new KiasControllerEditMessage
                { Edit = action, Revision = state!.Revision, Node = 1, Config = new() { Text = text } });
        });
        await ClientEdit(KiasGraphEdit.Configure);
        await pair.RunTicksSync(10);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<KiasControllerProgrammerComponent>(programmer).Draft!.Nodes[0].Config.Text, Is.EqualTo(text));
            Assert.That(em.GetComponent<KiasControllerCardComponent>(card).Program.Nodes[0].Config.Text, Is.Empty);
        });
        await ClientEdit(KiasGraphEdit.Write);
        await pair.RunTicksSync(10);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<KiasControllerCardComponent>(card).Program.Nodes[0].Config.Text, Is.EqualTo(text));
            em.System<Robust.Server.GameObjects.UserInterfaceSystem>().CloseUi(programmer, KiasControllerUiKey.Programmer, actor);
            Assert.That(em.System<ItemSlotsSystem>().TryEject(programmer, KiasControllerProgrammerComponent.SlotId, actor, out _), Is.True);
            Assert.That(em.System<ItemSlotsSystem>().TryInsert(rack, KiasControllerRackComponent.SlotId(0), card, null), Is.True);
        });
        await pair.RunTicksSync(20);
        await pair.Server.WaitAssertion(() =>
        {
            var runtime = em.System<KiasControllerRuntimeSystem>();
            Assert.That(runtime.Running(card), Is.True, runtime.Fault(card));
            Assert.That(runtime.LastValue(card, 1, "Value").Text, Is.EqualTo(text));
            Assert.That(em.System<KiasControllerIoSystem>().Match(map.Grid,
                em.GetComponent<KiasControllerCardComponent>(card).Program.Nodes[2]).Count, Is.EqualTo(speakers.Count));
        });
        await pair.Client.WaitAssertion(() =>
        {
            foreach (var speaker in speakers)
                Assert.That(received.Count(message => message.SenderEntity == speaker && message.Channel == ChatChannel.Local && message.Message.Contains(text, StringComparison.OrdinalIgnoreCase)), Is.EqualTo(1), string.Join("\n", received.Select(message => $"{message.SenderEntity} {message.Channel}: {message.Message}")));
            Assert.That(received.Any(message => excluded.Contains(message.SenderEntity)), Is.False);
            received.Clear();
        });
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.System<ItemSlotsSystem>().TryEject(rack, KiasControllerRackComponent.SlotId(0), null, out _), Is.True);
            Assert.That(em.System<ItemSlotsSystem>().TryInsert(rack, KiasControllerRackComponent.SlotId(0), card, null), Is.True);
        });
        await pair.RunTicksSync(10);
        await pair.Client.WaitAssertion(() =>
        {
            Assert.That(received.Any(message => message.Message.Contains(text, StringComparison.OrdinalIgnoreCase)), Is.False);
            pair.Client.ResolveDependency<IUserInterfaceManager>().GetUIController<ChatUIController>().MessageAdded -= received.Add;
        });
        await pair.CleanReturnAsync();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ListModeStillCollectsAtmosDevicesWithNativeOrIntegratedPorts(bool integrated)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var coordinates = new EntityCoordinates(map.Grid, .5f, .5f);
            var actor = em.SpawnEntity("MobHuman", coordinates);
            var tool = em.SpawnEntity("Multitool", coordinates);
            var config = em.GetComponent<NetworkConfiguratorComponent>(tool);
            config.LinkModeActive = false;
            foreach (var prototype in new[] { "AirSensor", "GasVentPump", "GasVentScrubber" })
            {
                var device = em.SpawnEntity(prototype, coordinates);
                if (integrated)
                {
                    em.EnsureComponent<KiasIntegratedComponent>(device).Direct = true;
                    em.System<DeviceLinkSystem>().EnsureSinkPorts(device, "On", "Off");
                    em.System<DeviceLinkSystem>().EnsureSourcePorts(device, "Status");
                }
                var use = new AfterInteractEvent(actor, tool, device, coordinates, true);
                em.EventBus.RaiseLocalEvent(tool, use);
                Assert.That(config.LinkModeActive, Is.False, prototype);
                Assert.That(config.Devices.Values, Does.Contain(device), prototype);
            }
        });
        await pair.CleanReturnAsync();
    }
    [Test]
    public async Task NativeSensorDeviceListDrivesAtmospherePresetAndRecovery()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        EntityUid alarm = default, sensor = default, core = default, recorder = default, card = default;
        await pair.Server.WaitAssertion(() =>
        {
            var coordinates = new EntityCoordinates(map.Grid, .5f, .5f);
            EntityUid Spawn(string id)
            {
                var uid = em.SpawnEntity(id, coordinates);
                em.System<SharedPowerReceiverSystem>().SetNeedsPower(uid, false);
                return uid;
            }
            core = Spawn("KiasCore"); Spawn("KiasDataCable"); Spawn("KiasAtmosServer");
            recorder = Spawn("KiasRecorder"); var rack = Spawn("KiasControllerRack");
            var actor = Spawn("MobHuman"); var tool = Spawn("Multitool");
            alarm = Spawn("AirAlarm"); sensor = Spawn("AirSensor");
            em.RemoveComponent<AccessReaderComponent>(alarm);
            em.System<KiasSystem>().Rebuild(map.Grid);
            var kit = Spawn("KiasIntegrationKit");
            var install = new AfterInteractEvent(actor, kit, alarm, coordinates, true);
            em.EventBus.RaiseLocalEvent(kit, install);
            Assert.That(install.Handled, Is.True);
            var config = em.GetComponent<NetworkConfiguratorComponent>(tool);
            config.LinkModeActive = false; config.UseDelay = TimeSpan.Zero;
            var nativeDevices = new[] { sensor, Spawn("GasVentPump"), Spawn("GasVentScrubber") };
            foreach (var device in nativeDevices)
                em.EventBus.RaiseLocalEvent(tool, new AfterInteractEvent(actor, tool, device, coordinates, true));
            em.EventBus.RaiseLocalEvent(tool, new AfterInteractEvent(actor, tool, alarm, coordinates, true));
            Assert.That(config.LinkModeActive, Is.False);
            Assert.That(config.ActiveDeviceList, Is.EqualTo(alarm));
            em.EventBus.RaiseLocalEvent(tool, new NetworkConfiguratorButtonPressedMessage(NetworkConfiguratorButtonKey.Set) { Actor = actor });
            Assert.That(em.GetComponent<DeviceListComponent>(alarm).Devices, Is.EquivalentTo(nativeDevices));
            card = Spawn("KiasProgrammableController");
            var program = pair.Server.ResolveDependency<IPrototypeManager>().Index<KiasControllerProgramPrototype>("atmosphere").Program.Copy();
            program.Nodes.Add(new() { Id = 99, Kind = KiasNodeKind.Counter });
            program.Wires.Add(new() { FromNode = 1, FromPort = "AtmosClear", ToNode = 99, ToPort = "Increment" });
            em.GetComponent<KiasControllerCardComponent>(card).Program = program;
            Assert.That(em.System<ItemSlotsSystem>().TryInsert(rack, KiasControllerRackComponent.SlotId(0), card, null), Is.True);
        });
        await pair.RunTicksSync(50);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<AtmosMonitorComponent>(sensor).RegisteredDevices,
                Does.Contain(em.GetComponent<DeviceNetworkComponent>(alarm).Address));
            Assert.That(em.System<KiasControllerRuntimeSystem>().Running(card), Is.True,
                em.System<KiasControllerRuntimeSystem>().Fault(card));
            var monitor = em.GetComponent<AtmosMonitorComponent>(sensor);
            monitor.TileGas = new GasMixture(Atmospherics.CellVolume) { Temperature = Atmospherics.T20C };
            var update = new AtmosDeviceUpdateEvent(1,
                new Entity<GridAtmosphereComponent, GasTileOverlayComponent>(map.Grid,
                    em.EnsureComponent<GridAtmosphereComponent>(map.Grid), em.EnsureComponent<GasTileOverlayComponent>(map.Grid)), null);
            em.EventBus.RaiseLocalEvent(sensor, ref update);
            Assert.That(monitor.LastAlarmState, Is.EqualTo(AtmosAlarmType.Danger));
        });
        await pair.RunTicksSync(30);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<AtmosAlarmableComponent>(alarm).LastAlarmState, Is.EqualTo(AtmosAlarmType.Danger));
            Assert.That(em.GetComponent<KiasProtocolComponent>(core).Alert, Is.EqualTo(KiasAlert.Emergency));
            Assert.That(em.GetComponent<KiasRecorderComponent>(recorder).Entries.Any(entry => entry.Contains("[P]")), Is.True);
            var air = new GasMixture(Atmospherics.CellVolume) { Temperature = Atmospherics.T20C };
            air.SetMoles(Gas.Oxygen, 21); air.SetMoles(Gas.Nitrogen, 79);
            em.GetComponent<AtmosMonitorComponent>(sensor).TileGas = air;
            var update = new AtmosDeviceUpdateEvent(1,
                new Entity<GridAtmosphereComponent, GasTileOverlayComponent>(map.Grid,
                    em.GetComponent<GridAtmosphereComponent>(map.Grid), em.GetComponent<GasTileOverlayComponent>(map.Grid)), null);
            em.EventBus.RaiseLocalEvent(sensor, ref update);
        });
        await pair.RunTicksSync(30);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<AtmosAlarmableComponent>(alarm).LastAlarmState, Is.EqualTo(AtmosAlarmType.Normal));
            Assert.That(em.System<KiasControllerRuntimeSystem>().LastValue(card, 99, "Value").Number, Is.EqualTo(1));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task OrdinaryLinkModeStillLinksAnAirAlarmToALight()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var coordinates = new EntityCoordinates(map.Grid, .5f, .5f);
            var actor = em.SpawnEntity("MobHuman", coordinates);
            var tool = em.SpawnEntity("Multitool", coordinates);
            var alarm = em.SpawnEntity("AirAlarm", coordinates);
            var light = em.SpawnEntity("Poweredlight", coordinates);
            em.RemoveComponent<AccessReaderComponent>(alarm);
            var config = em.GetComponent<NetworkConfiguratorComponent>(tool);
            config.LinkModeActive = true; config.UseDelay = TimeSpan.Zero;
            em.EventBus.RaiseLocalEvent(tool, new AfterInteractEvent(actor, tool, alarm, coordinates, true));
            Assert.That(config.LinkModeActive, Is.True);
            Assert.That(config.ActiveDeviceLink, Is.EqualTo(alarm));
            em.EventBus.RaiseLocalEvent(tool, new AfterInteractEvent(actor, tool, light, coordinates, true));
            Assert.That(config.DeviceLinkTarget, Is.EqualTo(light));
            em.EventBus.RaiseLocalEvent(tool, new NetworkConfiguratorToggleLinkMessage("AirDanger", "Off") { Actor = actor });
            Assert.That(em.System<DeviceLinkSystem>().GetLinks(alarm, light).Any(link => link.Item1.ToString() == "AirDanger" && link.Item2.ToString() == "Off"), Is.True);
            Assert.That(config.Devices, Is.Empty);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ServiceGroupsUseSeparateFieldsAndRefreshRunningSelectors()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        EntityUid speaker = default, tool = default, actor = default, card = default;
        await pair.Server.WaitAssertion(() =>
        {
            var coordinates = new EntityCoordinates(map.Grid, .5f, .5f);
            EntityUid Spawn(string id)
            {
                var uid = em.SpawnEntity(id, coordinates); em.RemoveComponent<ApcPowerReceiverComponent>(uid); return uid;
            }
            Spawn("KiasCore"); Spawn("KiasDataCable"); var rack = Spawn("KiasControllerRack");
            speaker = Spawn("KiasSpeaker"); actor = Spawn("MobHuman"); tool = Spawn("KiasServiceTool");
            var service = em.GetComponent<KiasServiceToolComponent>(tool);
            service.Mode = KiasServiceMode.Group; service.Message = "Linked announcement";
            em.System<KiasSystem>().Rebuild(map.Grid);
            em.EventBus.RaiseLocalEvent(tool, new KiasSetMessage(" custom-room-17 ") { Actor = actor });
            Assert.That(service.Group, Is.EqualTo("CUSTOM-ROOM-17"));
            Assert.That(service.Message, Is.EqualTo("Linked announcement"));
            foreach (var prototype in new[] { "Poweredlight", "KiasLightController" })
            {
                var target = Spawn(prototype);
                em.System<KiasSystem>().Rebuild(map.Grid);
                em.EventBus.RaiseLocalEvent(tool, new AfterInteractEvent(actor, tool, target, coordinates, true));
                Assert.That(((KiasServiceState) em.System<KiasDisplaySystem>().BuildLocalState(tool)!).CurrentGroup, Is.EqualTo("CUSTOM-ROOM-17"));
            }
            em.System<KiasSystem>().Rebuild(map.Grid);
            em.EventBus.RaiseLocalEvent(tool, new AfterInteractEvent(actor, tool, speaker, coordinates, true));
            var state = (KiasServiceState) em.System<KiasDisplaySystem>().BuildLocalState(tool)!;
            Assert.That(state.CurrentGroup, Is.EqualTo("CUSTOM-ROOM-17")); Assert.That(state.GroupKind, Is.EqualTo("speaker"));
            card = Spawn("KiasProgrammableController");
            em.GetComponent<KiasControllerCardComponent>(card).Program.Nodes.Add(new() { Id = 1, Kind = KiasNodeKind.All, Profile = "Speaker", Group = "CUSTOM-ROOM-17" });
            Assert.That(em.System<ItemSlotsSystem>().TryInsert(rack, KiasControllerRackComponent.SlotId(0), card, null), Is.True);
        });
        await pair.RunTicksSync(40);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.System<KiasControllerRuntimeSystem>().LastValue(card, 1, "$MatchedCount").Number, Is.EqualTo(1));
            em.EventBus.RaiseLocalEvent(tool, new KiasSetMessage("OTHER") { Actor = actor });
            Assert.That(((KiasServiceState) em.GetComponent<UserInterfaceComponent>(tool).States[KiasUiKey.Service]).Group, Is.EqualTo("OTHER"));
            em.EventBus.RaiseLocalEvent(tool, new AfterInteractEvent(actor, tool, speaker, new EntityCoordinates(map.Grid, .5f, .5f), true));
        });
        await pair.RunTicksSync(40);
        await pair.Server.WaitAssertion(() => Assert.That(em.System<KiasControllerRuntimeSystem>().LastValue(card, 1, "$MatchedCount").Number, Is.Zero));
        await pair.CleanReturnAsync();
    }

}
