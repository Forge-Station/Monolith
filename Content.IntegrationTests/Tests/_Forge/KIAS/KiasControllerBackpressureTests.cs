using System.Collections.Generic;
using Content.Server._Forge.KIAS;
using Content.Server._Forge.KIAS.Controllers;
using Content.Server.Power.Components;
using Content.Shared._Forge.KIAS.Controllers;
using Content.Shared.Containers.ItemSlots;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._Forge.KIAS;

[TestFixture]
public sealed class KiasControllerBackpressureTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task SubscriberBurstDrainsWithoutFaultsOrLostPulses(bool topologyPending)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        var runtime = em.System<KiasControllerRuntimeSystem>();
        var kias = em.System<KiasSystem>();
        var cards = new List<EntityUid>();
        EntityUid core = default;
        long consumedBefore = 0, rejectedBefore = 0;
        await pair.Server.WaitAssertion(() =>
        {
            EntityUid Spawn(string prototype)
            {
                var uid = em.SpawnEntity(prototype, new EntityCoordinates(map.Grid, .5f, .5f));
                em.RemoveComponent<ApcPowerReceiverComponent>(uid);
                return uid;
            }
            core = Spawn("KiasCore");
            Spawn("KiasDataCable");
            var rack = Spawn("KiasControllerRack");
            kias.Rebuild(map.Grid);
            for (var slot = 0; slot < 8; slot++)
            {
                var card = Spawn("KiasProgrammableController");
                var program = new KiasControllerProgram();
                program.Nodes.Add(new() { Id = 33, Kind = KiasNodeKind.Counter });
                for (var node = 1; node <= 32; node++)
                {
                    program.Nodes.Add(new() { Id = node, Kind = KiasNodeKind.Any, Profile = "Automation" });
                    program.Wires.Add(new() { FromNode = node, FromPort = "Manual", ToNode = 33, ToPort = "Increment" });
                }
                em.GetComponent<KiasControllerCardComponent>(card).Program = program;
                Assert.That(em.System<ItemSlotsSystem>().TryInsert(rack, KiasControllerRackComponent.SlotId(slot), card, null), Is.True);
                cards.Add(card);
            }
            kias.MeasureUpdates = true;
        });
        await pair.RunTicksSync(40);
        await pair.Server.WaitAssertion(() =>
        {
            foreach (var card in cards) Assert.That(runtime.Running(card), Is.True, runtime.Fault(card));
            consumedBefore = runtime.WorkQueueMetrics.Consumed;
            rejectedBefore = runtime.WorkQueueMetrics.Rejected;
            for (var pulse = 0; pulse < 32; pulse++)
                em.System<KiasControllerIoSystem>().Emit(core, "Automation", "Manual", KiasGraphValue.Pulse);
            runtime.Update(0);
            Assert.That(runtime.PendingEventDispatchCount, Is.GreaterThan(0));
            Assert.That(runtime.WorkQueueMetrics.HighWater, Is.EqualTo(4096));
            if (topologyPending)
            {
                var pending = runtime.PendingEventDispatchCount;
                kias.Invalidate(map.Grid);
                runtime.Update(0);
                Assert.That(runtime.PendingEventDispatchCount, Is.EqualTo(pending));
                kias.Rebuild(map.Grid);
            }
        });
        await pair.RunTicksSync(80);
        await pair.Server.WaitAssertion(() =>
        {
            foreach (var card in cards)
            {
                Assert.That(runtime.Running(card), Is.True, runtime.Fault(card));
                Assert.That(runtime.LastValue(card, 33, "Value").Number, Is.EqualTo(1024));
            }
            Assert.That(runtime.PendingEventDispatchCount, Is.Zero);
            Assert.That(runtime.WorkQueueMetrics.Rejected - rejectedBefore, Is.Zero);
            Assert.That(runtime.WorkQueueMetrics.Consumed - consumedBefore, Is.EqualTo(8192));
            kias.MeasureUpdates = false;
        });
        await pair.CleanReturnAsync();
    }
}
