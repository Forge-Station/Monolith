#pragma warning disable RA0002
using System.Collections.Generic;
using Content.Server._Forge.KIAS;
using Content.Server._Forge.KIAS.Controllers;
using Content.Server.Power.Components;
using Content.Shared._Forge.KIAS;
using Content.Shared._Forge.KIAS.Controllers;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Power.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._Forge.KIAS;

[TestFixture]
public sealed class KiasControllerPhysicalTests
{
    [Test]
    public async Task EightPhysicalSlotsDynamicPowerAndDirtyDraftEjectProtection()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var position = new EntityCoordinates(map.Grid, 0.5f, 0.5f);
            em.SpawnEntity("KiasDataCable", position);
            var core = em.SpawnEntity("KiasCore", position);
            em.RemoveComponent<ApcPowerReceiverComponent>(core);
            var rack = em.SpawnEntity("KiasControllerRack", position);
            var programmer = em.SpawnEntity("KiasControllerProgrammer", position);
            var actor = em.SpawnEntity("MobHuman", position);
            var power = em.System<SharedPowerReceiverSystem>();
            power.SetNeedsPower(rack, false);
            power.SetNeedsPower(programmer, false);
            em.System<KiasSystem>().Rebuild(map.Grid);
            var slots = em.System<ItemSlotsSystem>();
            var physical = em.System<KiasControllerPhysicalSystem>();
            var rackComp = em.GetComponent<KiasControllerRackComponent>(rack);
            var load = em.GetComponent<ApcPowerReceiverComponent>(rack);
            Assert.That(em.GetComponent<ItemSlotsComponent>(rack).Slots.Count, Is.EqualTo(8));
            Assert.That(load.Load, Is.EqualTo(rackComp.BasePowerLoad));
            var cards = new List<EntityUid>();
            for (var i = 0; i < 8; i++)
            {
                var card = em.SpawnEntity("KiasProgrammableController", position);
                cards.Add(card);
                Assert.That(slots.TryInsert(rack, KiasControllerRackComponent.SlotId(i), card, actor), Is.True);
                Assert.That(load.Load, Is.EqualTo(rackComp.BasePowerLoad + (i + 1) * rackComp.PerControllerLoad));
            }
            var ninth = em.SpawnEntity("KiasProgrammableController", position);
            Assert.That(slots.TryInsertEmpty((rack, em.GetComponent<ItemSlotsComponent>(rack)), ninth, null), Is.False);
            Assert.That(physical.Inserted(rack), Is.EqualTo(8));
            Assert.That(slots.TryEject(rack, KiasControllerRackComponent.SlotId(0), actor, out var ejected), Is.True);
            Assert.That(ejected, Is.EqualTo(cards[0]));
            Assert.That(load.Load, Is.EqualTo(rackComp.BasePowerLoad + 7 * rackComp.PerControllerLoad));
            em.EnsureComponent<KiasClaimComponent>(map.Grid).Owner = new Robust.Shared.Network.NetUserId(Guid.NewGuid());
            Assert.That(slots.TryInsert(rack, KiasControllerRackComponent.SlotId(0), ninth, actor), Is.False);
            Assert.That(slots.TryEject(rack, KiasControllerRackComponent.SlotId(1), actor, out _), Is.False);
            em.RemoveComponent<KiasClaimComponent>(map.Grid);
            Assert.That(slots.TryInsert(programmer, KiasControllerProgrammerComponent.SlotId, ninth, actor), Is.True);
            var draft = em.GetComponent<KiasControllerProgrammerComponent>(programmer);
            draft.Draft!.Name = "Changed draft";
            draft.DraftDirty = true;
            Assert.That(em.GetComponent<KiasControllerCardComponent>(ninth).Program.Name, Is.EqualTo("Controller"));
            Assert.That(slots.TryEject(programmer, KiasControllerProgrammerComponent.SlotId, actor, out _), Is.False);
            draft.DraftDirty = false;
            Assert.That(slots.TryEject(programmer, KiasControllerProgrammerComponent.SlotId, actor, out _), Is.True);
            Assert.That(draft.Draft, Is.Null);
        });
        await pair.CleanReturnAsync();
    }
}
