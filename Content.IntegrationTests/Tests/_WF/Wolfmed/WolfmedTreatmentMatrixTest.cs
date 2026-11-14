using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server.Medical.Components; // WOLFGATE: HealingComponent is server-only here.
// WOLFGATE: WoundHealingSystem's FILE is under Content.Server/_Onyx/Wounds, but its namespace is the shared
// one - the same mismatch PLAN5 N2 records for CirculatoryStreamSystem.
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.Compat; // WOLFGATE: D12 damage facade.
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// Which tool treats which body. Phase 5 is the phase that makes <c>treatmentCapabilities</c> discriminate:
/// before it, every shipped profile was <c>[Biological]</c> and every overlap check trivially passed.
/// </summary>
/// <remarks>
/// <para>PLAN5 §6.2 T-P5-15 / -16 / -17 (WP13-6).</para>
/// <para>
/// The capability sets under test, all read off <c>Resources/Prototypes/_Onyx/Wounds/wounds.yml</c>:
/// <c>OrganicBodyPartProfile</c> and <c>PlantBodyPartProfile</c> and <c>SlimeBodyPartProfile</c> are
/// <c>[Biological]</c>; <c>IpcBodyPartProfile</c> and <c>CyberneticBodyPartProfile</c> are
/// <c>[Mechanical, Electrical]</c>. The gate itself is a plain set intersection —
/// <c>WoundDamageRoutingSystem.CanTreatPart</c> for the reagent/scope path and
/// <c>WoundHealingSystem.IsCompatiblePart</c> for the item path — so "no overlap" means the part is never
/// offered to the heal at all, not that the heal lands for zero.
/// </para>
/// </remarks>
[TestFixture]
[TestOf(typeof(WoundHealingSystem))]
public sealed class WolfmedTreatmentMatrixTest : GameTest
{
    /// <summary>
    /// PLAN5 §6.2 T-P5-15. The six cells: <c>{Biological, Mechanical, Electrical}</c> against an organic wound
    /// and an IPC chassis wound. Driven through
    /// <c>WoundDamageRoutingSystem.WithTreatmentCapabilities</c>, which is the same scope HOOK 9 opens for a
    /// reagent's <c>treatmentCapabilities</c> and HOOK 8 opens for an item's.
    /// </summary>
    [Test]
    public async Task TreatmentCapabilityMatrixTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var routing = entities.System<WoundDamageRoutingSystem>();
            var wounds = entities.System<WoundSystem>();

            var organic = entities.SpawnEntity("MobHuman", map.GridCoords);
            var organicArm = Part(entities, organic, BodyPartType.Arm, BodyPartSymmetry.Left);
            var mechanical = entities.SpawnEntity("MobIPC", map.GridCoords);
            var mechanicalArm = Part(entities, mechanical, BodyPartType.Arm, BodyPartSymmetry.Left);

            // Slash, because trap 3 forbids a Blunt-family wound in anything that has to be deterministic, and
            // because both profiles accept it and both wound sets carry a `severityMultiplier` of 1 for it -
            // so 30 Slash is severity 30 on either body and the two halves of the matrix are comparable.
            Assert.That(routing.TryApplyPartDamage(organic, organicArm, Spec("Slash", 30), null,
                ignoreResistances: true));
            Assert.That(routing.TryApplyPartDamage(mechanical, mechanicalArm, Spec("Slash", 30), null,
                ignoreResistances: true));

            var organicWound = FindWound(entities, wounds, organicArm, "SlashWound");
            var mechanicalWound = FindWound(entities, wounds, mechanicalArm, "IpcMechanicalDamageWound");
            Assert.Multiple(() =>
            {
                Assert.That(organicWound.Comp.Severity, Is.EqualTo(FixedPoint2.New(30)));
                Assert.That(mechanicalWound.Comp.Severity, Is.EqualTo(FixedPoint2.New(30)));
            });

            // Row 1 - Biological: heals the organic wound, refused on the chassis.
            Heal(routing, organic, organicArm, TreatmentCapability.Biological);
            Heal(routing, mechanical, mechanicalArm, TreatmentCapability.Biological);
            Assert.Multiple(() =>
            {
                // WOLFGATE (W0): SlashWound's `healingMultiplier: 0.15` makes a -5 heal 0.75 of wound
                // severity. The part damage below still moves by the full 5 - that separation is the point.
                Assert.That(organicWound.Comp.Severity, Is.EqualTo(FixedPoint2.New(29.25)),
                    "OrganicBodyPartProfile is [Biological]: gauze, ointment and every medicine still work.");
                Assert.That(mechanicalWound.Comp.Severity, Is.EqualTo(FixedPoint2.New(30)),
                    "IpcBodyPartProfile is [Mechanical, Electrical]: no medicine, brute pack, ointment or " +
                    "gauze touches a chassis.");
            });

