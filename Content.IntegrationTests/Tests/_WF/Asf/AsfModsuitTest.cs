using Content.Shared._Goobstation.Clothing.Components;
using Content.Shared._Goobstation.Clothing.Systems;
using Content.Shared._Mono.ArmorPlate;
using Content.Shared._NF.Clothing.Components;
using Content.Shared._Mono.PersonalShield;
using Content.Shared.Clothing.Components;
using Content.Shared.Clothing.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Item.ItemToggle.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Asf;

/// <summary>Checks the ASF field modsuits take plates and cover wings, and that the Aegis shield only comes up on a sealed suit.</summary>
[TestFixture]
public sealed class AsfModsuitTest
{
    private static readonly string[] Chestplates = ["WFAsfClothingModsuitChestplateField", "WFAsfClothingModsuitChestplateAegis"];

    [Test]
    public async Task ChestplatesTakePlatesCoverWingsAndKeepTheirSeal()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var factory = server.ResolveDependency<IComponentFactory>();

        await server.WaitAssertion(() =>
        {
            foreach (var id in Chestplates)
            {
                var proto = prototypes.Index<EntityPrototype>(id);
                Assert.That(proto.TryGetComponent<ArmorPlateHolderComponent>(out _, factory), Is.True, $"{id} should accept armour plates.");
                Assert.That(proto.TryGetComponent<HarpyHideWingsComponent>(out _, factory), Is.True, $"{id} should cover wings like a hardsuit.");
                // The seal's ComponentToggler listens to ItemToggle; a toggle here would strip pressure protection.
                Assert.That(proto.TryGetComponent<ItemToggleComponent>(out _, factory), Is.False, $"{id} must not carry an item toggle.");
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AegisShieldNeedsASealedSuit()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var maps = entities.System<SharedMapSystem>();
        var inventory = entities.System<InventorySystem>();
        var toggle = entities.System<ItemToggleSystem>();
        var sealing = entities.System<SharedSealableClothingSystem>();

        EntityUid human = default;
        EntityUid suit = default;
        await server.WaitAssertion(() =>
        {
            maps.CreateMap(out var mapId);
            var coords = new MapCoordinates(0, 0, mapId);
            human = entities.SpawnEntity("MobHuman", coords);
            suit = entities.SpawnEntity("WFAsfClothingModsuitAegisPowerCell", coords);

            Assert.That(entities.GetComponent<ToggleableClothingComponent>(suit).ClothingPrototypes, Has.Count.EqualTo(4));
            Assert.That(entities.GetComponent<PersonalShieldComponent>(suit).Shield.RequiredSlot, Is.EqualTo(SlotFlags.BACK));
            Assert.That(inventory.TryEquip(human, suit, "back", silent: true, force: true), Is.True);
            Assert.That(toggle.TryActivate(suit, human, predicted: false), Is.False, "The shield must stay down on an unsealed suit.");

            entities.EventBus.RaiseLocalEvent(suit, new ToggleClothingEvent { Performer = human });
            var control = entities.GetComponent<SealableClothingControlComponent>(suit);
            Assert.That(sealing.TryStartSealToggleProcess((suit, control), human), Is.True, "The deployed suit should start sealing.");
        });

        await server.WaitRunTicks(300);

        await server.WaitAssertion(() =>
        {
            Assert.That(entities.GetComponent<SealableClothingControlComponent>(suit).IsCurrentlySealed, Is.True);
            Assert.That(toggle.TryActivate(suit, human, predicted: false), Is.True, "The shield should switch on once the suit is sealed.");
        });

        await server.WaitRunTicks(150);

        await server.WaitAssertion(() =>
        {
            Assert.That(entities.GetComponent<PersonalShieldComponent>(suit).IsUp, Is.True, "The Aegis shield should be up after spinning up.");
            var control = entities.GetComponent<SealableClothingControlComponent>(suit);
            Assert.That(sealing.TryStartSealToggleProcess((suit, control), human), Is.True);
        });

        await server.WaitRunTicks(300);

        await server.WaitAssertion(() =>
        {
            Assert.That(entities.GetComponent<SealableClothingControlComponent>(suit).IsCurrentlySealed, Is.False);
            Assert.That(toggle.IsActivated(suit), Is.False, "Unsealing the suit should drop the shield.");
        });

        await pair.CleanReturnAsync();
    }
}
