using Content.Shared._WF.ArmorDeflection;
using Content.Shared.Damage;
using Content.Shared.Inventory;
using Content.Shared.Projectiles;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.ArmorDeflection;

/// <summary>Checks light rounds ricochet off the Juggernaut while rifles and armour-piercing rounds get through.</summary>
[TestFixture]
public sealed class WFArmorDeflectTest
{
    private const string Suit = "ClothingOuterHardsuitJuggernaut";

    private static readonly string[] Bounces = ["Bullet9x19mmFMJ", "Bullet9x19mmHP", "Bullet9x19mmRIP", "Bullet45_ACPFMJ"];
    private static readonly string[] Penetrates = ["Bullet9x19mmPlasteelAP", "Bullet762x39mmFMJ", "Bullet762x51mmFMJ", "Bullet127x99mm"];

    [Test]
    public async Task LightRoundsBounceAndHeavyOnesDoNot()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var factory = server.ResolveDependency<IComponentFactory>();

        await server.WaitAssertion(() =>
        {
            Assert.That(prototypes.Index<EntityPrototype>(Suit).TryGetComponent<WFArmorDeflectComponent>(out var armor, factory), Is.True);
            foreach (var id in Bounces)
            {
                Assert.That(prototypes.Index<EntityPrototype>(id).TryGetComponent<ProjectileComponent>(out var round, factory), Is.True, id);
                Assert.That(WFArmorDeflectSystem.IsLightRound(armor!, round!), Is.True, $"{id} should ricochet.");
            }
            foreach (var id in Penetrates)
            {
                Assert.That(prototypes.Index<EntityPrototype>(id).TryGetComponent<ProjectileComponent>(out var round, factory), Is.True, id);
                Assert.That(WFArmorDeflectSystem.IsLightRound(armor!, round!), Is.False, $"{id} should get through.");
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task WearerRicochetsALightRound()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var maps = entities.System<SharedMapSystem>();
        var inventory = entities.System<InventorySystem>();

        await server.WaitAssertion(() =>
        {
            maps.CreateMap(out var mapId);
            var coords = new MapCoordinates(0, 0, mapId);
            var human = entities.SpawnEntity("MobHuman", coords);
            var suit = entities.SpawnEntity(Suit, coords);
            Assert.That(inventory.TryEquip(human, suit, "outerClothing", silent: true, force: true), Is.True);
            Assert.That(entities.HasComponent<WFArmorDeflectWearerComponent>(human), Is.True);

            var light = entities.SpawnEntity("Bullet9x19mmFMJ", new MapCoordinates(2, 0, mapId));
            var lightEv = new ProjectileReflectAttemptEvent(light, entities.GetComponent<ProjectileComponent>(light), false);
            entities.EventBus.RaiseLocalEvent(human, ref lightEv);
            Assert.That(lightEv.Cancelled, Is.True, "A 9mm round ricochets off the plate.");

            var heavy = entities.SpawnEntity("Bullet762x51mmFMJ", new MapCoordinates(2, 0, mapId));
            var heavyEv = new ProjectileReflectAttemptEvent(heavy, entities.GetComponent<ProjectileComponent>(heavy), false);
            entities.EventBus.RaiseLocalEvent(human, ref heavyEv);
            Assert.That(heavyEv.Cancelled, Is.False, "A full-power rifle round gets through.");

            Assert.That(entities.GetComponent<DamageableComponent>(human).TotalDamage.Float(), Is.Zero);

            Assert.That(inventory.TryUnequip(human, "outerClothing", silent: true, force: true), Is.True);
            Assert.That(entities.HasComponent<WFArmorDeflectWearerComponent>(human), Is.False);
        });

        await pair.CleanReturnAsync();
    }
}
