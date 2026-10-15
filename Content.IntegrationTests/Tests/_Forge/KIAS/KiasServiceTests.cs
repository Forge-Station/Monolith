#pragma warning disable RA0002
using System.Linq;
using Content.Client._Forge.KIAS;
using Content.Server._Forge.KIAS;
using Content.Server.Power.Components;
using Content.Shared._Forge.KIAS;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared.Interaction;
using Content.Shared.Mind;
using Content.Shared.Power;
using Content.Shared.Verbs;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Localization;

namespace Content.IntegrationTests.Tests._Forge.KIAS;

[TestFixture]
public sealed class KiasServiceTests
{
    [Test]
    public async Task NativeGridSplitSeparatesDeviceRegistries()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        EntityUid core = default;
        EntityUid server = default;
        var splitOwner = new Robust.Shared.Network.NetUserId(Guid.NewGuid());
        await pair.Server.WaitAssertion(() =>
        {
            pair.Server.ResolveDependency<Robust.Shared.Configuration.IConfigurationManager>().SetCVar(Robust.Shared.CVars.GridSplitting, true);
            map.Grid.Comp.CanSplit = true;
            for (var x = 0; x < 7; x++)
            {
                em.System<SharedMapSystem>().SetTile(map.Grid, map.Grid.Comp, new Vector2i(x, 0), map.Tile.Tile);
                if (x != 3)
                    em.SpawnEntity("KiasDataCable", new EntityCoordinates(map.Grid, x + 0.5f, 0.5f));
            }
            core = em.SpawnEntity("KiasCore", new EntityCoordinates(map.Grid, 0.5f, 0.5f));
            server = em.SpawnEntity("KiasCrewServer", new EntityCoordinates(map.Grid, 6.5f, 0.5f));
            em.RemoveComponent<ApcPowerReceiverComponent>(core);
            em.RemoveComponent<ApcPowerReceiverComponent>(server);
            em.System<KiasSystem>().Rebuild(map.Grid);
            em.EnsureComponent<KiasClaimComponent>(map.Grid).Owner = splitOwner;
            em.System<SharedMapSystem>().SetTile(map.Grid, map.Grid.Comp, new Vector2i(3, 0), Tile.Empty);
            em.System<Robust.Server.Physics.GridFixtureSystem>().CheckSplits(map.Grid);
        });
        await pair.RunTicksSync(3);
        await pair.Server.WaitAssertion(() =>
        {
            var coreGrid = em.GetComponent<TransformComponent>(core).GridUid!.Value;
            var otherGrid = em.GetComponent<TransformComponent>(server).GridUid!.Value;
            Assert.That(coreGrid, Is.Not.EqualTo(otherGrid));
            Assert.That(em.GetComponent<KiasClaimComponent>(coreGrid).Owner, Is.EqualTo(splitOwner));
            Assert.That(em.GetComponent<KiasClaimComponent>(otherGrid).Owner, Is.EqualTo(splitOwner));
            Assert.That(em.GetComponent<KiasGridComponent>(coreGrid).Devices, Does.Not.Contain(server));
            Assert.That(em.GetComponent<KiasGridComponent>(otherGrid).Devices, Does.Contain(server));
            Assert.That(em.System<KiasSystem>().ActiveGrids, Does.Contain(coreGrid));
            Assert.That(em.System<KiasSystem>().IsOnline(server), Is.False);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task OwnerConfigurationRejectsRetiredProtocolEditsAndClientWindowBuilds()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var map = await pair.CreateTestMap();
        var other = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var session = pair.Server.ResolveDependency<IPlayerManager>().Sessions.Single();
            var actor = em.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid, 0.5f, 0.5f));
            var stranger = em.SpawnEntity("MobCat", new EntityCoordinates(map.Grid, 0.5f, 0.5f));
            var mind = em.System<SharedMindSystem>().CreateMind(session.UserId);
            em.System<SharedMindSystem>().TransferTo(mind, actor);
            em.EnsureComponent<ShipOwnershipComponent>(map.Grid).OwnerUserId = session.UserId;
            EntityUid Spawn(string prototype, EntityUid grid, int x)
            {
                em.System<SharedMapSystem>().SetTile(grid, em.GetComponent<Robust.Shared.Map.Components.MapGridComponent>(grid), new Vector2i(x, 0), map.Tile.Tile);
                em.SpawnEntity("KiasDataCable", new EntityCoordinates(grid, x + 0.5f, 0.5f));
                var uid = em.SpawnEntity(prototype, new EntityCoordinates(grid, x + 0.5f, 0.5f));
                em.RemoveComponent<ApcPowerReceiverComponent>(uid);
                return uid;
            }
            var core = Spawn("KiasCore", map.Grid, 0);
            var display = Spawn("KiasManagementConsole", map.Grid, 1);
            var defence = Spawn("KiasDefenceServer", map.Grid, 2);
            var crewServer = Spawn("KiasCrewServer", map.Grid, 3);
            var serviceTool = em.SpawnEntity("KiasServiceTool", new EntityCoordinates(map.Grid, 0.5f, 0.5f));
            var logger = Spawn("KiasRecorder", map.Grid, 4);
            var wall = Spawn("KiasDisplay", map.Grid, 5);
            var foreign = Spawn("KiasRelay", other.Grid, 0);
            em.System<KiasSystem>().Rebuild(map.Grid);
            var uiSystem = em.System<KiasDisplaySystem>();
            Assert.That(uiSystem.BuildLocalState(serviceTool), Is.TypeOf<KiasServiceState>());
            Assert.That(uiSystem.BuildLocalState(logger), Is.TypeOf<KiasRecorderState>());
            Assert.That(uiSystem.BuildLocalState(wall), Is.TypeOf<KiasWallState>());
            Assert.That(uiSystem.BuildLocalState(crewServer), Is.TypeOf<KiasCrewState>());
            Assert.That(em.GetComponent<KiasGridComponent>(map.Grid).ProtocolTargets, Is.Empty);
            em.EnsureComponent<ShuttleDeedComponent>(map.Grid).ShuttleUid = map.Grid.Owner;
            var deedId = em.SpawnEntity("PassengerIDCard", new EntityCoordinates(map.Grid, 0.5f, 0.5f));
            em.EnsureComponent<ShuttleDeedComponent>(deedId).ShuttleUid = map.Grid.Owner;
            Assert.That(em.System<Content.Shared.Hands.EntitySystems.SharedHandsSystem>().TryPickup(actor, deedId), Is.True);
            var registration = em.GetComponent<KiasCrewServerComponent>(crewServer);
            Assert.That(registration.RegistrationLocked, Is.False);
            AlternativeVerb? RegistrationVerb(EntityUid user, string key)
            {
                var verbs = new GetVerbsEvent<AlternativeVerb>(user, crewServer, null, null, true, true, true, new());
                em.EventBus.RaiseLocalEvent(crewServer, verbs);
                return verbs.Verbs.SingleOrDefault(verb => verb.Text == Loc.GetString(key));
            }
            Assert.That(RegistrationVerb(stranger, "kias-lock-registration"), Is.Null);
            em.RemoveComponent<ShuttleDeedComponent>(map.Grid);
            Assert.That(RegistrationVerb(actor, "kias-lock-registration"), Is.Null, "A grid without a deed or claim must stay open for registration.");
            Assert.That(registration.RegistrationLocked, Is.False);
            em.EnsureComponent<ShuttleDeedComponent>(map.Grid).ShuttleUid = map.Grid.Owner;
            var lockVerb = RegistrationVerb(actor, "kias-lock-registration");
            Assert.That(lockVerb, Is.Not.Null);
            lockVerb!.Act!();
            Assert.That(registration.RegistrationLocked, Is.True);
            Assert.That(RegistrationVerb(stranger, "kias-unlock-registration"), Is.Null);
            var unlockVerb = RegistrationVerb(actor, "kias-unlock-registration");
            Assert.That(unlockVerb, Is.Not.Null);
            unlockVerb!.Act!();
            Assert.That(registration.RegistrationLocked, Is.False);
            var protocols = em.GetComponent<KiasProtocolComponent>(core);
            protocols.Protocols.Clear();
            var edit = new KiasProtocolMessage { Actor = stranger, Index = 0, Trigger = KiasTrigger.Manual, Action = KiasActionKind.Record, Message = "Owner only" };
            em.EventBus.RaiseLocalEvent(display, edit);
            Assert.That(protocols.Protocols, Is.Empty);
            edit.Actor = actor;
            edit.Target = em.GetNetEntity(foreign);
            em.EventBus.RaiseLocalEvent(display, edit);
            Assert.That(protocols.Protocols, Is.Empty);
            edit.Target = null;
            em.EventBus.RaiseLocalEvent(display, edit);
            Assert.That(protocols.Protocols, Is.Empty, "The retired editor cannot create invisible policies, even for an owner.");
            edit.Actions = new() { new() { Kind = KiasActionKind.Record, Message = "retired" } };
            em.EventBus.RaiseLocalEvent(display, edit);
            Assert.That(protocols.Protocols, Is.Empty);
            var audio = em.GetComponent<KiasAudioComponent>(core);
            var audioEdit = new KiasAudioSettingsMessage { Actor = stranger, Channel = KiasAudioChannel.Emergency, Preset = KiasTonePreset.Silent };
            em.EventBus.RaiseLocalEvent(display, audioEdit);
            Assert.That(audio.Emergency, Is.EqualTo(KiasTonePreset.ReactorAlarm));
            audioEdit.Actor = actor;
            em.EventBus.RaiseLocalEvent(display, audioEdit);
            Assert.That(audio.Emergency, Is.EqualTo(KiasTonePreset.Silent));
            audioEdit.Preset = (KiasTonePreset) 255;
            em.EventBus.RaiseLocalEvent(display, audioEdit);
            Assert.That(audio.Emergency, Is.EqualTo(KiasTonePreset.Silent));
            audioEdit.Preset = KiasTonePreset.Silent;
            audioEdit.Preview = true;
            em.EventBus.RaiseLocalEvent(display, audioEdit);
            var previewAfter = audio.PreviewAfter;
            em.EventBus.RaiseLocalEvent(display, audioEdit);
            Assert.That(audio.PreviewAfter, Is.EqualTo(previewAfter));
            Assert.That(em.System<KiasDefenceSystem>().SetEnabled(defence, stranger, true), Is.False);
            Assert.That(em.System<KiasDefenceSystem>().SetEnabled(defence, actor, true), Is.True);
            protocols.Alert = KiasAlert.Emergency;
            var reset = new KiasControlMessage { Actor = stranger, Reset = true };
            em.EventBus.RaiseLocalEvent(display, reset);
            Assert.That(protocols.Alert, Is.EqualTo(KiasAlert.Emergency));
            reset.Actor = actor;
            em.EventBus.RaiseLocalEvent(display, reset);
            Assert.That(protocols.Alert, Is.EqualTo(KiasAlert.Normal));
        });
        await pair.Client.WaitAssertion(() =>
        {
            using var window = new KiasWindow();
            var state = new KiasManagementState { Online = true, Entities = 4, Crew = 2 };
            Assert.DoesNotThrow(() => window.UpdateState(state));
            using var serviceWindow = new KiasServiceWindow();
            Assert.DoesNotThrow(() => serviceWindow.UpdateState(new KiasServiceState { Mode = KiasServiceMode.Link, Message = "Local only" }));
            using var scannerWindow = new KiasScannerWindow();
            Assert.DoesNotThrow(() => scannerWindow.UpdateState(new KiasScannerState { Range = 7, Modules = KiasScannerModules.Motion }));
            state.Automation = "2 cards";
            Assert.DoesNotThrow(() => window.UpdateState(state));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TestExpiresAndMovementPowerLossInvalidateRuntime()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        EntityUid core = default;
        EntityUid scanner = default;
        EntityUid tool = default;
        EntityUid adapter = default;
        await pair.Server.WaitAssertion(() =>
        {
            var maps = em.System<SharedMapSystem>();
            for (var x = 0; x < 8; x++)
                maps.SetTile(map.Grid, map.Grid.Comp, new Vector2i(x, 0), map.Tile.Tile);
            em.SpawnEntity("KiasDataCable", new EntityCoordinates(map.Grid, 0.5f, 0.5f));
            core = em.SpawnEntity("KiasCore", new EntityCoordinates(map.Grid, 0.5f, 0.5f));
            scanner = em.SpawnEntity("KiasRoomScanner", new EntityCoordinates(map.Grid, 1.5f, 0.5f));
            adapter = em.SpawnEntity("KiasDeviceAdapter", new EntityCoordinates(map.Grid, 1.5f, 0.5f));
            em.RemoveComponent<ApcPowerReceiverComponent>(core);
            em.RemoveComponent<ApcPowerReceiverComponent>(scanner);
            em.RemoveComponent<ApcPowerReceiverComponent>(adapter);
            em.System<Content.Server.DeviceLinking.Systems.DeviceLinkSystem>().SaveLinks(null, scanner, adapter, new() { ("KiasMotion", "On") });
            tool = em.SpawnEntity("KiasServiceTool", new EntityCoordinates(map.Grid, 0.5f, 0.5f));
            em.GetComponent<KiasServiceToolComponent>(tool).Mode = KiasServiceMode.Test;
            em.System<KiasSystem>().Rebuild(map.Grid);
            var protocols = em.GetComponent<KiasProtocolComponent>(core);
            protocols.Alert = KiasAlert.Contact;
            protocols.Cooldowns[100] = TimeSpan.FromSeconds(100);
            var use = new AfterInteractEvent(core, tool, scanner, new EntityCoordinates(map.Grid, 1.5f, 0.5f), true);
            em.EventBus.RaiseLocalEvent(tool, use);
            Assert.That(em.System<KiasServiceSystem>().IsTesting(map.Grid), Is.True);
            Assert.That(em.GetComponent<KiasDeviceAdapterComponent>(adapter).State, Is.False);
            protocols.Alert = KiasAlert.Emergency;
            protocols.Cooldowns[100] = TimeSpan.FromSeconds(200);
        });
        await pair.RunTicksSync(310);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.System<KiasServiceSystem>().IsTesting(map.Grid), Is.False);
            Assert.That(em.GetComponent<KiasProtocolComponent>(core).Alert, Is.EqualTo(KiasAlert.Emergency));
            Assert.That(em.GetComponent<KiasProtocolComponent>(core).Cooldowns[100], Is.EqualTo(TimeSpan.FromSeconds(200)));
            var transform = em.System<SharedTransformSystem>();
            transform.Unanchor(scanner);
            transform.SetCoordinates(scanner, new EntityCoordinates(map.Grid, 6.5f, 0.5f));
            transform.AnchorEntity(scanner);
        });
        await pair.RunTicksSync(2);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<KiasDeviceComponent>(scanner).Status, Is.EqualTo(KiasDeviceStatus.NoDataPath));
            em.AddComponent<ApcPowerReceiverComponent>(core);
            var lost = new PowerChangedEvent(false, 0);
            em.EventBus.RaiseLocalEvent(core, ref lost);
        });
        await pair.RunTicksSync(2);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.System<KiasSystem>().ActiveGrids, Does.Not.Contain(map.Grid.Owner));
            Assert.That(em.GetComponent<KiasDeviceComponent>(core).Status, Is.EqualTo(KiasDeviceStatus.NoPower));
        });
        await pair.CleanReturnAsync();
    }
}
