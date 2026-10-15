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
public sealed class KiasControllerEpochTests
{
    [Test]
    public async Task ReinsertedCardDoesNotConsumeAnEventFromBeforeItsColdBoot()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap(); var em = pair.Server.ResolveDependency<IEntityManager>();
        EntityUid rack = default, card = default;
        var runtime = em.System<KiasControllerRuntimeSystem>();
        await pair.Server.WaitAssertion(() =>
        {
            EntityUid Spawn(string prototype)
            {
                var uid = em.SpawnEntity(prototype, new EntityCoordinates(map.Grid, .5f, .5f));
                em.RemoveComponent<ApcPowerReceiverComponent>(uid); return uid;
            }
            Spawn("KiasCore"); Spawn("KiasDataCable"); rack = Spawn("KiasControllerRack"); card = Spawn("KiasProgrammableController");
            var program = new KiasControllerProgram();
            program.Nodes.Add(new() { Id = 1, Kind = KiasNodeKind.Any, Profile = "Automation" });
            program.Nodes.Add(new() { Id = 2, Kind = KiasNodeKind.Counter });
            program.Wires.Add(new() { FromNode = 1, FromPort = "Manual", ToNode = 2, ToPort = "Increment" });
            em.GetComponent<KiasControllerCardComponent>(card).Program = program;
            em.System<KiasSystem>().Rebuild(map.Grid);
            Assert.That(em.System<ItemSlotsSystem>().TryInsert(rack, KiasControllerRackComponent.SlotId(0), card, null), Is.True);
        });
        await pair.RunTicksSync(40);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(runtime.Running(card), Is.True);
            em.System<KiasProtocolSystem>().Trigger(map.Grid, KiasTrigger.Manual);
            var slots = em.System<ItemSlotsSystem>();
            Assert.That(slots.TryEject(rack, KiasControllerRackComponent.SlotId(0), null, out _), Is.True);
            Assert.That(slots.TryInsert(rack, KiasControllerRackComponent.SlotId(0), card, null), Is.True);
        });
        await pair.RunTicksSync(8);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(runtime.Running(card), Is.True);
            Assert.That(runtime.LastValue(card, 2, "Value").Number, Is.Zero);
            em.System<KiasProtocolSystem>().Trigger(map.Grid, KiasTrigger.Manual);
        });
        await pair.RunTicksSync(8);
        await pair.Server.WaitAssertion(() => Assert.That(runtime.LastValue(card, 2, "Value").Number, Is.EqualTo(1)));
        await pair.CleanReturnAsync();
    }
}
