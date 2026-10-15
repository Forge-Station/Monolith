#pragma warning disable RA0002
using System.Collections.Generic;
using System.Numerics;
using System.Linq;
using Content.Server._Forge.KIAS;
using Content.Server.DeviceLinking.Systems;
using Content.Server.Power.Components;
using Content.Server.Shuttles.Events;
using Content.Shared._Forge.KIAS;
using Content.Shared._Forge.KIAS.Controllers;
using Content.Shared.Containers.ItemSlots;
using Content.Shared._Mono.Company;
using Content.Shared.DeviceLinking.Events;
using Content.Shared.DeviceNetwork.Components;
using Content.Shared.Verbs;
using Content.Shared.Doors.Components;
using Content.Shared.Interaction;
using Content.Shared.Shuttles.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Localization;
using Robust.Shared.Maths;
using Robust.Shared.Containers;

namespace Content.IntegrationTests.Tests._Forge.KIAS;

[TestFixture]
public sealed class KiasDeviceTests
{
    [Test]
    public async Task PhysicalIffDockingKeySwitchAdapterAndWirelessWorkWithNativeDevices()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        EntityUid dockSource = default, dockContact = default;
        await pair.Server.WaitAssertion(() =>
        {
            var maps = em.System<SharedMapSystem>();
            var transforms = em.System<SharedTransformSystem>();
            var kias = em.System<KiasSystem>();
            var cables = new List<EntityUid>();
            for (var x = 0; x < 12; x++)
            {
                maps.SetTile(map.Grid, map.Grid.Comp, new Vector2i(x, 0), map.Tile.Tile);
                cables.Add(em.SpawnEntity("KiasDataCable", new EntityCoordinates(map.Grid, x + 0.5f, 0.5f)));
            }
            EntityUid Spawn(string prototype, EntityUid grid, int x)
            {
                var uid = em.SpawnEntity(prototype, new EntityCoordinates(grid, x + 0.5f, 0.5f));
                em.RemoveComponent<ApcPowerReceiverComponent>(uid);
                return uid;
            }
            var core = Spawn("KiasCore", map.Grid, 0);
            var iff = Spawn("KiasIffReceiver", map.Grid, 1);
            var dock = Spawn("KiasDockingSensor", map.Grid, 2);
            dockSource = dock;
            var adapter = Spawn("KiasDeviceAdapter", map.Grid, 3);
            var keySwitch = Spawn("KiasKeySwitch", map.Grid, 4);
            var rotary = Spawn("KiasRotarySwitch", map.Grid, 5);
            var monitor = Spawn("KiasResourceMonitor", map.Grid, 6);
            var door = Spawn("AirlockGlass", map.Grid, 7);
            em.GetComponent<AirlockComponent>(door).Powered = true;
            em.RemoveComponent<Content.Shared.Access.Components.AccessReaderComponent>(door);
            var receiver = Spawn("KiasWirelessTransceiver", map.Grid, 8);
            Spawn("KiasRecorder", map.Grid, 9);
            kias.Rebuild(map.Grid);
            var actor = em.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid, 0.5f, 0.5f));
            void Signal(EntityUid target, string port, EntityUid source)
            {
                var signal = new SignalReceivedEvent(port, source);
                em.EventBus.RaiseLocalEvent(target, ref signal);
            }
            var links = em.System<DeviceLinkSystem>();
            var linkingTool = em.SpawnEntity(null, new EntityCoordinates(map.Grid, 7.5f, 0.5f));
            var configurator = em.EnsureComponent<NetworkConfiguratorComponent>(linkingTool);
            configurator.ActiveDeviceLink = adapter;
            var defaultsVerb = new GetVerbsEvent<AlternativeVerb>(actor, door, linkingTool, null, true, true, true, new());
            em.EventBus.RaiseLocalEvent(door, defaultsVerb);
            defaultsVerb.Verbs.Single(verb => verb.Text == Loc.GetString("network-configurator-link-defaults")).Act!();
            Assert.That(links.GetLinks(adapter, door).Any(link => link.source == "On" && link.sink == "Open"), Is.True);
            Assert.That(em.System<Content.Server.Doors.Systems.DoorSystem>().CanOpen(door), Is.True);
            Signal(adapter, "On", core);
            Assert.That(em.GetComponent<KiasDeviceAdapterComponent>(adapter).State, Is.True);
            Assert.That(em.GetComponent<DoorComponent>(door).State.ToString(), Is.AnyOf("Opening", "Open"));
            var turn = new InteractHandEvent(actor, rotary);
            em.EventBus.RaiseLocalEvent(rotary, turn);
            Assert.That(em.GetComponent<KiasRotaryComponent>(rotary).Position, Is.EqualTo(1));
            var stack = em.SpawnEntity("SheetSteel", new EntityCoordinates(map.Grid, 6.5f, 0.5f));
            em.GetComponent<KiasResourceMonitorComponent>(monitor).Targets.Add(stack);
            Assert.That(((KiasResourceState) em.System<KiasDisplaySystem>().BuildLocalState(monitor)).Details, Does.Contain("50"));

