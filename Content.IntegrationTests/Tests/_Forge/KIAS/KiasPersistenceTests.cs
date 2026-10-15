using System.Linq;
using Content.Server._Forge.KIAS;
using Content.Server.Power.Components;
using Content.Shared._Forge.KIAS;
using Content.Shared._Forge.KIAS.Controllers;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Power;
using Robust.Shared.ContentPack;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._Forge.KIAS;

[TestFixture]
public sealed class KiasPersistenceTests
{
    [Test]
    public async Task RelayTokenProtocolAndRecorderSurviveMapRoundTrip()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var server = pair.Server;
        var em = server.ResolveDependency<IEntityManager>();
        var maps = em.System<SharedMapSystem>();
        var loader = em.System<MapLoaderSystem>();
        var path = new ResPath("/Maps/Test/KiasRoundTrip.yml");
        var serial = Guid.NewGuid().ToString("N");
        string savedIdentifier = string.Empty;
        var owner = new Robust.Shared.Network.NetUserId(Guid.NewGuid());
        await server.WaitAssertion(() =>
        {
            for (var x = 0; x < 9; x++)
            {
                maps.SetTile(map.Grid, map.Grid.Comp, new Vector2i(x, 0), map.Tile.Tile);
                em.SpawnEntity("KiasDataCable", new EntityCoordinates(map.Grid, x + 0.5f, 0.5f));
            }
            EntityUid Spawn(string prototype, int x)
            {
                var uid = em.SpawnEntity(prototype, new EntityCoordinates(map.Grid, x + 0.5f, 0.5f));
                em.RemoveComponent<ApcPowerReceiverComponent>(uid);
                return uid;
            }
            var core = Spawn("KiasCore", 0);
            var recorder = Spawn("KiasRecorder", 1);
            var tool = Spawn("KiasServiceTool", 1);
            var service = em.GetComponent<KiasServiceToolComponent>(tool);
            service.Mode = KiasServiceMode.Group; service.Group = "TEST-42"; service.Message = "custom-announcement";
            var legacyTool = Spawn("KiasServiceTool", 1);
            var legacyService = em.GetComponent<KiasServiceToolComponent>(legacyTool);
            legacyService.Mode = KiasServiceMode.Group; legacyService.Message = "bridge-old";
            var crew = Spawn("KiasCrewServer", 2);
            var rack = Spawn("KiasControllerRack", 0);
            var card = Spawn("KiasProgrammableController", 0);
            var relay = Spawn("KiasRelay", 4);
            var cardComponent = em.GetComponent<KiasControllerCardComponent>(card);
            cardComponent.Enabled = false;
            cardComponent.Revision = 42;
            cardComponent.Program = new() { Name = "saved-card" };
            cardComponent.Program.Nodes.Add(new() { Id = 17, Kind = KiasNodeKind.Specific, Profile = "Relay", Binding = relay,
                X = 123, Y = 456, Room = "saved-room", Group = "saved-group", DeviceName = "saved-relay" });
            cardComponent.Program.Nodes.Add(new() { Id = 91, Kind = KiasNodeKind.OnStart, X = -400, Y = 20 });
            cardComponent.Program.Wires.Add(new() { FromNode = 91, FromPort = "Started", ToNode = 17, ToPort = "Open" });
            Assert.That(em.System<ItemSlotsSystem>().TryInsert(rack, KiasControllerRackComponent.SlotId(7), card, null), Is.True);
            var speaker = Spawn("KiasSpeaker", 6);
            var scanner = Spawn("KiasRoomScanner", 7);
            savedIdentifier = em.System<KiasDeviceIdentitySystem>().Identifier(scanner);
            var rotary = Spawn("KiasRotarySwitch", 7);
            em.GetComponent<KiasRotaryComponent>(rotary).Positions = 2;
            em.GetComponent<KiasRotaryComponent>(rotary).Signals = new() { 3, 0 };
            var wireless = Spawn("KiasWirelessTransceiver", 7);
            var transmitter = Spawn("KiasWirelessTransceiver", 7);
            em.GetComponent<KiasWirelessComponent>(transmitter).Channel = "peer";
            var monitor = Spawn("KiasResourceMonitor", 8);
            em.GetComponent<KiasWirelessComponent>(wireless).Channel = "saved-channel";
            em.GetComponent<KiasWirelessComponent>(wireless).Range = 123;
            em.GetComponent<KiasWirelessComponent>(wireless).TrustedTransmitters.Add(transmitter);
            em.GetComponent<KiasResourceMonitorComponent>(monitor).Targets.Add(speaker);
            em.GetComponent<KiasRoomScannerComponent>(scanner).Range = 4;
            em.GetComponent<KiasDeviceComponent>(scanner).Room = "saved-room";
            em.EnsureComponent<KiasClaimComponent>(map.Grid).Owner = owner;
            var audio = em.GetComponent<KiasAudioComponent>(core);
            audio.Notification = KiasTonePreset.Silent;
            audio.Warning = KiasTonePreset.BlueAlert;
            audio.Battle = KiasTonePreset.Buzzer;
            audio.Emergency = KiasTonePreset.RedAlert;
            audio.PreviewAfter = TimeSpan.FromHours(1);
            em.GetComponent<KiasSpeakerComponent>(speaker).Links.Add(new KiasSpeakerLink { Source = scanner, SourcePort = "KiasMotion", Message = "Saved custom message" });
            var token = em.SpawnEntity("KiasCrewTransponder", new EntityCoordinates(map.Grid, 2.5f, 0.5f));
            var tokenComponent = em.GetComponent<KiasTransponderComponent>(token);
            tokenComponent.Serial = serial;
            tokenComponent.Core = core;
            em.GetComponent<KiasCrewServerComponent>(crew).Registered.Add(serial);
            em.GetComponent<KiasRecorderComponent>(recorder).Entries.Add("Saved event");
            var protocol = new KiasProtocolRecord { Trigger = KiasTrigger.HullImpact, Cooldown = 30, Enabled = false, PresetId = "saved-preset" };
            protocol.Actions.Add(new KiasProtocolAction { Kind = KiasActionKind.Relay, Target = relay, Value = false });
            protocol.Actions.Add(new KiasProtocolAction { Kind = KiasActionKind.Announce, Group = "EMERGENCY", Message = "saved-message" });
            em.GetComponent<KiasProtocolComponent>(core).Protocols.Clear();
            em.GetComponent<KiasProtocolComponent>(core).Protocols.Add(protocol);
            em.System<KiasSystem>().Rebuild(map.Grid);
            Assert.That(em.System<KiasRelaySystem>().SetClosed(relay, false), Is.True);
            server.ResolveDependency<IResourceManager>().UserData.CreateDir(path.Directory);
            Assert.That(loader.TrySaveMap(map.MapId, path), Is.True);
            maps.DeleteMap(map.MapId);
        });
        await server.WaitIdleAsync();
        EntityUid loadedGrid = default;
        await server.WaitAssertion(() =>
        {
            Assert.That(loader.TryLoadMap(path, out _, out var grids), Is.True);
            loadedGrid = grids!.Single();
        });
        await pair.RunTicksSync(3);
        await server.WaitAssertion(() =>
        {
            var candidates = new System.Collections.Generic.HashSet<EntityUid>();
            var grid = em.GetComponent<Robust.Shared.Map.Components.MapGridComponent>(loadedGrid);
            em.System<EntityLookupSystem>().GetLocalEntitiesIntersecting(loadedGrid, grid.LocalAABB, candidates);
            var core = candidates.Single(em.HasComponent<KiasCoreComponent>);
            var relay = candidates.Single(em.HasComponent<KiasRelayComponent>);
            var rack = candidates.Single(em.HasComponent<KiasControllerRackComponent>);
            var slot = em.System<ItemSlotsSystem>().GetItemOrNull(rack, KiasControllerRackComponent.SlotId(7));
            Assert.That(slot, Is.Not.Null);
            var savedTools = candidates.Where(em.HasComponent<KiasServiceToolComponent>).Select(em.GetComponent<KiasServiceToolComponent>).ToArray();
            Assert.That(savedTools.Single(t => t.Message == "custom-announcement").Group, Is.EqualTo("TEST-42"));
            Assert.That(savedTools.Single(t => t.Message == "bridge-old").Group, Is.EqualTo("BRIDGE-OLD"));
            var savedCard = em.GetComponent<KiasControllerCardComponent>(slot!.Value);
            Assert.That(savedCard.Enabled, Is.False);
            Assert.That(savedCard.Revision, Is.EqualTo(42));
            Assert.That(savedCard.Program.Name, Is.EqualTo("saved-card"));
            Assert.That(em.GetComponent<MetaDataComponent>(slot.Value).EntityName, Is.EqualTo("saved-card"));
            Assert.That(savedCard.Program.Nodes[0].Binding, Is.EqualTo(relay));
            Assert.That(savedCard.Program.Nodes[0].Id, Is.EqualTo(17));
            Assert.That(savedCard.Program.Nodes[0].X, Is.EqualTo(123));
            Assert.That(savedCard.Program.Nodes[0].Y, Is.EqualTo(456));
            Assert.That(savedCard.Program.Nodes[0].Room, Is.EqualTo("saved-room"));
            Assert.That(savedCard.Program.Nodes[0].Group, Is.EqualTo("saved-group"));
            Assert.That(savedCard.Program.Wires.Single().ToNode, Is.EqualTo(17));
            var recorder = candidates.Single(em.HasComponent<KiasRecorderComponent>);
            var crew = candidates.Single(em.HasComponent<KiasCrewServerComponent>);
            var speaker = candidates.Single(em.HasComponent<KiasSpeakerComponent>);
            var scanner = candidates.Single(em.HasComponent<KiasRoomScannerComponent>);
            Assert.That(em.System<KiasDeviceIdentitySystem>().Identifier(scanner), Is.EqualTo(savedIdentifier));
            var rotary = candidates.Single(em.HasComponent<KiasRotaryComponent>);
            Assert.That(em.GetComponent<KiasRotaryComponent>(rotary).Signals, Is.EqualTo(new[] { 3, 0 }));
            Assert.That(em.GetComponent<KiasRotaryComponent>(rotary).Positions, Is.EqualTo(2));
            var wireless = candidates.Single(uid => em.HasComponent<KiasWirelessComponent>(uid)
                && em.GetComponent<KiasWirelessComponent>(uid).Channel == "saved-channel");
            var transmitter = candidates.Single(uid => em.HasComponent<KiasWirelessComponent>(uid)
                && em.GetComponent<KiasWirelessComponent>(uid).Channel == "peer");
            var monitor = candidates.Single(em.HasComponent<KiasResourceMonitorComponent>);
            var audio = em.GetComponent<KiasAudioComponent>(core);
            var tokens = em.EntityQueryEnumerator<KiasTransponderComponent>();
            Assert.That(tokens.MoveNext(out _, out var token), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(em.GetComponent<KiasRelayComponent>(relay).Closed, Is.False);
                Assert.That(em.GetComponent<KiasRelayComponent>(relay).Channel, Is.EqualTo(CableType.Data));
                Assert.That(token.Serial, Is.EqualTo(serial));
                Assert.That(token.Core, Is.EqualTo(core));
                Assert.That(em.GetComponent<KiasCrewServerComponent>(crew).Registered, Does.Contain(serial));
                Assert.That(em.GetComponent<KiasRecorderComponent>(recorder).Entries, Does.Contain("Saved event"));
                var protocol = em.GetComponent<KiasProtocolComponent>(core).Protocols.Single();
                Assert.That(protocol.Actions[0].Target, Is.EqualTo(relay));
                Assert.That(protocol.Actions[1].Message, Is.EqualTo("saved-message"));
                Assert.That(protocol.Actions, Has.Count.EqualTo(2));
                Assert.That(protocol.Enabled, Is.False);
                Assert.That(protocol.PresetId, Is.EqualTo("saved-preset"));
                Assert.That(em.GetComponent<KiasClaimComponent>(loadedGrid).Owner, Is.EqualTo(owner));
                Assert.That(audio.Notification, Is.EqualTo(KiasTonePreset.Silent));
                Assert.That(audio.Warning, Is.EqualTo(KiasTonePreset.BlueAlert));
                Assert.That(audio.Battle, Is.EqualTo(KiasTonePreset.Buzzer));
                Assert.That(audio.Emergency, Is.EqualTo(KiasTonePreset.RedAlert));
                Assert.That(audio.PreviewAfter, Is.EqualTo(TimeSpan.Zero));
                Assert.That(em.GetComponent<KiasWirelessComponent>(wireless).Channel, Is.EqualTo("saved-channel"));
                Assert.That(em.GetComponent<KiasWirelessComponent>(wireless).Range, Is.EqualTo(123));
                Assert.That(em.GetComponent<KiasWirelessComponent>(wireless).TrustedTransmitters, Does.Contain(transmitter));
                Assert.That(em.GetComponent<KiasResourceMonitorComponent>(monitor).Targets.Single(), Is.EqualTo(speaker));
                Assert.That(em.GetComponent<KiasRoomScannerComponent>(scanner).Range, Is.EqualTo(4));
                Assert.That(em.GetComponent<KiasDeviceComponent>(scanner).Room, Is.EqualTo("saved-room"));
                Assert.That(em.GetComponent<KiasSpeakerComponent>(speaker).Links.Single().Source, Is.EqualTo(scanner));
                Assert.That(em.GetComponent<KiasSpeakerComponent>(speaker).Links.Single().Message, Is.EqualTo("Saved custom message"));
            });
            em.System<KiasSystem>().Rebuild(loadedGrid);
            Assert.That(em.System<KiasRelaySystem>().IsBlocked(loadedGrid, new Vector2i(4, 0), CableType.Data), Is.True);
            maps.DeleteMap(em.GetComponent<TransformComponent>(loadedGrid).MapID);
        });
        await pair.CleanReturnAsync();
    }
}
