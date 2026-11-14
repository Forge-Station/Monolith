using System.Collections.Generic;
using Content.Shared._Mono.PersonalShield;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.SoftShield;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Body.Part;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Inventory;
using Content.Shared.Item.ItemToggle;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.SoftShield;

/// <summary>Checks the soft-shield tacsuits let brute through dampened, leave burns to the armour and stop bleeding.</summary>
[TestFixture]
public sealed class WFSoftShieldTest
{
    private static readonly string[] Suits = ["ClothingOuterHardsuitAshenElite", "ClothingOuterHardsuitNfsdExperimental"];

    [Test]
    [TestCaseSource(nameof(Suits))]
    public async Task SoftShieldDampensAndStopsBleeding(string suitId)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var maps = entities.System<SharedMapSystem>();
        var inventory = entities.System<InventorySystem>();
        var toggle = entities.System<ItemToggleSystem>();
        var damageable = entities.System<DamageableSystem>();
        var bleedGate = entities.System<WolfmedBleedGateSystem>();

        EntityUid human = default;
        EntityUid suit = default;
        await server.WaitAssertion(() =>
        {
            maps.CreateMap(out var mapId);
            var coords = new MapCoordinates(0, 0, mapId);
            human = entities.SpawnEntity("MobHuman", coords);
            suit = entities.SpawnEntity(suitId, coords);

            Assert.That(entities.HasComponent<WFSoftShieldComponent>(suit), Is.True);
            Assert.That(inventory.TryEquip(human, suit, "outerClothing", silent: true, force: true), Is.True);
            Assert.That(toggle.TryActivate(suit, human, predicted: false), Is.True);
        });

        await server.WaitRunTicks(300);

        await server.WaitAssertion(() =>
        {
            var shield = entities.GetComponent<PersonalShieldComponent>(suit);
            Assert.That(shield.IsUp, Is.True, "The field should be up after spin-up.");
            Assert.That(bleedGate.CanStartBleeding(human), Is.False, "An active soft shield refuses bleeds.");

            var charge = shield.Runtime.Charge;
            var hit = new DamageSpecifier();
            hit.DamageDict["Piercing"] = FixedPoint2.New(20);
            var applied = damageable.TryChangeDamage(human, hit, origin: human);

            Assert.That(applied, Is.Not.Null, "A soft shield lets the hit through.");
            Assert.That(applied!.DamageDict.GetValueOrDefault("Piercing"), Is.GreaterThan(FixedPoint2.Zero));
            Assert.That(applied.DamageDict.GetValueOrDefault("Piercing"), Is.LessThanOrEqualTo(FixedPoint2.New(8)),
                "At most 40% of the round should reach the wearer.");
            Assert.That(shield.Runtime.Charge, Is.EqualTo(charge - 12f).Within(0.5f), "The field soaks 60% of the round.");

            var burnCharge = shield.Runtime.Charge;
            var burn = new DamageSpecifier();
            burn.DamageDict["Heat"] = FixedPoint2.New(20);
            damageable.TryChangeDamage(human, burn, origin: human);
            Assert.That(shield.Runtime.Charge, Is.EqualTo(burnCharge).Within(0.01f), "Burns pass the field untouched.");

            var query = entities.EntityQueryEnumerator<WoundBleedingComponent, WoundComponent>();
            while (query.MoveNext(out _, out _, out var wound))
            {
                Assert.That(entities.GetComponent<BodyPartComponent>(wound.HoldingPart).Body, Is.Not.EqualTo(human),
                    "No bleed opens under an active soft shield.");
            }

            shield.Runtime.Charge = 2f;
            var last = new DamageSpecifier();
            last.DamageDict["Piercing"] = FixedPoint2.New(20);
            var through = damageable.TryChangeDamage(human, last, origin: human);
            // Full soak lets 8 of 20 through; a 2-charge field lets 18 through, both before the suit's armour.
            Assert.That(through!.DamageDict.GetValueOrDefault("Piercing"),
                Is.GreaterThan(applied.DamageDict.GetValueOrDefault("Piercing") * 2f),
                "A nearly empty field soaks only the charge it has left.");
            Assert.That(shield.IsUp, Is.False, "The field breaks when its charge runs out.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SoftShieldStopsArterialCuts()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var maps = entities.System<SharedMapSystem>();
        var inventory = entities.System<InventorySystem>();
        var toggle = entities.System<ItemToggleSystem>();
        var damageable = entities.System<DamageableSystem>();

        EntityUid human = default;
        await server.WaitAssertion(() =>
        {
            maps.CreateMap(out var mapId);
            var coords = new MapCoordinates(0, 0, mapId);
            human = entities.SpawnEntity("MobHuman", coords);
            var suit = entities.SpawnEntity("ClothingOuterHardsuitAshenElite", coords);
            Assert.That(inventory.TryEquip(human, suit, "outerClothing", silent: true, force: true), Is.True);
            Assert.That(toggle.TryActivate(suit, human, predicted: false), Is.True);
        });

        await server.WaitRunTicks(300);

        await server.WaitAssertion(() =>
        {
            // Well past the slash artery rule's 22 even after the field and the suit take their share.
            var hit = new DamageSpecifier();
            hit.DamageDict["Slash"] = FixedPoint2.New(150);
            damageable.TryChangeDamage(human, hit, origin: human,
                targetPart: Content.Shared._Shitmed.Targeting.TargetBodyPart.LeftArm);

            var query = entities.EntityQueryEnumerator<WoundComponent>();
            while (query.MoveNext(out _, out var wound))
            {
                if (entities.GetComponent<BodyPartComponent>(wound.HoldingPart).Body != human)
                    continue;
                Assert.That(wound.Prototype.ToString(), Is.Not.EqualTo("WFWolfmedArterialBleedWound"),
                    "An active soft shield must not let a hit cut an artery.");
            }
        });

        await pair.CleanReturnAsync();
    }
}