            var contact = maps.CreateGridEntity(map.MapId);
            dockContact = contact.Owner;
            maps.SetTile(contact, contact.Comp, Vector2i.Zero, map.Tile.Tile);
            transforms.SetLocalPosition(contact, new Vector2(100, 0));
            em.AddComponent<IFFComponent>(contact);
            em.EnsureComponent<CompanyComponent>(map.Grid).CompanyName = "TSF";
            em.EnsureComponent<CompanyComponent>(contact).CompanyName = "TSF";
            Assert.That(em.System<KiasNavigationSystem>().Classify(map.Grid, contact), Is.EqualTo(KiasContactDisposition.Friendly));
            em.DeleteEntity(iff);
            kias.Rebuild(map.Grid);
            Assert.That(em.System<KiasNavigationSystem>().Classify(map.Grid, contact), Is.EqualTo(KiasContactDisposition.Unknown));
            em.RemoveComponent<CompanyComponent>(map.Grid);
            var config = em.GetComponent<KiasProtocolComponent>(core);
            config.Protocols.Clear();
            config.Protocols.Add(new KiasProtocolRecord { Trigger = KiasTrigger.Docked, Actions = new() { new() { Kind = KiasActionKind.Record, Message = "Dock detected" } } });
            var rack = Spawn("KiasControllerRack", map.Grid, 0);
            var card = Spawn("KiasProgrammableController", map.Grid, 0);
            var io = em.System<Content.Server._Forge.KIAS.Controllers.KiasControllerIoSystem>();
            var imported = KiasLegacyGraphTranslator.Import(config.Protocols[0], io.Supports, io.NativeSinkProfile);
            Assert.That(imported.Errors, Is.Empty);
            em.GetComponent<KiasControllerCardComponent>(card).Program = imported.Program!;
            kias.Rebuild(map.Grid);
            Assert.That(em.System<ItemSlotsSystem>().TryInsert(rack, KiasControllerRackComponent.SlotId(0), card, null), Is.True);

