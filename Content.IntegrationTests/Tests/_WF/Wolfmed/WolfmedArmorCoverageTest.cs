#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.Armor;
using Content.Shared._WF.Wolfmed.Compat;
using Content.Shared.Armor;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Inventory;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// P6's locational-armour content pass: worn armour protects the parts it is worn on, and a full-body suit
/// still protects everything.
/// </summary>
/// <remarks>
/// P3-D5 made unset <c>coverage</c> mean "protects every part", which is why hardsuits, EVA and bio suits
/// need no annotation at all and must stay unannotated. This fixture is the guard that says so, because the
/// natural follow-up mistake is to "complete" the pass by giving them an explicit part list that goes stale
/// the moment a new <see cref="BodyPartType"/> appears. Helmets, masks, gloves and vests carry the explicit
/// sets instead.
/// </remarks>
[TestFixture]
[TestOf(typeof(WolfmedPartArmorSystem))]
public sealed class WolfmedArmorCoverageTest : GameTest
{
    /// <summary>Items whose armour must reach every part: suits that seal the whole body.</summary>
    private static readonly string[] FullBody =
    [
        "ClothingOuterHardsuitEngineering",
        "ClothingOuterHardsuitSecurity",
        "ClothingOuterBioGeneral", // the bio/rad suit set; plain EVA carries no ArmorComponent at all.
    ];

    /// <summary>Item id, and exactly the part types its armour may reach.</summary>
    private static readonly (string Id, BodyPartType[] Parts)[] Located =
    [
        ("ClothingHeadHelmetHardsuitEngineering", [BodyPartType.Head]),
        ("ClothingHeadHelmetSwat", [BodyPartType.Head]),
        ("ClothingHeadHelmetRiot", [BodyPartType.Head]),
        ("ClothingMaskGasSecurity", [BodyPartType.Head]),
        ("ClothingHandsGlovesHop", [BodyPartType.Hand]),
        ("ClothingOuterArmorBulletproof", [BodyPartType.Torso, BodyPartType.Arm, BodyPartType.Leg]),
    ];

    /// <summary>The shipped coverage data, read off spawned items so inherited armour counts too.</summary>
    [Test]
    public async Task ShippedCoverageIsWhatTheItemIsWornOnTest()
    {
        await Server.WaitIdleAsync();
        var entities = Server.EntMan;
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                foreach (var id in FullBody)
                {
                    var armor = Armor(entities, id, map.GridCoords);
                    Assert.That(armor.Coverage, Is.Null.Or.Empty,
                        $"{id} seals the whole body, so its coverage must stay unset (P3-D5: unset protects every part).");
                    Assert.That(armor.CoverageSymmetry, Is.Null.Or.Empty, $"{id} must not be one-sided.");
                    Assert.That(WolfmedPartArmorSystem.Covers(armor, BodyPartType.Foot, BodyPartSymmetry.Left),
                        $"{id} must still reach a foot it never names.");
                }

                foreach (var (id, parts) in Located)
                {
                    var armor = Armor(entities, id, map.GridCoords);
                    Assert.That(armor.Coverage, Is.Not.Null.And.Not.Empty, $"{id} needs an explicit coverage set.");
                    Assert.That(armor.Coverage!, Is.EquivalentTo(parts), $"{id} covers the wrong parts.");
                }
            });
        });
    }

    /// <summary>
    /// The data is live: a hardsuit armours a leg it never names, a helmet leaves the same leg alone.
    /// </summary>
    [Test]
    public async Task CoverageReachesRoutedPartDamageTest()
    {
        await Server.WaitIdleAsync();
        var entities = Server.EntMan;
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var suit = entities.SpawnEntity("ClothingOuterHardsuitEngineering", map.GridCoords);
            var helmet = entities.SpawnEntity("ClothingHeadHelmetRiot", map.GridCoords);
            var inventory = entities.System<InventorySystem>();
            var routing = entities.System<WoundDamageRoutingSystem>();
            var damage = entities.System<WolfmedDamageableSystem>();
            var graph = entities.System<SharedBodySystem>();

            var leg = graph.GetBodyChildren(body)
                .First(part => part.Component.PartType == BodyPartType.Leg).Id;

            var bare = Hit(routing, damage, body, leg);

            Assert.That(inventory.TryEquip(body, helmet, "head"), Is.True);
            var helmeted = Hit(routing, damage, body, leg);

            Assert.That(inventory.TryEquip(body, suit, "outerClothing"), Is.True);
            var suited = Hit(routing, damage, body, leg);

            Assert.Multiple(() =>
            {
                Assert.That(helmeted, Is.EqualTo(bare), "a head-covering helmet must not armour a leg.");
                Assert.That(suited, Is.LessThan(bare), "a hardsuit's unset coverage must still reach a leg.");
            });
        });
    }

    /// <summary>Damage the part actually kept from one routed 20-Blunt hit.</summary>
    private static FixedPoint2 Hit(
        WoundDamageRoutingSystem routing,
        WolfmedDamageableSystem damage,
        EntityUid body,
        EntityUid part)
    {
        var before = damage.GetAllDamage(part).GetTotal();
        routing.TryApplyPartDamage(body, part, Spec("Blunt", 20));
        return damage.GetAllDamage(part).GetTotal() - before;
    }

    private static ArmorComponent Armor(IEntityManager entities, string id, EntityCoordinates coordinates)
    {
        var item = entities.SpawnEntity(id, coordinates);
        Assert.That(entities.TryGetComponent(item, out ArmorComponent? armor), $"{id} has no ArmorComponent.");
        return armor!;
    }

    private static DamageSpecifier Spec(string type, int amount) => new()
    {
        DamageDict = { [new ProtoId<DamageTypePrototype>(type)] = FixedPoint2.New(amount) },
    };
}
