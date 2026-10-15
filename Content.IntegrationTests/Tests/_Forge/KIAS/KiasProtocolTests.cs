using System.Linq;
using Content.Server._Forge.KIAS;
using Content.Server._Forge.KIAS.Controllers;
using Content.Server.Power.Components;
using Content.Shared._Forge.KIAS;
using Content.Shared._Forge.KIAS.Controllers;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._Forge.KIAS;

[TestFixture]
public sealed class KiasProtocolTests
{
    private static EntityUid Spawn(IEntityManager em, EntityUid grid, string prototype, int x = 0)
    {
        var uid = em.SpawnEntity(prototype, new EntityCoordinates(grid, x + .5f, .5f));
        em.RemoveComponent<ApcPowerReceiverComponent>(uid); return uid;
    }

    private static EntityUid Install(IEntityManager em, EntityUid grid, KiasProtocolRecord record)
    {
        var rack = Spawn(em, grid, "KiasControllerRack");
        var card = Spawn(em, grid, "KiasProgrammableController");
        var io = em.System<KiasControllerIoSystem>();
        var imported = KiasLegacyGraphTranslator.Import(record, io.Supports, io.NativeSinkProfile);
        Assert.That(imported.Errors, Is.Empty);
        em.GetComponent<KiasControllerCardComponent>(card).Program = imported.Program!;
        em.System<KiasSystem>().Rebuild(grid);
        Assert.That(em.System<ItemSlotsSystem>().TryInsert(rack, KiasControllerRackComponent.SlotId(0), card, null), Is.True);
        return card;
    }

