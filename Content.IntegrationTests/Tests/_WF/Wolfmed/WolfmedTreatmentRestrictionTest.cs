using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server.Medical.Components; // WOLFGATE: HealingComponent is server-only here.
using Content.Shared._Onyx.Wounds;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// W0: which topical treats which injury, and how little of a wound plain damage removal closes.
/// </summary>
/// <remarks>
/// The restriction is one data field, <c>HealingComponent.TreatedDamageTypes</c>, read in exactly one place
/// (<c>WoundHealingSystem.GetTreatableDamage</c>) and applied to every wound-host path. A type outside the
/// set is dropped from the item's spec, so the item resolves no part, heals nothing and the player gets the
/// <c>wolfmed-item-cant-treat-part</c> popup instead of a silent no-op.
/// </remarks>
[TestFixture]
[TestOf(typeof(WoundHealingSystem))]
public sealed class WolfmedTreatmentRestrictionTest : GameTest
{
    /// <summary>A bruise pack works on bruises: it neither closes a cut nor slows the cut's bleeding.</summary>
    [Test]
    public async Task BruisePackTreatsBluntOnlyTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var routing = entities.System<WoundDamageRoutingSystem>();
            var healing = entities.System<WoundHealingSystem>();
            var bleeding = entities.System<WoundBleedingSystem>();
            var wounds = entities.System<WoundSystem>();

            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var cutArm = Part(entities, body, BodyPartSymmetry.Left);
            var bruisedArm = Part(entities, body, BodyPartSymmetry.Right);
            var pack = entities.SpawnEntity("Brutepack", map.GridCoords);
            var packHealing = entities.GetComponent<HealingComponent>(pack);

            // The whole restriction, as data.
            Assert.That(packHealing.TreatedDamageTypes, Is.EquivalentTo(new ProtoId<DamageTypePrototype>[] { "Blunt" }));
            Assert.That(healing.GetTreatableDamage(packHealing).DamageDict.Keys,
                Is.EquivalentTo(new ProtoId<DamageTypePrototype>[] { "Blunt" }),
                "the pack's `Brute` group carries Slash and Piercing too; both must be dropped before any " +
                "wound path sees the spec.");

            // 10 stays under WFWolfmedFractureProfile's Hairline threshold (12), so no stray bone fracture.
            Assert.That(routing.TryApplyPartDamage(body, cutArm, Spec("Slash", 10), null, ignoreResistances: true));
            Assert.That(routing.TryApplyPartDamage(body, bruisedArm, Spec("Blunt", 10), null, ignoreResistances: true));
            var cut = FindWound(entities, wounds, cutArm, "SlashWound");
            var bruise = FindWound(entities, wounds, bruisedArm, "BluntWound");
            var bleedBefore = bleeding.GetPartRate(cutArm);
            Assert.That(bleedBefore, Is.GreaterThan(0f), "SlashWound bleeds from severity 9 up; 10 must bleed.");

            Assert.That(healing.TryApplyHealing(body, cutArm, (pack, packHealing), null, out _, out var stopped),
                Is.False, "a bruise pack has nothing to give a cut.");
            Assert.Multiple(() =>
            {
                Assert.That(stopped, Is.False);
                Assert.That(cut.Comp.Severity, Is.EqualTo(FixedPoint2.New(10)));
                Assert.That(bleeding.GetPartRate(cutArm), Is.EqualTo(bleedBefore),
                    "and it must not stop the bleeding by shrinking the wound behind the player's back.");
            });

