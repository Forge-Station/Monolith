#pragma warning disable RA0002
using System.Linq;
using System.Collections.Generic;
using Content.Server._Forge.KIAS;
using Content.Server._Forge.KIAS.Controllers;
using Content.Server.Power.Components;
using Content.Shared._Forge.KIAS;
using Content.Shared._Forge.KIAS.Controllers;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Interaction;
using Content.Shared.SurveillanceCamera.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._Forge.KIAS;

[TestFixture]
public sealed class KiasReviewRegressionTests
{
    [Test]
    public async Task LosingLastPowerRoleClearsRecordedDeficit()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() =>
        {
            EntityUid Spawn(string id)
            {
                var uid = em.SpawnEntity(id, new EntityCoordinates(map.Grid, .5f, .5f));
                em.RemoveComponent<ApcPowerReceiverComponent>(uid); return uid;
            }
            Spawn("KiasCore"); Spawn("KiasDataCable"); var power = Spawn("KiasPowerServer");
            var kias = em.System<KiasSystem>(); var system = em.System<KiasPowerSystem>();
            kias.Rebuild(map.Grid);
            Assert.That(kias.HasRole(map.Grid, KiasDeviceRole.Power), Is.True);
            var deficits = (Dictionary<EntityUid, bool[]>) typeof(KiasPowerSystem)
                .GetField("_deficits", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(system)!;
            deficits[map.Grid] = new[] { true, false, false };
            Assert.That(system.HasDeficit(map.Grid), Is.True);
            em.DeleteEntity(power); kias.Rebuild(map.Grid);
            Assert.That(kias.ActiveGrids, Does.Contain(map.Grid.Owner));
            Assert.That(kias.HasRole(map.Grid, KiasDeviceRole.Power), Is.False);
            Assert.That(system.HasDeficit(map.Grid), Is.False);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RoomEditReindexesLiveSelectors()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        EntityUid scanner = default, card = default, actor = default;
        await pair.Server.WaitAssertion(() =>
        {
            EntityUid Spawn(string id)
            {
                var uid = em.SpawnEntity(id, new EntityCoordinates(map.Grid, .5f, .5f));
                em.RemoveComponent<ApcPowerReceiverComponent>(uid); return uid;
            }
            Spawn("KiasCore"); Spawn("KiasDataCable"); scanner = Spawn("KiasRoomScanner");
            em.GetComponent<KiasDeviceComponent>(scanner).Room = "A";
            actor = Spawn("MobHuman"); var rack = Spawn("KiasControllerRack"); card = Spawn("KiasProgrammableController");
            var program = em.GetComponent<KiasControllerCardComponent>(card).Program;
            program.Nodes.Add(new() { Id = 1, Kind = KiasNodeKind.Any, Profile = "RoomScanner", Room = "A" });
            program.Nodes.Add(new() { Id = 2, Kind = KiasNodeKind.Any, Profile = "RoomScanner", Room = "B" });
            em.System<KiasSystem>().Rebuild(map.Grid);
            Assert.That(em.System<ItemSlotsSystem>().TryInsert(rack, KiasControllerRackComponent.SlotId(0), card, null), Is.True);
        });
        await pair.RunTicksSync(15);
        await pair.Server.WaitAssertion(() =>
        {
            var runtime = em.System<KiasControllerRuntimeSystem>();
            Assert.That(runtime.LastValue(card, 1, "$MatchedCount").Number, Is.EqualTo(1));
            Assert.That(runtime.LastValue(card, 2, "$MatchedCount").Number, Is.Zero);
            em.EventBus.RaiseLocalEvent(scanner, new KiasDeviceSettingsMessage { Actor = actor, Room = "B", Range = 10 });
            Assert.That(em.System<KiasSystem>().TopologyPending(map.Grid), Is.True);
        });
        await pair.RunTicksSync(10);
        await pair.Server.WaitAssertion(() =>
        {
            var runtime = em.System<KiasControllerRuntimeSystem>();
            Assert.That(runtime.LastValue(card, 1, "$MatchedCount").Number, Is.Zero);
            Assert.That(runtime.LastValue(card, 2, "$MatchedCount").Number, Is.EqualTo(1));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task FireAlarmReconnectReconcilesNativeState()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() =>
        {
            EntityUid Spawn(string id)
            {
                var uid = em.SpawnEntity(id, new EntityCoordinates(map.Grid, .5f, .5f));
                em.RemoveComponent<ApcPowerReceiverComponent>(uid); return uid;
            }
            Spawn("KiasCore"); Spawn("KiasDataCable"); Spawn("KiasAtmosServer"); var alarm = Spawn("FireAlarm");
            em.EnsureComponent<KiasIntegratedComponent>(alarm).Direct = true;
            var native = em.GetComponent<Content.Server.Atmos.Monitor.Components.AtmosAlarmableComponent>(alarm);
            native.LastAlarmState = Content.Shared.Atmos.Monitor.AtmosAlarmType.Danger;
            var kias = em.System<KiasSystem>(); kias.Rebuild(map.Grid);
            var protocols = em.System<KiasProtocolSystem>();
            var sources = (HashSet<EntityUid>) typeof(KiasProtocolSystem)
                .GetField("_fireAlarms", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(protocols)!;
            em.EventBus.RaiseLocalEvent(alarm, new Content.Server.Atmos.Monitor.Systems.AtmosAlarmEvent(native.LastAlarmState, alarm));
            Assert.That(sources, Does.Contain(alarm));
            var transforms = em.System<SharedTransformSystem>();
            transforms.Unanchor(alarm); transforms.SetCoordinates(alarm, new EntityCoordinates(map.Grid, 7.5f, .5f)); transforms.AnchorEntity(alarm);
            kias.Rebuild(map.Grid);
            native.LastAlarmState = Content.Shared.Atmos.Monitor.AtmosAlarmType.Normal;
            em.EventBus.RaiseLocalEvent(alarm, new Content.Server.Atmos.Monitor.Systems.AtmosAlarmEvent(native.LastAlarmState, alarm));
            transforms.Unanchor(alarm); transforms.SetCoordinates(alarm, new EntityCoordinates(map.Grid, .5f, .5f)); transforms.AnchorEntity(alarm);
            kias.Rebuild(map.Grid);
            Assert.That(em.System<KiasIntegrationSystem>().CanControl(alarm), Is.True);
            Assert.That(sources, Does.Not.Contain(alarm));
            native.LastAlarmState = Content.Shared.Atmos.Monitor.AtmosAlarmType.Danger;
            kias.Rebuild(map.Grid);
            Assert.That(sources, Does.Contain(alarm));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RebuildPreservesUnchangedBooleanOutput()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var map = await pair.CreateTestMap();
        var destination = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() =>
        {
            EntityUid Spawn(string id)
            {
                var uid = em.SpawnEntity(id, new EntityCoordinates(map.Grid, .5f, .5f));
                em.RemoveComponent<ApcPowerReceiverComponent>(uid); return uid;
            }
            var core = Spawn("KiasCore"); Spawn("KiasDataCable"); var adapter = Spawn("KiasDeviceAdapter");
            var kias = em.System<KiasSystem>(); var io = em.System<KiasControllerIoSystem>();
            kias.Rebuild(map.Grid);
            io.Emit(adapter, "DeviceAdapter", "State", KiasGraphValue.Boolean(true));
            Assert.That(io.TryOutput(adapter, "DeviceAdapter", "State", out var before, out _), Is.True);
            Assert.That(before.Bool, Is.True);
            io.Emit(core, "Automation", "Manual", KiasGraphValue.Pulse);
            kias.Rebuild(map.Grid);
            Assert.That(kias.IsOnline(adapter), Is.True);
            Assert.That(io.TryOutput(adapter, "DeviceAdapter", "State", out var after, out _), Is.True,
                "An unchanged connected output must survive a rebuild.");
            Assert.That(after.Bool, Is.True);
            Assert.That(io.TryOutput(core, "Automation", "Manual", out _, out _), Is.False);
            var otherCore = em.SpawnEntity("KiasCore", new EntityCoordinates(destination.Grid, .5f, .5f));
            em.RemoveComponent<ApcPowerReceiverComponent>(otherCore);
            em.SpawnEntity("KiasDataCable", new EntityCoordinates(destination.Grid, .5f, .5f));
            var transforms = em.System<SharedTransformSystem>();
            transforms.Unanchor(adapter); transforms.SetCoordinates(adapter, new EntityCoordinates(destination.Grid, .5f, .5f)); transforms.AnchorEntity(adapter);
            kias.Rebuild(destination.Grid);
            Assert.That(kias.IsOnline(adapter), Is.True);
            Assert.That(io.TryOutput(adapter, "DeviceAdapter", "State", out _, out _), Is.False,
                "Moving to another grid must not retain the old grid snapshot.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DataDisconnectionDeactivatesOpticalCamera()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var map = await pair.CreateTestMap();
        var unpoweredMap = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        EntityUid scanner = default;
        await pair.Server.WaitAssertion(() =>
        {
            for (var x = 0; x < 8; x++) em.System<SharedMapSystem>().SetTile(map.Grid, map.Grid.Comp, new Vector2i(x, 0), map.Tile.Tile);
            EntityUid Spawn(string id)
            {
                var uid = em.SpawnEntity(id, new EntityCoordinates(map.Grid, .5f, .5f));
                em.RemoveComponent<ApcPowerReceiverComponent>(uid); return uid;
            }
            Spawn("KiasCore"); Spawn("KiasDataCable"); scanner = Spawn("KiasRoomScanner");
            em.System<KiasSystem>().Rebuild(map.Grid);
        });
        await pair.RunTicksSync(10);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<SurveillanceCameraComponent>(scanner).Active, Is.True);
            var transform = em.System<SharedTransformSystem>();
            transform.Unanchor(scanner); transform.SetCoordinates(scanner, new EntityCoordinates(map.Grid, 7.5f, .5f)); transform.AnchorEntity(scanner);
            em.System<KiasSystem>().Rebuild(map.Grid);
            Assert.That(em.System<KiasSystem>().IsOnline(scanner), Is.False);
            Assert.That(em.GetComponent<SurveillanceCameraComponent>(scanner).Active, Is.False);
            transform.Unanchor(scanner); transform.SetCoordinates(scanner, new EntityCoordinates(map.Grid, .5f, .5f)); transform.AnchorEntity(scanner);
            em.System<KiasSystem>().Rebuild(map.Grid);
            Assert.That(em.GetComponent<SurveillanceCameraComponent>(scanner).Active, Is.True);
            transform.Unanchor(scanner); transform.SetCoordinates(scanner, new EntityCoordinates(unpoweredMap.Grid, .5f, .5f)); transform.AnchorEntity(scanner);
            em.System<KiasSystem>().Rebuild(map.Grid);
            Assert.That(em.GetComponent<SurveillanceCameraComponent>(scanner).Active, Is.False);
        });
        await pair.CleanReturnAsync();
    }

    [TestCase(2)]
    [TestCase(40)]
    public async Task DirtyFirstGridDoesNotBlockSecondGridEvent(int grids)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var a = await pair.CreateTestMap(); var b = await pair.CreateTestMap();
        var additional = new List<EntityUid>();
        for (var i = 2; i < grids; i++) additional.Add((await pair.CreateTestMap()).Grid.Owner);
        var healthy = new List<(EntityUid Core, EntityUid Card)>();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        EntityUid coreA = default, coreB = default, cardB = default;
        await pair.Server.WaitAssertion(() =>
        {
            (EntityUid Core, EntityUid Card) Setup(EntityUid grid)
            {
                EntityUid Spawn(string id)
                {
                    var uid = em.SpawnEntity(id, new EntityCoordinates(grid, .5f, .5f));
                    em.RemoveComponent<ApcPowerReceiverComponent>(uid); return uid;
                }
                var core = Spawn("KiasCore"); Spawn("KiasDataCable"); var rack = Spawn("KiasControllerRack");
                var card = Spawn("KiasProgrammableController"); var program = new KiasControllerProgram();
                program.Nodes.Add(new() { Id = 1, Kind = KiasNodeKind.Any, Profile = "Automation" });
                program.Nodes.Add(new() { Id = 2, Kind = KiasNodeKind.Counter });
                program.Wires.Add(new() { FromNode = 1, FromPort = "Manual", ToNode = 2, ToPort = "Increment" });
                em.GetComponent<KiasControllerCardComponent>(card).Program = program;
                em.System<KiasSystem>().Rebuild(grid);
                Assert.That(em.System<ItemSlotsSystem>().TryInsert(rack, KiasControllerRackComponent.SlotId(0), card, null), Is.True);
                return (core, card);
            }
            (coreA, _) = Setup(a.Grid); (coreB, cardB) = Setup(b.Grid);
            foreach (var grid in additional) healthy.Add(Setup(grid));
        });
        await pair.RunTicksSync(15);
        await pair.Server.WaitAssertion(() =>
        {
            var runtime = em.System<KiasControllerRuntimeSystem>(); var io = em.System<KiasControllerIoSystem>();
            Assert.That(runtime.Running(cardB), Is.True);
            io.Emit(coreA, "Automation", "Manual", KiasGraphValue.Pulse);
            io.Emit(coreB, "Automation", "Manual", KiasGraphValue.Pulse);
            foreach (var (core, _) in healthy) io.Emit(core, "Automation", "Manual", KiasGraphValue.Pulse);
            em.System<KiasSystem>().Invalidate(a.Grid);
            runtime.Update(0);
            Assert.That(runtime.LastValue(cardB, 2, "Value").Number, Is.EqualTo(1),
                "A ready ship must continue while another topology is pending.");
            foreach (var (_, card) in healthy) Assert.That(runtime.LastValue(card, 2, "Value").Number, Is.EqualTo(1));
            em.System<KiasSystem>().Rebuild(a.Grid); runtime.Update(0);
            Assert.That(runtime.LastValue(cardB, 2, "Value").Number, Is.EqualTo(1));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TestPulseCannotActuateAfterFiveSecondTestExpires()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        EntityUid scanner = default, adapter = default, tool = default, core = default, card = default;
        await pair.Server.WaitAssertion(() =>
        {
            EntityUid Spawn(string id)
            {
                var uid = em.SpawnEntity(id, new EntityCoordinates(map.Grid, .5f, .5f));
                em.RemoveComponent<ApcPowerReceiverComponent>(uid); return uid;
            }
            core = Spawn("KiasCore"); Spawn("KiasDataCable"); scanner = Spawn("KiasRoomScanner");
            adapter = Spawn("KiasDeviceAdapter"); var rack = Spawn("KiasControllerRack"); card = Spawn("KiasProgrammableController");
            tool = Spawn("KiasServiceTool"); em.GetComponent<KiasServiceToolComponent>(tool).Mode = KiasServiceMode.Test;
            em.System<KiasSystem>().Rebuild(map.Grid);
            var io = em.System<KiasControllerIoSystem>();
            var profile = io.Profiles(scanner).Single(p => p.StartsWith("Link."));
            var program = new KiasControllerProgram();
            program.Nodes.Add(new() { Id = 1, Kind = KiasNodeKind.Specific, Profile = profile, Binding = scanner });
            program.Nodes.Add(new() { Id = 2, Kind = KiasNodeKind.Timer, Config = new() { Seconds = 6 } });
            program.Nodes.Add(new() { Id = 3, Kind = KiasNodeKind.Specific, Profile = "DeviceAdapter", Binding = adapter });
            program.Wires.Add(new() { FromNode = 1, FromPort = "out:KiasMotion", ToNode = 2, ToPort = "Trigger" });
            program.Wires.Add(new() { FromNode = 2, FromPort = "Elapsed", ToNode = 3, ToPort = "On" });
            em.GetComponent<KiasControllerCardComponent>(card).Program = program;
            Assert.That(em.System<ItemSlotsSystem>().TryInsert(rack, KiasControllerRackComponent.SlotId(0), card, null), Is.True);
        });
        await pair.RunTicksSync(15);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.System<KiasControllerRuntimeSystem>().Running(card), Is.True, em.System<KiasControllerRuntimeSystem>().Fault(card));
            var use = new AfterInteractEvent(core, tool, scanner, new EntityCoordinates(map.Grid, .5f, .5f), true);
            em.EventBus.RaiseLocalEvent(tool, use);
            Assert.That(em.System<KiasServiceSystem>().IsTesting(map.Grid), Is.True);
        });
        await pair.RunTicksSync(430);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.System<KiasServiceSystem>().IsTesting(map.Grid), Is.False);
            Assert.That(em.GetComponent<KiasDeviceAdapterComponent>(adapter).State, Is.False,
                "Diagnostic work must never reach the live actuator.");
        });
        await pair.CleanReturnAsync();
    }
}
