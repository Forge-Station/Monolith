using System.Linq;
using Content.Server._Forge.KIAS;
using Content.Server.Power.Components;
using Content.Shared._Forge.KIAS;
using Content.Shared.Light.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Atmos.Components;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.DeviceLinking.Events;
using Content.Shared._Forge.KIAS.Controllers;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Verbs;
using Content.Shared.Atmos.Monitor;
using Content.Server.Atmos.Monitor.Systems;
using Content.Server._Forge.KIAS.Controllers;
using Robust.Shared.Localization;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._Forge.KIAS;

[TestFixture]
public sealed class KiasActuatorTests
{
    [Test]
    public async Task NativeFireAlarmRunsSuppressionGraphAndManualVerbReloads()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        EntityUid suppression = default, alarm = default, card = default, actor = default;
        await pair.Server.WaitAssertion(() =>
        {
            EntityUid Spawn(string id)
            {
                var uid = em.SpawnEntity(id, new EntityCoordinates(map.Grid, .5f, .5f));
                em.RemoveComponent<ApcPowerReceiverComponent>(uid);
                return uid;
            }
            Spawn("KiasDataCable"); Spawn("KiasCore"); Spawn("KiasAtmosServer");
            suppression = Spawn("KiasSuppression"); alarm = Spawn("FireAlarm"); actor = Spawn("MobHuman");
            em.EnsureComponent<KiasIntegratedComponent>(alarm).Direct = true;
            em.EnsureComponent<KiasDeviceComponent>(alarm).Role = KiasDeviceRole.Adapter;
            var rack = Spawn("KiasControllerRack"); card = Spawn("KiasProgrammableController");
            var program = new KiasControllerProgram();
            program.Nodes.Add(new() { Id = 1, Kind = KiasNodeKind.Any, Profile = "AtmosSafety" });
            program.Nodes.Add(new() { Id = 2, Kind = KiasNodeKind.Specific, Profile = "Suppression", Binding = suppression });
            program.Nodes.Add(new() { Id = 3, Kind = KiasNodeKind.All, Profile = "Suppression" });
            foreach (var target in new[] { 2, 3 })
                program.Wires.Add(new() { FromNode = 1, FromPort = "Fire", ToNode = target, ToPort = "Trigger" });
            em.GetComponent<KiasControllerCardComponent>(card).Program = program;
            Assert.That(em.System<ItemSlotsSystem>().TryInsert(rack, KiasControllerRackComponent.SlotId(0), card, null), Is.True);
        });
        await pair.RunTicksSync(40);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.System<KiasControllerRuntimeSystem>().Running(card), Is.True, em.System<KiasControllerRuntimeSystem>().Fault(card));
            em.System<AtmosAlarmableSystem>().ForceAlert(alarm, AtmosAlarmType.Danger);
        });
        await pair.RunTicksSync(10);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.System<SharedContainerSystem>().TryGetContainer(suppression, "kias-cartridge", out var slot), Is.True);
            Assert.That(slot!.ContainedEntities, Is.Empty);
            var nearby = new System.Collections.Generic.HashSet<EntityUid>();
            em.System<EntityLookupSystem>().GetEntitiesInRange(suppression, 1, nearby);
            Assert.That(nearby.Count(uid => em.HasComponent<SmokeComponent>(uid)), Is.EqualTo(1));
            var cartridge = em.SpawnEntity("KiasSuppressionCartridge", new EntityCoordinates(map.Grid, .5f, .5f));
            Assert.That(em.System<SharedContainerSystem>().Insert(cartridge, slot), Is.True);
            var verbs = new GetVerbsEvent<AlternativeVerb>(actor, suppression, null, null, true, true, true, new());
            em.EventBus.RaiseLocalEvent(suppression, verbs);
            verbs.Verbs.Single(verb => verb.Text == Loc.GetString("kias-suppress")).Act!();
            Assert.That(em.IsQueuedForDeletion(cartridge), Is.True);
            Assert.That(em.System<KiasActuatorSystem>().Suppress(suppression), Is.False);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DeviceLinkSuppressionCreatesFoamAndExtinguishesNativeHotspotOnce()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        EntityUid suppression = default;
        await pair.Server.WaitAssertion(() =>
        {
            em.System<AtmosphereSystem>().SetMapAtmosphere(map.MapUid, false, new GasMixture(Atmospherics.CellVolume));
            em.EnsureComponent<GasTileOverlayComponent>(map.Grid);
            em.EnsureComponent<GridAtmosphereComponent>(map.Grid);
            em.System<SharedMapSystem>().SetTile(map.Grid, map.Grid.Comp, new Vector2i(0, 0), map.Tile.Tile);
            foreach (var id in new[] { "KiasDataCable", "KiasCore", "KiasAtmosServer", "KiasSuppression" })
            {
                var uid = em.SpawnEntity(id, new EntityCoordinates(map.Grid, .5f, .5f));
                em.RemoveComponent<ApcPowerReceiverComponent>(uid);
                if (id == "KiasSuppression") suppression = uid;
            }
        });
        await pair.RunTicksSync(30);
        await pair.Server.WaitAssertion(() =>
        {
            var atmos = em.System<AtmosphereSystem>();
            var tile = new Vector2i(0, 0);
            var gas = atmos.GetTileMixture(map.Grid.Owner, null, tile, true);
            Assert.That(gas, Is.Not.Null);
            gas!.SetMoles(Gas.Oxygen, 20);
            gas.SetMoles(Gas.Plasma, 5);
            gas.Temperature = 1000;
            atmos.HotspotExpose((map.Grid.Owner, null), tile, 1000, 100);
            Assert.That(atmos.IsHotspotActive(map.Grid, tile), Is.True);
            var signal = new SignalReceivedEvent("KiasSuppress");
            em.EventBus.RaiseLocalEvent(suppression, ref signal);
            Assert.That(atmos.IsHotspotActive(map.Grid, tile), Is.False);
            Assert.That(gas.Temperature, Is.LessThan(1000));
            var nearby = new System.Collections.Generic.HashSet<EntityUid>();
            em.System<EntityLookupSystem>().GetEntitiesInRange(suppression, 1, nearby);
            var foam = nearby.Where(uid => em.HasComponent<SmokeComponent>(uid)).ToArray();
            Assert.That(foam, Has.Length.EqualTo(1));
            Assert.That(em.System<KiasActuatorSystem>().Suppress(suppression), Is.False);
        });
        await pair.RunTicksSync(3);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.System<SharedContainerSystem>().TryGetContainer(suppression, "kias-cartridge", out var slot), Is.True);
            Assert.That(slot!.ContainedEntities, Is.Empty);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task LightGroupsCartridgeConsumptionAndHardOff()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var maps = em.System<SharedMapSystem>();
            for (var x = 0; x < 5; x++)
            {
                maps.SetTile(map.Grid, map.Grid.Comp, new Vector2i(x, 0), map.Tile.Tile);
                em.SpawnEntity("KiasDataCable", new EntityCoordinates(map.Grid, x + 0.5f, 0.5f));
            }
            EntityUid Spawn(string proto, int x)
            {
                var uid = em.SpawnEntity(proto, new EntityCoordinates(map.Grid, x + 0.5f, 0.5f));
                em.RemoveComponent<ApcPowerReceiverComponent>(uid);
                return uid;
            }
            var core = Spawn("KiasCore", 0);
            Spawn("KiasAtmosServer", 1);
            var controller = Spawn("KiasLightController", 2);
            var suppression = Spawn("KiasSuppression", 3);
            var lamp = Spawn("PoweredlightEmpty", 4);
            var otherLamp = Spawn("PoweredlightEmpty", 4);
            em.AddComponent<KiasLightGroupComponent>(lamp).Group = "EMERGENCY";
            em.AddComponent<KiasLightGroupComponent>(otherLamp).Group = "ENGINEERING";
            em.GetComponent<KiasLightControllerComponent>(controller).Group = "EMERGENCY";
            var kias = em.System<KiasSystem>();
            kias.Rebuild(map.Grid);
            var actuators = em.System<KiasActuatorSystem>();
            actuators.SetLights(controller, false);
            var on = em.GetComponent<PoweredLightComponent>(lamp).On;
            var otherOn = em.GetComponent<PoweredLightComponent>(otherLamp).On;
            Assert.That(on, Is.False);
            Assert.That(otherOn, Is.True);
            Assert.That(actuators.Suppress(suppression), Is.True);
            Assert.That(actuators.Suppress(suppression), Is.False);
            var containers = em.System<SharedContainerSystem>();
            containers.TryGetContainer(suppression, "kias-cartridge", out var slot);
            foreach (var old in slot!.ContainedEntities.ToArray())
                containers.Remove(old, slot);
            var cartridge = em.SpawnEntity("KiasSuppressionCartridge", new EntityCoordinates(map.Grid, 3.5f, 0.5f));
            containers.Insert(cartridge, slot);
            kias.SetEnabled(core, false);
            actuators.SetLights(controller, true);
            on = em.GetComponent<PoweredLightComponent>(lamp).On;
            Assert.That(on, Is.False);
            Assert.That(actuators.Suppress(suppression), Is.False);
            Assert.That(slot.Contains(cartridge), Is.True);
        });
        await pair.CleanReturnAsync();
    }
}