            // Row 2 - Mechanical: the mirror image.
            Heal(routing, organic, organicArm, TreatmentCapability.Mechanical);
            Heal(routing, mechanical, mechanicalArm, TreatmentCapability.Mechanical);
            Assert.Multiple(() =>
            {
                Assert.That(organicWound.Comp.Severity, Is.EqualTo(FixedPoint2.New(29.25)),
                    "a welder must not close a flesh wound.");
                // W0 exempts IpcMechanicalDamageWound from the 0.15 nerf: repair is repair, and the welder is
                // the only thing that closes a chassis wound at all.
                Assert.That(mechanicalWound.Comp.Severity, Is.EqualTo(FixedPoint2.New(25)));
            });

            // Row 3 - Electrical: the cable coil's set (PROTO U). It overlaps BOTH mechanical profiles and
            // neither biological one, which is exactly the trade P5-D12 makes.
            Heal(routing, organic, organicArm, TreatmentCapability.Electrical);
            Heal(routing, mechanical, mechanicalArm, TreatmentCapability.Electrical);
            Assert.Multiple(() =>
            {
                Assert.That(organicWound.Comp.Severity, Is.EqualTo(FixedPoint2.New(29.25)));
                Assert.That(mechanicalWound.Comp.Severity, Is.EqualTo(FixedPoint2.New(20)));

                // A refused cell must leave the flat part damage alone too, not merely the wound: CanTreatPart
                // refuses the PART, so ApplyLocalizedHealing has nothing to distribute the heal across.
                Assert.That(entities.System<WolfmedDamageableSystem>().GetAllDamage(organicArm)
                        .DamageDict.GetValueOrDefault(new ProtoId<DamageTypePrototype>("Slash")),
                    Is.EqualTo(FixedPoint2.New(25)));
                Assert.That(entities.System<WolfmedDamageableSystem>().GetAllDamage(mechanicalArm)
                        .DamageDict.GetValueOrDefault(new ProtoId<DamageTypePrototype>("Slash")),
                    Is.EqualTo(FixedPoint2.New(20)));
            });
        });
    }

    /// <summary>
    /// PLAN5 §6.2 T-P5-16 (U5 shipped as PROTO U). The nerf and its compensating gain in one test: the cable
    /// coil stops healing organics and starts being a real repair tool for chassis and prosthetics.
    /// </summary>
    /// <remarks>
    /// The leak this closes was live: HOOK 8 skips <c>HealingSystem</c>'s <c>damageContainers</c> check for
    /// wound hosts and <c>WoundHealingSystem.IsCompatiblePart</c> never reads that list, so
    /// <c>TreatmentCapabilities</c> — defaulting to <c>[Biological]</c> — was the only surviving filter and it
    /// overlapped <c>OrganicBodyPartProfile</c>. A coil healed human Heat and Shock at -3/-3 per 0.6 s.
    /// </remarks>
    [Test]
    public async Task CableCoilNoLongerHealsOrganicsButHealsMechanicalTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var healing = entities.System<WoundHealingSystem>();
            var routing = entities.System<WoundDamageRoutingSystem>();
            var wounds = entities.System<WoundSystem>();
            var coil = entities.SpawnEntity("CableApcStack", map.GridCoords);
            var coilHealing = entities.GetComponent<HealingComponent>(coil);

            Assert.That(coilHealing.TreatmentCapabilities,
                Is.EquivalentTo(new[] { TreatmentCapability.Electrical }),
                "PROTO U's one line. Without it HealingComponent defaults to [Biological].");

            var organic = entities.SpawnEntity("MobHuman", map.GridCoords);
            var organicArm = Part(entities, organic, BodyPartType.Arm, BodyPartSymmetry.Left);
            var mechanical = entities.SpawnEntity("MobIPC", map.GridCoords);
            var mechanicalArm = Part(entities, mechanical, BodyPartType.Arm, BodyPartSymmetry.Left);

            // Heat, because it is the one damage type in the coil's own block that both profiles accept and
            // both wound sets carry (BurnWound's `Heat` and IpcMechanicalDamageWound's `Heat`, both
            // severityMultiplier 1 by default), so the two halves differ only in the capability set.
            Assert.That(routing.TryApplyPartDamage(organic, organicArm, Spec("Heat", 20), null,
                ignoreResistances: true));
            Assert.That(routing.TryApplyPartDamage(mechanical, mechanicalArm, Spec("Heat", 20), null,
                ignoreResistances: true));
            var organicWound = FindWound(entities, wounds, organicArm, "BurnWound");
            var mechanicalWound = FindWound(entities, wounds, mechanicalArm, "IpcMechanicalDamageWound");

            // The organic half. {Electrical} ∩ {Biological} = ∅, so IsCompatiblePart refuses the limb;
            // ResolveHealingPartEvent then reports Accepted == false because the coil's spec contains
            // localized types with no part to put them on, and TryApplyHealing returns before touching
            // anything at all.
            Assert.That(healing.TryApplyHealing(organic, organicArm, (coil, coilHealing), null, out _, out _),
                Is.False, "a cable coil must no longer treat flesh (P5-D12 - a live leak, now closed).");
            Assert.That(organicWound.Comp.Severity, Is.EqualTo(FixedPoint2.New(20)));

            // The mechanical half. {Electrical} ∩ {Mechanical, Electrical} ≠ ∅. The coil's block is
            // Heat/Shock/Radiation -3 each and UniversalTopicalsHealModifier is 1, so the Heat component is a
            // flat -3 on the chassis wound (W0 exempts IpcMechanicalDamageWound from the 0.15 nerf).
            Assert.That(healing.TryApplyHealing(mechanical, mechanicalArm, (coil, coilHealing), null,
                out var healed, out _), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(mechanicalWound.Comp.Severity, Is.EqualTo(FixedPoint2.New(17)),
                    "an IPC and a cybernetic limb gain the coil in the same phase organics lose it.");
                Assert.That(healed.DamageDict.Keys, Does.Contain(new ProtoId<DamageTypePrototype>("Heat")));
            });
        });
    }

    /// <summary>
    /// PLAN5 §6.2 T-P5-17, re-derived. Guards the deliberately ungated systemic branch against someone
    /// "fixing" it while tightening the capability gate.
    /// </summary>
    /// <remarks>
    /// WOLFGATE (WP13-6): PLAN5 words this test as "the coil's <c>Radiation: -3.0</c> component still lands on
    /// an organic". Measured, it does not, and the reason is a layer PLAN5 did not name.
    /// <c>WoundHealingSystem.OnResolveHealingPart</c> sets
    /// <c>Accepted = Part != null || !hasLocalized</c>, where <c>hasLocalized</c> is true if ANY type in the
    /// item's spec is in <c>WoundHostComponent.LocalizedDamageTypes</c>. The coil's spec carries Heat and
    /// Shock, which are localized, so on a body with no capability-compatible part the whole application is
    /// refused before the Radiation component is ever looked at. The invariant PLAN5 actually wanted lives one
    /// layer down, in the routing system, and is asserted here directly.
    /// </remarks>
    [Test]
    public async Task CableCoilRadiationStillHealsSystemicallyTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var routing = entities.System<WoundDamageRoutingSystem>();
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var coil = entities.SpawnEntity("CableApcStack", map.GridCoords);
            var coilHealing = entities.GetComponent<HealingComponent>(coil);

            // Radiation is absent from WoundHostComponent.LocalizedDamageTypes (Blunt, Slash, Piercing, Heat,
            // Cold, Shock, Caustic), so routing keeps it systemic - it never reaches a body part and therefore
            // never reaches CanTreatPart.
            Assert.That(routing.TryApplyDamage(body, Spec("Radiation", 10), null, null, ignoreResistances: true));
            var systemic = entities.GetComponent<SystemicDamageComponent>(body);
            Assert.That(systemic.Damage.DamageDict.GetValueOrDefault(new ProtoId<DamageTypePrototype>("Radiation")),
                Is.EqualTo(FixedPoint2.New(10)));

            // The guard: heal it under the coil's own, deliberately incompatible, capability set.
            routing.WithTreatmentCapabilities(body, coilHealing.TreatmentCapabilities, () =>
                Assert.That(routing.TryApplyDamage(body, Spec("Radiation", -3), null, null,
                    ignoreResistances: true)));
            Assert.That(systemic.Damage.DamageDict.GetValueOrDefault(new ProtoId<DamageTypePrototype>("Radiation")),
                Is.EqualTo(FixedPoint2.New(7)),
                "systemic healing must bypass CanTreatPart entirely - Onyx leaves that branch ungated on " +
                "purpose, and gating it would make every non-localized reagent species-specific by accident.");

            // And the correction, pinned: through the ITEM path the coil is refused outright on an organic,
            // Radiation component included, because the spec's Heat/Shock halves find no compatible part.
            Assert.That(entities.System<WoundHealingSystem>()
                    .TryApplyHealing(body, null, (coil, coilHealing), null, out _, out _),
                Is.False,
                "ResolveHealingPartEvent.Accepted is false whenever a spec carries localized types and no " +
                "part can take them, so an incompatible item lands nothing at all - not even its systemic " +
                "components.");
            Assert.That(systemic.Damage.DamageDict.GetValueOrDefault(new ProtoId<DamageTypePrototype>("Radiation")),
                Is.EqualTo(FixedPoint2.New(7)));
        });
    }

    /// <summary>Applies a fixed -5 heal to one part inside a single-capability treatment scope.</summary>
    private static void Heal(
        WoundDamageRoutingSystem routing,
        EntityUid body,
        EntityUid part,
        TreatmentCapability capability)
    {
        routing.WithTreatmentCapabilities(body, new HashSet<TreatmentCapability> { capability }, () =>
            routing.TryApplyPartDamage(body, part, Spec("Slash", -5), null, ignoreResistances: true));
    }

    private static EntityUid Part(
        IEntityManager entities,
        EntityUid body,
        BodyPartType type,
        BodyPartSymmetry symmetry)
    {
        return entities.System<SharedBodySystem>().GetBodyChildren(body)
            .Single(part => part.Component.PartType == type && part.Component.Symmetry == symmetry)
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
