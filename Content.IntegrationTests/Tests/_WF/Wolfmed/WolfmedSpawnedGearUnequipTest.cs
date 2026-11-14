using Content.Client.Inventory;
using Content.Shared.Clothing.Components;
using Content.Shared.Inventory;
using Robust.Client.Player;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// A client that first sees a mob already wearing its gear never receives the equip for those items, so their
/// ClothingComponent.InSlot stays null client side. Taking a layer-hiding item off then tripped
/// HideLayerClothingSystem's slot assert and closed the Debug client (playtest 4: spawnoutfit, then a slot click).
/// </summary>
[TestFixture]
public sealed class WolfmedSpawnedGearUnequipTest
{
    [Test]
    public async Task ClientCanUnequipTheHelmetItArrivedWearingTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;
        var sEntMan = server.EntMan;
        var inventory = sEntMan.System<InventorySystem>();
        var map = await pair.CreateTestMap();

        EntityUid mob = default;
        EntityUid helmet = default;
        await server.WaitPost(() =>
        {
            // Same tick as spawnoutfit: the mob, its gear and the player attach all reach the client together.
            mob = sEntMan.SpawnEntity("MobHuman", map.GridCoords);
            helmet = sEntMan.SpawnEntity("ClothingHeadHelmetHardsuitBasic", map.GridCoords);
            Assert.That(inventory.TryEquip(mob, helmet, "head", silent: true, force: true), Is.True);
            server.PlayerMan.SetAttachedEntity(pair.Player!, mob);
        });

        await pair.RunTicksSync(15);

        var cMob = pair.ToClientUid(mob);
        var cHelmet = pair.ToClientUid(helmet);
        await client.WaitPost(() =>
        {
            Assert.That(client.ResolveDependency<IPlayerManager>().LocalEntity, Is.EqualTo(cMob));
            var clothing = client.EntMan.GetComponent<ClothingComponent>(cHelmet);
            TestContext.Out.WriteLine($"client-side InSlot before the unequip: {clothing.InSlot ?? "null"}");

            // The slot click: a predicted unequip that ran ClothingGotUnequippedEvent on the client.
            client.EntMan.System<ClientInventorySystem>().UIInventoryActivate("head");
        });

        await pair.RunTicksSync(15);

        await server.WaitPost(() =>
        {
            Assert.That(inventory.TryGetSlotEntity(mob, "head", out _), Is.False, "the server did not take the helmet off");
        });

        await pair.CleanReturnAsync();
    }
}