    [Test]
    public async Task MedicalAssistanceRequiresDistressAndUnavailableRegisteredCrew()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap(); var em = pair.Server.ResolveDependency<IEntityManager>();
        EntityUid body = default, crewServer = default, card = default;
        var engine = em.System<KiasProtocolSystem>();
        await pair.Server.WaitAssertion(() =>
        {
            for (var x = 0; x < 5; x++)
            {
                em.System<SharedMapSystem>().SetTile(map.Grid, map.Grid.Comp, new Vector2i(x, 0), map.Tile.Tile);
                Spawn(em, map.Grid, "KiasDataCable", x);
            }
            var core = Spawn(em, map.Grid, "KiasCore");
            crewServer = Spawn(em, map.Grid, "KiasCrewServer", 1);
            var scanner = Spawn(em, map.Grid, "KiasRoomScanner", 2);
            em.System<SharedMapSystem>().SetTile(map.Grid, map.Grid.Comp, new Vector2i(2, 1), map.Tile.Tile);
            em.System<SharedTransformSystem>().SetLocalRotation(scanner, Angle.FromDegrees(180));
            Spawn(em, map.Grid, "KiasRecorder", 3);
            var containers = em.System<SharedContainerSystem>();
            var moduleSlot = containers.GetContainer(scanner, "kias-module-3");
            foreach (var module in moduleSlot.ContainedEntities.ToArray()) em.DeleteEntity(module);
            var bio = Spawn(em, map.Grid, "KiasBiometricModule", 2);
            Assert.That(containers.Insert(bio, moduleSlot), Is.True);
            body = Spawn(em, map.Grid, "MobHuman", 2);
            em.AddComponent<KiasTrackedEntityComponent>(body);
            var token = Spawn(em, map.Grid, "KiasCrewTransponder", 2);
            var transponder = em.GetComponent<KiasTransponderComponent>(token);
            transponder.Core = core;
            em.GetComponent<KiasCrewServerComponent>(crewServer).Registered.Add(transponder.Serial);
            containers.Insert(token, containers.EnsureContainer<Container>(body, "kias-test-token"));
            var record = new KiasProtocolRecord { Trigger = KiasTrigger.VesselCritical, RequireCrewUnavailable = true };
            record.Actions.Add(new() { Kind = KiasActionKind.Record, Message = "Medical assistance" });
            card = Install(em, map.Grid, record);
        });
        await pair.RunTicksSync(40);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.System<KiasControllerRuntimeSystem>().Running(card), Is.True);
            Assert.That(em.System<KiasCrewSystem>().CrewUnavailable(map.Grid), Is.False);
            engine.Trigger(map.Grid, KiasTrigger.VesselCritical);
        });
        await pair.RunTicksSync(8);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<KiasGridComponent>(map.Grid).Log, Is.Empty);
            em.System<MobStateSystem>().ChangeMobState(body, MobState.Critical);
            Assert.That(em.System<KiasCrewSystem>().CrewUnavailable(map.Grid), Is.True);
            engine.Trigger(map.Grid, KiasTrigger.VesselCritical);
        });
        await pair.RunTicksSync(8);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<KiasGridComponent>(map.Grid).Log.Count(e => e.Contains("Medical assistance")), Is.EqualTo(1));
            engine.Trigger(map.Grid, KiasTrigger.Manual);
            em.GetComponent<KiasCrewServerComponent>(crewServer).Registered.Clear();
            Assert.That(em.System<KiasCrewSystem>().CrewUnavailable(map.Grid), Is.False);
        });
        await pair.RunTicksSync(8);
        await pair.Server.WaitAssertion(() => Assert.That(em.GetComponent<KiasGridComponent>(map.Grid).Log.Count(e => e.Contains("Medical assistance")), Is.EqualTo(1)));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CooldownTestModeForeignTargetMaydayAndHardOff()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap(); var em = pair.Server.ResolveDependency<IEntityManager>();
        EntityUid core = default, relay = default, card = default;
        var engine = em.System<KiasProtocolSystem>();
        await pair.Server.WaitAssertion(() =>
        {
            for (var x = 0; x < 6; x++)
            {
                em.System<SharedMapSystem>().SetTile(map.Grid, map.Grid.Comp, new Vector2i(x, 0), map.Tile.Tile);
                Spawn(em, map.Grid, "KiasDataCable", x);
            }
            core = Spawn(em, map.Grid, "KiasCore");
            Spawn(em, map.Grid, "KiasNavigationServer", 1); Spawn(em, map.Grid, "KiasRecorder", 2);
            Spawn(em, map.Grid, "KiasMaydayAntenna", 3); relay = Spawn(em, map.Grid, "KiasRelay", 4);
            em.EnsureComponent<IFFComponent>(map.Grid);
            var record = new KiasProtocolRecord { Trigger = KiasTrigger.Manual, Cooldown = 10 };
            record.Actions.Add(new() { Kind = KiasActionKind.Record, Message = "Once" });
            record.Actions.Add(new() { Kind = KiasActionKind.Relay, Target = relay, Value = false });
            card = Install(em, map.Grid, record);
            var foreign = em.SpawnEntity("KiasRelay", MapCoordinates.Nullspace);
            record.Actions[1].Target = foreign;
            var io = em.System<KiasControllerIoSystem>();
            var imported = KiasLegacyGraphTranslator.Import(record, io.Supports, io.NativeSinkProfile);
            Assert.That(imported.Program!.Nodes, Has.Some.Matches<KiasControllerNode>(n => n.Binding == foreign));
        });
        await pair.RunTicksSync(40);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.System<KiasControllerRuntimeSystem>().Running(card), Is.True);
            em.GetComponent<KiasGridComponent>(map.Grid).Testing = true;
            engine.Trigger(map.Grid, KiasTrigger.Manual, eventKey: "test");
            engine.Trigger(map.Grid, KiasTrigger.Manual, eventKey: "test");
        });
        await pair.RunTicksSync(8);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<KiasGridComponent>(map.Grid).Log, Has.Count.EqualTo(1));
            Assert.That(em.GetComponent<KiasRelayComponent>(relay).Closed, Is.True);
            em.GetComponent<KiasGridComponent>(map.Grid).Testing = false;
            engine.Trigger(map.Grid, KiasTrigger.Manual, eventKey: "live");
        });
        await pair.RunTicksSync(8);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<KiasRelayComponent>(relay).Closed, Is.False);
            Assert.That(em.GetComponent<KiasGridComponent>(map.Grid).Log, Has.Count.EqualTo(2));
            em.System<KiasSystem>().Rebuild(map.Grid);
            Assert.That(engine.Mayday(map.Grid, "Test distress"), Is.True);
            Assert.That(engine.Mayday(map.Grid, "Repeat"), Is.False);
            Assert.That(em.System<SharedShuttleSystem>().GetIFFLabel(map.Grid), Does.Contain("[MAYDAY]"));
            em.System<KiasSystem>().SetEnabled(core, false);
            Assert.That(em.System<KiasControllerRuntimeSystem>().Running(card), Is.False);
            engine.Trigger(map.Grid, KiasTrigger.Manual, eventKey: "off");
            Assert.That(em.HasComponent<KiasMaydayComponent>(map.Grid), Is.False);
        });
        await pair.CleanReturnAsync();
    }
}
