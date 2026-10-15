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
public sealed class KiasControllerSelectorTests
{
    [Test]
    public async Task AnyChoosesFirstOnlineAndFailsOverAndAllTogglesEachMatchExactlyOnce()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap(); var em = pair.Server.ResolveDependency<IEntityManager>();
        EntityUid rack = default, card = default;
        var targets = new EntityUid[3]; var runtime = em.System<KiasControllerRuntimeSystem>();
        await pair.Server.WaitAssertion(() =>
        {
            EntityUid Spawn(string prototype)
            {
                var uid = em.SpawnEntity(prototype, new EntityCoordinates(map.Grid, .5f, .5f));
                em.RemoveComponent<ApcPowerReceiverComponent>(uid); return uid;
            }
            Spawn("KiasCore"); Spawn("KiasDataCable"); rack = Spawn("KiasControllerRack");
            for (var i = 0; i < targets.Length; i++) targets[i] = Spawn("KiasDeviceAdapter");
            card = Spawn("KiasProgrammableController");
            var program = new KiasControllerProgram();
            program.Nodes.Add(new() { Id = 1, Kind = KiasNodeKind.Any, Profile = "Automation" });
            program.Nodes.Add(new() { Id = 2, Kind = KiasNodeKind.Any, Profile = "DeviceAdapter" });
            program.Wires.Add(new() { FromNode = 1, FromPort = "Manual", ToNode = 2, ToPort = "Toggle" });
            em.GetComponent<KiasControllerCardComponent>(card).Program = program;
            em.System<KiasSystem>().Rebuild(map.Grid);
            Assert.That(em.System<ItemSlotsSystem>().TryInsert(rack, KiasControllerRackComponent.SlotId(0), card, null), Is.True);
        });
        await pair.RunTicksSync(40);
        await pair.Server.WaitAssertion(() => em.System<KiasProtocolSystem>().Trigger(map.Grid, KiasTrigger.Manual));
        await pair.RunTicksSync(8);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<KiasDeviceAdapterComponent>(targets[0]).State, Is.True);
            Assert.That(em.GetComponent<KiasDeviceAdapterComponent>(targets[1]).State, Is.False);
            Assert.That(em.GetComponent<KiasDeviceAdapterComponent>(targets[2]).State, Is.False);
            em.DeleteEntity(targets[0]); em.System<KiasSystem>().Rebuild(map.Grid);
            em.System<KiasProtocolSystem>().Trigger(map.Grid, KiasTrigger.Manual);
        });
        await pair.RunTicksSync(8);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<KiasDeviceAdapterComponent>(targets[1]).State, Is.True);
            Assert.That(em.GetComponent<KiasDeviceAdapterComponent>(targets[2]).State, Is.False);
            var component = em.GetComponent<KiasControllerCardComponent>(card);
            component.Program.Nodes[1].Kind = KiasNodeKind.All; component.Revision++;
            runtime.Reconcile(rack);
        });
        await pair.RunTicksSync(8);
        await pair.Server.WaitAssertion(() => em.System<KiasProtocolSystem>().Trigger(map.Grid, KiasTrigger.Manual));
        await pair.RunTicksSync(8);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<KiasDeviceAdapterComponent>(targets[1]).State, Is.False);
            Assert.That(em.GetComponent<KiasDeviceAdapterComponent>(targets[2]).State, Is.True);
            Assert.That(runtime.LastValue(card, 2, "$MatchedCount").Number, Is.EqualTo(2));
        });
        await pair.CleanReturnAsync();
    }
}
