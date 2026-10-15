#pragma warning disable RA0002
using System.Collections.Generic;
using System.Linq;
using Content.Server._Forge.KIAS;
using Content.Server.DeviceLinking.Systems;
using Content.Server.Power.Components;
using Content.Server.Radio.EntitySystems;
using Content.Shared._Forge.KIAS;
using Content.Shared._Forge.KIAS.Controllers;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.DeviceLinking.Events;
using Content.Shared.Interaction;
using Content.Shared.Radio.Components;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._Forge.KIAS;

[TestFixture]
public sealed class KiasParityTests
{
    [Test]
    public async Task StarterModulesRoomBridgeKitAndNativeJammerHaveGameplayEffects()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        EntityUid core = default;
        EntityUid scanner = default;
        EntityUid alarm = default;
        EntityUid jammer = default;
        var kias = em.System<KiasSystem>();
        await pair.Server.WaitAssertion(() =>
        {
            for (var x = 0; x < 12; x++)
            for (var y = 0; y < 2; y++) em.System<SharedMapSystem>().SetTile(map.Grid, map.Grid.Comp, new Vector2i(x, y), map.Tile.Tile);
            for (var x = 0; x < 4; x++) em.SpawnEntity("KiasDataCable", new EntityCoordinates(map.Grid, x + 0.5f, 0.5f));
            EntityUid Spawn(string prototype, int x)
            {
                var uid = em.SpawnEntity(prototype, new EntityCoordinates(map.Grid, x + 0.5f, 0.5f));
                em.RemoveComponent<ApcPowerReceiverComponent>(uid);
                return uid;
            }
            core = Spawn("KiasCore", 0);
            scanner = Spawn("KiasRoomScanner", 1);
            em.System<SharedTransformSystem>().SetLocalRotation(scanner, Angle.FromDegrees(180));
            alarm = Spawn("AirAlarm", 7);
            var advanced = Spawn("KiasAdvancedRoomScanner", 2);
            Spawn("KiasDefenceServer", 3);
            jammer = Spawn("KiasJammer", 3);
            kias.Rebuild(map.Grid);
            kias.Rebuild(map.Grid);
            var starter = KiasScannerModules.Identity | KiasScannerModules.Connector | KiasScannerModules.Optical;
            Assert.That(em.GetComponent<KiasRoomScannerComponent>(scanner).Modules & starter, Is.EqualTo(starter));
            Assert.That(em.GetComponent<KiasRoomScannerComponent>(advanced).Modules & KiasScannerModules.Biometric, Is.EqualTo(KiasScannerModules.Biometric));
            Assert.That(em.GetComponent<KiasRoomScannerComponent>(advanced).Modules & KiasScannerModules.Threat, Is.EqualTo(KiasScannerModules.Threat));
        });
        await pair.RunTicksSync(60);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(kias.IsOnline(alarm), Is.True, "The room module bridges a native alarm outside the DATA service radius.");
            Assert.That(em.GetComponent<KiasIntegratedComponent>(alarm).Scanner, Is.EqualTo(scanner));
            Assert.That(em.System<JammerSystem>().SetEnabled(jammer, true), Is.True);
            Assert.That(em.HasComponent<ActiveRadioJammerComponent>(jammer), Is.True);
            var containers = em.System<SharedContainerSystem>();
            Assert.That(em.GetComponent<Content.Shared.SurveillanceCamera.Components.SurveillanceCameraComponent>(scanner).Active, Is.True);
            var optical = containers.GetContainer(scanner, "kias-module-5").ContainedEntities.Single();
            containers.Remove(optical, containers.GetContainer(scanner, "kias-module-5"));
            em.System<Content.Shared.SurveillanceCamera.SharedSurveillanceCameraSystem>().SetActive(scanner, true);
            Assert.That(em.GetComponent<Content.Shared.SurveillanceCamera.Components.SurveillanceCameraComponent>(scanner).Active, Is.False,
                "Native power/EMP recovery cannot reactivate a camera without its optical module.");
            var connector = containers.GetContainer(scanner, "kias-module-6").ContainedEntities.Single();
            containers.Remove(connector, containers.GetContainer(scanner, "kias-module-6"));
            kias.Rebuild(map.Grid);
            Assert.That(kias.IsOnline(alarm), Is.False, "Removing the physical connector removes its room bridge.");
            var machine = em.SpawnEntity("Autolathe", new EntityCoordinates(map.Grid, 3.5f, 0.5f));
            var actor = em.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid, 3.5f, 0.5f));
            var kit = em.SpawnEntity("KiasIntegrationKit", new EntityCoordinates(map.Grid, 3.5f, 0.5f));
            em.EventBus.RaiseLocalEvent(kit, new AfterInteractEvent(actor, kit, machine, new EntityCoordinates(map.Grid, 3.5f, 0.5f), true));
            Assert.That(em.HasComponent<KiasIntegratedComponent>(machine), Is.True);
            kias.Rebuild(map.Grid);
            var off = new SignalReceivedEvent("Off", core);
            em.EventBus.RaiseLocalEvent(machine, ref off);
            Assert.That(em.GetComponent<ApcPowerReceiverComponent>(machine).PowerDisabled, Is.True);
            var on = new SignalReceivedEvent("On", core);
            em.EventBus.RaiseLocalEvent(machine, ref on);
            Assert.That(em.GetComponent<ApcPowerReceiverComponent>(machine).PowerDisabled, Is.False);
        });
        await pair.RunTicksSync(3);
        await pair.Server.WaitAssertion(() =>
        {
            em.System<KiasSystem>().SetEnabled(core, false);
            Assert.That(em.System<KiasIntegrationSystem>().CanControl(alarm), Is.False);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SeparateSourcesDoNotHideCriticalProtocolsAndFireLockHasHardOffFallback()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        EntityUid core = default, defence = default, recorder = default, weapon = default;
        await pair.Server.WaitAssertion(() =>
        {
            for (var x = 0; x < 5; x++)
            {
                em.System<SharedMapSystem>().SetTile(map.Grid, map.Grid.Comp, new Vector2i(x, 0), map.Tile.Tile);
                em.SpawnEntity("KiasDataCable", new EntityCoordinates(map.Grid, x + 0.5f, 0.5f));
            }
            EntityUid Spawn(string prototype, int x)
            {
                var uid = em.SpawnEntity(prototype, new EntityCoordinates(map.Grid, x + 0.5f, 0.5f));
                em.RemoveComponent<ApcPowerReceiverComponent>(uid);
                return uid;
            }
            core = Spawn("KiasCore", 0);
            defence = Spawn("KiasDefenceServer", 1);
            recorder = Spawn("KiasRecorder", 2);
            var console = Spawn("KiasManagementConsole", 3);
            weapon = Spawn("WeaponTurretFlare", 4);
            var kias = em.System<KiasSystem>();
            kias.Rebuild(map.Grid);
            var protocols = em.GetComponent<KiasProtocolComponent>(core);
            protocols.Protocols.Clear();
            protocols.Protocols.Add(new KiasProtocolRecord { Trigger = KiasTrigger.CrewCritical, Cooldown = 60,
                Actions = new List<KiasProtocolAction> { new() { Kind = KiasActionKind.Record, Message = "injured" } } });
            var rack = Spawn("KiasControllerRack", 0);
            var card = Spawn("KiasProgrammableController", 0);
            var io = em.System<Content.Server._Forge.KIAS.Controllers.KiasControllerIoSystem>();
            var imported = KiasLegacyGraphTranslator.Import(protocols.Protocols[0], io.Supports, io.NativeSinkProfile);
            Assert.That(imported.Errors, Is.Empty);
            em.GetComponent<KiasControllerCardComponent>(card).Program = imported.Program!;
            kias.Rebuild(map.Grid);
            Assert.That(em.System<ItemSlotsSystem>().TryInsert(rack, KiasControllerRackComponent.SlotId(0), card, null), Is.True);
        });
        await pair.RunTicksSync(40);
        await pair.Server.WaitAssertion(() =>
        {
            var engine = em.System<KiasProtocolSystem>();
            engine.Trigger(map.Grid, KiasTrigger.CrewCritical, eventKey: "fore");
            engine.Trigger(map.Grid, KiasTrigger.CrewCritical, eventKey: "aft");
            engine.Trigger(map.Grid, KiasTrigger.CrewCritical, eventKey: "fore");
        });
        await pair.RunTicksSync(8);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<KiasGridComponent>(map.Grid).Log, Has.Count.EqualTo(2));
            Assert.That(em.GetComponent<KiasRecorderComponent>(recorder).Entries.Count(e => e.Contains("injured")), Is.EqualTo(2));
            var kias = em.System<KiasSystem>();
            em.GetComponent<KiasDefenceComponent>(defence).FireLock = true;
            Assert.That(em.System<KiasDefenceSystem>().IsFireLocked(weapon), Is.True);
            kias.SetEnabled(core, false);
            Assert.That(em.System<KiasDefenceSystem>().IsFireLocked(weapon), Is.False);
            Assert.That(em.System<KiasDefenceSystem>().AuthorizeFire(weapon, false), Is.True);
        });
        await pair.CleanReturnAsync();
    }
}
