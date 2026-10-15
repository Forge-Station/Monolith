#pragma warning disable RA0002
using Content.Server._Forge.KIAS;
using Content.Server._Forge.KIAS.Controllers;
using Content.Server.Power.Components;
using Content.Shared._Forge.KIAS;
using Content.Shared._Forge.KIAS.Controllers;
using Content.Shared.Containers.ItemSlots;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._Forge.KIAS;

[TestFixture]
public sealed class KiasControllerFeedbackTests
{
    [Test]
    public async Task DeviceFeedbackFaultsItsCardWithoutStoppingAnIndependentCard()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap(); var em = pair.Server.ResolveDependency<IEntityManager>();
        EntityUid loop = default, healthy = default;
        var runtime = em.System<KiasControllerRuntimeSystem>();
        await pair.Server.WaitAssertion(() =>
        {
            EntityUid Spawn(string prototype)
            {
                var uid = em.SpawnEntity(prototype, new EntityCoordinates(map.Grid, .5f, .5f));
                em.RemoveComponent<ApcPowerReceiverComponent>(uid); return uid;
            }
            Spawn("KiasCore"); Spawn("KiasDataCable"); var rack = Spawn("KiasControllerRack");
            loop = Spawn("KiasProgrammableController"); healthy = Spawn("KiasProgrammableController");
            KiasControllerProgram Program(bool feedback)
            {
                var program = new KiasControllerProgram();
                program.Nodes.Add(new() { Id = 1, Kind = KiasNodeKind.Any, Profile = "Automation" });
                program.Nodes.Add(new() { Id = 2, Kind = feedback ? KiasNodeKind.All : KiasNodeKind.Counter, Profile = feedback ? "Automation" : "" });
                program.Wires.Add(new() { FromNode = 1, FromPort = "Manual", ToNode = 2, ToPort = feedback ? "RunManual" : "Increment" });
                return program;
            }
            em.GetComponent<KiasControllerCardComponent>(loop).Program = Program(true);
            em.GetComponent<KiasControllerCardComponent>(healthy).Program = Program(false);
            em.System<KiasSystem>().Rebuild(map.Grid);
            Assert.That(em.System<ItemSlotsSystem>().TryInsert(rack, KiasControllerRackComponent.SlotId(0), loop, null), Is.True);
            Assert.That(em.System<ItemSlotsSystem>().TryInsert(rack, KiasControllerRackComponent.SlotId(1), healthy, null), Is.True);
        });
        await pair.RunTicksSync(40);
        await pair.Server.WaitAssertion(() => em.System<KiasProtocolSystem>().Trigger(map.Grid, KiasTrigger.Manual));
        await pair.RunTicksSync(80);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(runtime.Running(loop), Is.False);
            Assert.That(runtime.Fault(loop), Is.EqualTo("external-feedback-budget"));
            Assert.That(runtime.Running(healthy), Is.True);
            Assert.That(runtime.LastValue(healthy, 2, "Value").Number, Is.GreaterThan(1).And.LessThanOrEqualTo(33));
        });
        await pair.CleanReturnAsync();
    }
}