            // The cell it is for: 10 Blunt of part damage removed, 15 % of that taken off the wound.
            Assert.That(healing.TryApplyHealing(body, bruisedArm, (pack, packHealing), null, out _, out _));
            Assert.That(bruise.Comp.Severity, Is.EqualTo(FixedPoint2.New(8.5)));
        });
    }

    /// <summary>Sutures are the mirror image: cuts and punctures and their bleeding, never bruises.</summary>
    [Test]
    public async Task SuturesTreatCutsAndBleedingNotBruisesTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var routing = entities.System<WoundDamageRoutingSystem>();
            var healing = entities.System<WoundHealingSystem>();
            var bleeding = entities.System<WoundBleedingSystem>();
            var wounds = entities.System<WoundSystem>();

            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var cutArm = Part(entities, body, BodyPartSymmetry.Left);
            var bruisedArm = Part(entities, body, BodyPartSymmetry.Right);
            var suture = entities.SpawnEntity("MedicatedSuture", map.GridCoords);
            var sutureHealing = entities.GetComponent<HealingComponent>(suture);

            Assert.That(healing.GetTreatableDamage(sutureHealing).DamageDict.Keys,
                Is.EquivalentTo(new ProtoId<DamageTypePrototype>[] { "Slash", "Piercing" }));

            // 11 rather than 10: `healingMultiplier: 0.15` leaves severity 9.35, still above SlashWound's
            // bleeding `minimumSeverity: 9`, so the bleed is there for the suture's bloodlossModifier to close
            // rather than having lapsed on its own.
            Assert.That(routing.TryApplyPartDamage(body, cutArm, Spec("Slash", 11), null, ignoreResistances: true));
            // M2: 7, under BluntWound's bleeding `minimumSeverity: 8`. At 10 a quarter of bruises rolled a bleed that
            // the suture then stopped, and the assertion below failed about one run in four.
            Assert.That(routing.TryApplyPartDamage(body, bruisedArm, Spec("Blunt", 7), null, ignoreResistances: true));
            var cut = FindWound(entities, wounds, cutArm, "SlashWound");
            var bruise = FindWound(entities, wounds, bruisedArm, "BluntWound");

            Assert.That(healing.TryApplyHealing(body, bruisedArm, (suture, sutureHealing), null, out _, out _),
                Is.False, "a suture is not a bruise pack.");
            Assert.That(bruise.Comp.Severity, Is.EqualTo(FixedPoint2.New(7)));

            Assert.That(healing.TryApplyHealing(body, cutArm, (suture, sutureHealing), null, out _, out var stopped));
            Assert.Multiple(() =>
            {
                Assert.That(cut.Comp.Severity, Is.EqualTo(FixedPoint2.New(9.35)));
                Assert.That(stopped, Is.True, "`bloodlossModifier: -10` is the half of a suture that closes a bleed.");
                Assert.That(bleeding.GetPartRate(cutArm), Is.EqualTo(0f));
            });
        });
    }

    /// <summary>
    /// The multipliers themselves, read off the prototypes: 0.15 on anything damage removal can reach, 0 on
    /// the wounds that exist only to bleed.
    /// </summary>
    [Test]
    public async Task HealingMultipliersMatchTheTreatmentModelTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var locale = server.ResolveDependency<ILocalizationManager>();

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                foreach (var id in new[]
                         {
                             "BluntWound", "SlashWound", "PiercingWound", "BurnWound", "ElectricalWound",
                             "SlimeBluntWound", "SlimeSlashWound", "SlimePiercingWound", "SlimeBurnWound",
                             "PlantBluntWound", "PlantSlashWound", "PlantPiercingWound", "PlantBurnWound",
                         })
                {
                    Assert.That(prototypes.Index<WoundPrototype>(id).HealingMultiplier,
                        Is.EqualTo(0.15f).Within(0.0001f), id);
                }

                foreach (var id in new[]
                         {
                             "SystemicBleedingWound", "InternalBleedingWound", "SurgicalIncisionWound",
                             "DismembermentWound",
                         })
                {
                    Assert.That(prototypes.Index<WoundPrototype>(id).HealingMultiplier,
                        Is.EqualTo(0f).Within(0.0001f), id);
                }

                // The one exemption: a welder closes a chassis wound by removing damage, and it is the only
                // thing that closes one at all, so the mechanical wounds keep the 1:1 rate.
                foreach (var id in new[] { "IpcMechanicalDamageWound", "CyberneticMechanicalDamageWound" })
                {
                    Assert.That(prototypes.Index<WoundPrototype>(id).HealingMultiplier,
                        Is.EqualTo(1f).Within(0.0001f), id);
                }

                // The refusal the player actually sees.
                Assert.That(locale.HasString("wolfmed-item-cant-treat-part"));
            });
        });
    }

    private static EntityUid Part(IEntityManager entities, EntityUid body, BodyPartSymmetry symmetry)
    {
        return entities.System<SharedBodySystem>().GetBodyChildren(body)
            .Single(part => part.Component.PartType == BodyPartType.Arm && part.Component.Symmetry == symmetry)
            .Id;
    }

    private static Entity<WoundComponent> FindWound(
        IEntityManager entities,
        WoundSystem wounds,
        EntityUid part,
        string prototype)
    {
        return wounds.GetWounds((part, entities.GetComponent<WoundableComponent>(part)))
            .Single(wound => wound.Comp.Prototype == new ProtoId<WoundPrototype>(prototype));
    }

    private static DamageSpecifier Spec(string type, int amount) => new()
    {
        DamageDict = { [new ProtoId<DamageTypePrototype>(type)] = FixedPoint2.New(amount) },
    };
}