            em.RemoveComponent<CompanyComponent>(contact);
            em.SpawnEntity("KiasDataCable", new EntityCoordinates(contact, 0.5f, 0.5f));
            Spawn("KiasCore", contact, 0);
            var transmitter = Spawn("KiasWirelessTransceiver", contact, 0);
            kias.Rebuild(contact);
            em.GetComponent<KiasWirelessComponent>(receiver).Channel = "other";
            Signal(receiver, "On", transmitter);
            Assert.That(em.GetComponent<KiasDeviceAdapterComponent>(receiver).State, Is.False);
            em.GetComponent<KiasWirelessComponent>(receiver).Channel = "KIAS";
            Signal(receiver, "On", transmitter);
            Assert.That(em.GetComponent<KiasDeviceAdapterComponent>(receiver).State, Is.True);
            var tool = em.SpawnEntity("KiasServiceTool", new EntityCoordinates(map.Grid, 0.5f, 0.5f));
            em.GetComponent<KiasServiceToolComponent>(tool).Mode = KiasServiceMode.Link;
            void UseTool(EntityUid target)
            {
                var use = new AfterInteractEvent(actor, tool, target, em.GetComponent<TransformComponent>(target).Coordinates, true);
                em.EventBus.RaiseLocalEvent(tool, use);
            }
            UseTool(transmitter);
            UseTool(receiver);
            Assert.That(em.GetComponent<KiasWirelessComponent>(receiver).TrustedTransmitters, Does.Contain(transmitter));
            Assert.That(links.GetLinks(transmitter, receiver).Any(link => link.source == "On" && link.sink == "On"), Is.True);
            UseTool(transmitter);
            UseTool(receiver);
            Assert.That(em.GetComponent<KiasWirelessComponent>(receiver).TrustedTransmitters, Is.Empty);
            Assert.That(links.GetLinks(transmitter, receiver), Is.Empty);
            em.EnsureComponent<KiasClaimComponent>(map.Grid).Owner = new Robust.Shared.Network.NetUserId(Guid.NewGuid());
            Signal(receiver, "Off", transmitter);
            Assert.That(em.GetComponent<KiasDeviceAdapterComponent>(receiver).State, Is.True, "A protected grid rejects an unpaired transmitter on the same channel.");
            em.GetComponent<KiasWirelessComponent>(receiver).TrustedTransmitters.Add(transmitter);
            Signal(receiver, "Off", transmitter);
            Assert.That(em.GetComponent<KiasDeviceAdapterComponent>(receiver).State, Is.False);
            Signal(receiver, "On", transmitter);
            Assert.That(em.GetComponent<KiasDeviceAdapterComponent>(receiver).State, Is.True);
            em.RemoveComponent<KiasClaimComponent>(map.Grid);
            transforms.SetLocalPosition(contact, new Vector2(500, 0));
            Signal(receiver, "Off", transmitter);
            Assert.That(em.GetComponent<KiasDeviceAdapterComponent>(receiver).State, Is.True);

            var key = em.SpawnEntity("KiasMasterKey", new EntityCoordinates(map.Grid, 4.5f, 0.5f));
            var containers = em.System<SharedContainerSystem>();
            containers.Insert(key, containers.GetContainer(keySwitch, "kias-key"));
            em.EventBus.RaiseLocalEvent(keySwitch, new ActivateInWorldEvent(actor, keySwitch, true));
            Assert.That(em.GetComponent<KiasCoreComponent>(core).Enabled, Is.False);
            em.DeleteEntity(cables[2]);
            em.EventBus.RaiseLocalEvent(keySwitch, new ActivateInWorldEvent(actor, keySwitch, true));
            Assert.That(em.GetComponent<KiasCoreComponent>(core).Enabled, Is.False);
            em.SpawnEntity("KiasDataCable", new EntityCoordinates(map.Grid, 2.5f, 0.5f));
            em.EventBus.RaiseLocalEvent(keySwitch, new ActivateInWorldEvent(actor, keySwitch, true));
            Assert.That(em.GetComponent<KiasCoreComponent>(core).Enabled, Is.True);
        });
        await pair.RunTicksSync(40);
        await pair.Server.WaitAssertion(() => em.EventBus.RaiseLocalEvent(dockSource,
            new DockEvent { GridAUid = map.Grid, GridBUid = dockContact }, true));
        await pair.RunTicksSync(8);
        await pair.Server.WaitAssertion(() => Assert.That(em.GetComponent<KiasGridComponent>(map.Grid).Log, Has.Some.Contains("Dock detected")));
        await pair.CleanReturnAsync();
    }
}
