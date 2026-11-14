using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server.EntityEffects.Effects; // WOLFGATE: HealthChange/EvenHealthChange are server-assembly effects.
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.Compat; // WOLFGATE: D12 damage facade.
using Content.Shared._WF.Wolfmed.EntityEffects; // WOLFGATE: WP12-1's four old-style effect classes.
using Content.Shared.Body.Part;
using Content.Shared.Body.Prototypes;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.EntityEffects;
using Content.Shared.FixedPoint;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// The reagent half of phase 4: HOOK 9's treatment-capability scope on <see cref="HealthChange"/>, and the four
/// old-style effect classes WP12-1 re-authored from Onyx's ECS ones.
/// </summary>
/// <remarks>
/// PLAN4 §6.2 T-REAGENT-CAP-YES / -CAP-NO / -SYSTEMIC-BYPASS / -SUPPRESS / -MEND / -STAM /
/// -PROTOTYPE-SANITY (WP12-9). Per §6.1 trap 8 no chemistry is involved: the effect args are constructed
/// directly and <c>Effect(args)</c> is called, which is exactly what MetabolizerSystem does at the end of its
/// own pipeline. Per §8.5 risk 23 these tests assert the capability SCOPE, never "a reagent does not touch
/// wounds" - GUARD D has routed reagent healing into wounds since phase 1.
/// </remarks>
[TestFixture]
[TestOf(typeof(SuppressPain))]
public sealed class WolfmedReagentTreatmentTest : GameTest
{
    // WOLFGATE: a Shitmed body graph like every other Wolfmed fixture. The pain fixture needs `- type: MobState`
    // and an explicit `- type: StatusEffects` allow-list (P2-D24); the stamina fixture needs `- type: Stamina`,
    // which StaminaSystem.TakeStaminaDamage resolves with logMissing:false and would otherwise silently no-op.
    // WolfmedReagentControlBody is the same graph WITHOUT WoundHost - it is what proves MendFractures' host gate
    // and, by extension, D2.
    [TestPrototypes]
    private const string Prototypes = @"
- type: body
  id: WolfmedReagentBodyGraph
  name: ""wolfmed reagent body""
  root: torso
  slots:
    torso:
      part: TorsoHuman
      connections:
      - head
      - left arm
    head:
      part: HeadHuman
    left arm:
      part: LeftArmHuman

- type: entity
  id: WolfmedReagentBody
  parent: InventoryBase
  components:
  - type: Body
    prototype: WolfmedReagentBodyGraph
  - type: Damageable
    damageContainer: Biological
  - type: MobState
  - type: Stamina
  - type: StatusEffects
    allowed:
    - Stun
    - KnockedDown
    - Jitter
  - type: WoundHost

- type: entity
  id: WolfmedReagentControlBody
  parent: InventoryBase
  components:
  - type: Body
    prototype: WolfmedReagentBodyGraph
  - type: Damageable
    damageContainer: Biological
  - type: MobState
  - type: Stamina
";

    /// <summary>
    /// PLAN4 §6.2 T-REAGENT-CAP-YES. Pins §2.9's "reagents already heal wounds today" so a later refactor
    /// cannot silently break it.
    /// </summary>
    [Test]
    public async Task TreatmentCapabilityMatchHealsWoundTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var (body, head, _) = Spawn(entities, map.GridCoords, "WolfmedReagentBody");
            var wounds = entities.System<WoundSystem>();
            var damage = entities.System<WolfmedDamageableSystem>();

            Assert.That(entities.System<WoundDamageRoutingSystem>()
                .TryApplyPartDamage(body, head, Spec("Blunt", 20)));

            // Derived, not assumed: BluntWound's `damageTypes.Blunt.severityMultiplier` is 1
            // (_Onyx/Wounds/wounds.yml), and WoundSystem.HandlePartDamageApplied creates the wound at
            // `amount * severityMultiplier`, so 20 Blunt is severity 20 on the nose.
            var wound = FindWound(entities, wounds, head, "BluntWound");
            Assert.Multiple(() =>
            {
                Assert.That(wound.Comp.Severity, Is.EqualTo(FixedPoint2.New(20)));
                Assert.That(damage.GetAllDamage(head).GetTotal(), Is.EqualTo(FixedPoint2.New(20)));
            });

            // Biological is what OrganicBodyPartProfile declares (wounds.yml:4), so the scope opens and passes.
            new HealthChange
            {
                Damage = Spec("Blunt", -5),
                TreatmentCapabilities = [TreatmentCapability.Biological],
            }.Effect(Args(entities, body));

            Assert.Multiple(() =>
            {
                // WOLFGATE (W0): BluntWound now sets `healingMultiplier: 0.15`, so WoundSystem.HealWounds
                // takes 0.75 of severity off for -5 Blunt while the part's own damage drops the full 5.
                Assert.That(wound.Comp.Severity, Is.EqualTo(FixedPoint2.New(19.25)));
                Assert.That(damage.GetAllDamage(head).GetTotal(), Is.EqualTo(FixedPoint2.New(15)));
            });
        });
    }

    /// <summary>
    /// PLAN4 §6.2 T-REAGENT-CAP-NO. The ONLY test that proves HOOK 9 is wired at all (§6.1 trap 7, §8.5
    /// risk 2): if the implementer forgets <c>WithTreatmentCapabilities</c>, CanTreatPart defaults to true and
    /// this heal lands.
    /// </summary>
    [Test]
    public async Task TreatmentCapabilityMismatchDoesNotHealTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var (body, head, _) = Spawn(entities, map.GridCoords, "WolfmedReagentBody");
            var wounds = entities.System<WoundSystem>();
            var damage = entities.System<WolfmedDamageableSystem>();

            Assert.That(entities.System<WoundDamageRoutingSystem>()
                .TryApplyPartDamage(body, head, Spec("Blunt", 20)));
            var wound = FindWound(entities, wounds, head, "BluntWound");

            new HealthChange
            {
                Damage = Spec("Blunt", -5),
                TreatmentCapabilities = [TreatmentCapability.Mechanical],
            }.Effect(Args(entities, body));

            Assert.Multiple(() =>
            {
                // OrganicBodyPartProfile.treatmentCapabilities is [Biological], so
                // profile.TreatmentCapabilities.Overlaps({Mechanical}) is false and CanTreatPart refuses the
                // part outright - ApplyLocalizedHealing then has no part to distribute the heal across.
                Assert.That(wound.Comp.Severity, Is.EqualTo(FixedPoint2.New(20)),
                    "a capability mismatch must leave the wound untouched - this is the HOOK 9 gate.");
                Assert.That(damage.GetAllDamage(head).GetTotal(), Is.EqualTo(FixedPoint2.New(20)));
            });
        });
    }

    /// <summary>
    /// PLAN4 §6.2 T-REAGENT-SYSTEMIC-BYPASS. Guards against someone "fixing" the capability gate into the
    /// systemic branch, which Onyx deliberately leaves ungated.
    /// </summary>
    [Test]
    public async Task SystemicHealingIgnoresTreatmentCapabilitiesTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var (body, _, _) = Spawn(entities, map.GridCoords, "WolfmedReagentBody");
            var damage = entities.System<WolfmedDamageableSystem>();

            // WOLFGATE: PLAN4 writes "Toxin", which is a damage GROUP in Wolfgate, not a damage type - the
            // types under it are Poison and Radiation (Resources/Prototypes/Damage/groups.yml). Poison is
            // absent from WoundHostComponent.LocalizedDamageTypes (Blunt/Slash/Piercing/Heat/Cold/Shock/
            // Caustic), which is the property the test actually needs, so it stands in.
            Assert.That(damage.TryChangeDamage(body, Spec("Poison", 10), out _));
            var systemic = entities.GetComponent<SystemicDamageComponent>(body);
            Assert.That(systemic.Damage.DamageDict[new ProtoId<DamageTypePrototype>("Poison")],
                Is.EqualTo(FixedPoint2.New(10)));

            new HealthChange
            {
                Damage = Spec("Poison", -4),
                TreatmentCapabilities = [TreatmentCapability.Mechanical],
            }.Effect(Args(entities, body));

            Assert.That(systemic.Damage.DamageDict[new ProtoId<DamageTypePrototype>("Poison")],
                Is.EqualTo(FixedPoint2.New(6)),
                "systemic healing must bypass CanTreatPart entirely (WoundDamageRoutingSystem.RouteAppliedDamage).");
        });
    }

    /// <summary>PLAN4 §6.2 T-REAGENT-SUPPRESS.</summary>
    [Test]
    public async Task SuppressPainLowersEffectivePainTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var (body, head, _) = Spawn(entities, map.GridCoords, "WolfmedReagentBody");
            var pain = entities.System<PainSystem>();
            var painComp = entities.GetComponent<PainComponent>(body);

            // WOLFGATE (W0): 10 rather than 15 - WFWolfmedFractureProfile's Hairline threshold is 12 at a 25 %
            // roll, and a stray bone fracture's own pain behaviour would push `before` past the 20 this test
            // needs it to stay under.
            Assert.That(entities.System<WoundDamageRoutingSystem>()
                .TryApplyPartDamage(body, head, Spec("Blunt", 10)));

            // Measured, not predicted (P2-D16): the exact figure is the sum of PainComponent.DamageMultipliers
            // ["Blunt"] = 0.87 on the hit and BluntWound's WoundPainBehavior floor, which phase 2 already pins
            // in WolfmedPainTest. All this test needs is that there IS pain to suppress.
            var before = pain.GetPain((body, painComp));
            Assert.That(before, Is.GreaterThan(FixedPoint2.Zero));
            Assert.That(before, Is.LessThan(FixedPoint2.New(20)),
                "the suppression amount below must exceed the pain for the floor-at-zero assertion to bite.");

            new SuppressPain { Amount = 20, DecayDuration = TimeSpan.FromSeconds(30) }.Effect(Args(entities, body));

            Assert.Multiple(() =>
            {
                // GetPain is the effective, suppression-adjusted figure (PainSystem.cs:165); GetRawPain (:198)
                // is the unsuppressed one and must NOT move - suppression hides pain, it does not heal it.
                Assert.That(pain.GetPain((body, painComp)), Is.EqualTo(FixedPoint2.Zero));
                Assert.That(pain.GetRawPain((body, painComp)), Is.EqualTo(before));
                // `identifier` defaults to "PainSuppressant" on the effect class.
                Assert.That(painComp.SuppressionModifiers.ContainsKey("PainSuppressant"), Is.True);
                Assert.That(painComp.SuppressionModifiers["PainSuppressant"].Amount,
                    Is.EqualTo(FixedPoint2.New(20)));
            });

            // PainSystem.SuppressPain reads the existing entry and ADDS to it (:346-350): a second dose of the
            // same drug accumulates rather than resetting.
            new SuppressPain { Amount = 20, DecayDuration = TimeSpan.FromSeconds(30) }.Effect(Args(entities, body));
            Assert.That(painComp.SuppressionModifiers["PainSuppressant"].Amount, Is.EqualTo(FixedPoint2.New(40)));

            // A different identifier is a separate ladder rung and stacks independently.
            new SuppressPain
            {
                Amount = 5,
                DecayDuration = TimeSpan.FromSeconds(30),
                Identifier = "WolfmedTestDrug",
            }.Effect(Args(entities, body));

            Assert.Multiple(() =>
            {
                Assert.That(painComp.SuppressionModifiers, Has.Count.EqualTo(2));
                Assert.That(painComp.SuppressionModifiers["WolfmedTestDrug"].Amount,
                    Is.EqualTo(FixedPoint2.New(5)));
            });

            // DecayPerSecond is rewritten as accumulated/decayDuration on every dose and stored as a
            // FixedPoint2, i.e. rounded to two places: 40/30 is kept as 1.33 and 5/30 as 0.16, so the nominal
            // 30 s leaves 0.1 and 0.2 behind. Sixty seconds is past both deadlines and clears them outright -
            // measured here rather than predicted (P2-D16).
            Assert.That(pain.DecayPainSuppression((body, painComp), 60f));
            Assert.Multiple(() =>
            {
                Assert.That(painComp.SuppressionModifiers, Is.Empty);
                Assert.That(pain.GetPain((body, painComp)), Is.EqualTo(before),
                    "once the suppression has decayed away the original pain is back, undiminished.");
            });
        });
    }

    /// <summary>PLAN4 §6.2 T-REAGENT-MEND.</summary>
    [Test]
    public async Task MendFracturesReducesMatchingFractureTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var (body, _, arm) = Spawn(entities, map.GridCoords, "WolfmedReagentBody");
            var routing = entities.System<WoundDamageRoutingSystem>();
            var fractures = entities.System<WoundFractureSystem>();

            // P2-D23 / W0: 75 Blunt clears WFWolfmedFractureProfile's Comminuted threshold (45), whose
            // creationChance is 1 - the only deterministic fracture grade.
            Assert.That(routing.TryApplyPartDamage(body, arm, Spec("Blunt", 75)));
            var fracture = fractures.GetFracture(arm)!.Value;
            Assert.Multiple(() =>
            {
                Assert.That(fracture.Comp2.Grade, Is.EqualTo(FractureGrade.Comminuted));
                Assert.That(fracture.Comp1.Severity, Is.EqualTo(FixedPoint2.New(75)));
            });

            // Defaults: wounds = [BoneFractureWound], minimumGrade Hairline, maximumGrade Comminuted.
            new MendFractures { Amount = 5 }.Effect(Args(entities, body));
            Assert.That(fracture.Comp1.Severity, Is.EqualTo(FixedPoint2.New(70)),
                "MendFractures removes `amount` severity per application (WoundSystem.ChangeSeverity).");

            // Osteogen's shape: maximumGrade Simple cannot touch a Comminuted fracture.
            new MendFractures { Amount = 5, MaximumGrade = FractureGrade.Simple }.Effect(Args(entities, body));
            Assert.That(fracture.Comp1.Severity, Is.EqualTo(FixedPoint2.New(70)));

            // A prototype filter that does not name BoneFractureWound is a no-op.
            new MendFractures { Amount = 5, Wounds = ["SlashWound"] }.Effect(Args(entities, body));
            Assert.That(fracture.Comp1.Severity, Is.EqualTo(FixedPoint2.New(70)));

            // Stasizium's shape: `wounds: []` means every fracture prototype, all grades.
            new MendFractures { Amount = 10, Wounds = [] }.Effect(Args(entities, body));
            Assert.That(fracture.Comp1.Severity, Is.EqualTo(FixedPoint2.New(60)));

            // D2: the HasComponent<WoundHostComponent> gate. The control body has the same graph and the same
            // parts and simply is not a wound host, so it has no fracture to mend and the effect returns early.
            var control = entities.SpawnEntity("WolfmedReagentControlBody", map.GridCoords);
            Assert.That(entities.HasComponent<WoundHostComponent>(control), Is.False);
            new MendFractures { Amount = 10, Wounds = [] }.Effect(Args(entities, control));
            Assert.That(fracture.Comp1.Severity, Is.EqualTo(FixedPoint2.New(60)),
                "the effect must act on its own target only.");
        });
    }

    /// <summary>PLAN4 §6.2 T-REAGENT-STAM.</summary>
    [Test]
    public async Task StaminaEffectAndConditionTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var (body, _, _) = Spawn(entities, map.GridCoords, "WolfmedReagentBody");
            var stamina = entities.System<StaminaSystem>();
            var staminaComp = entities.GetComponent<StaminaComponent>(body);

            Assert.Multiple(() =>
            {
                Assert.That(stamina.GetStaminaDamage(body, staminaComp), Is.EqualTo(0f));
                // Min defaults to -1, so an undamaged target is already above it; the explicit Min 20 below is
                // what the condition is actually for.
                Assert.That(new StaminaDamageCondition { Min = 20 }.Condition(Args(entities, body)), Is.False);
            });

            new TakeStaminaDamage { Amount = 30 }.Effect(Args(entities, body));

            Assert.Multiple(() =>
            {
                // StaminaSystem.TakeStaminaDamage resets NextUpdate to CurTime + Cooldown, so GetStaminaDamage's
                // decay term is clamped to zero and reads the raw value back immediately.
                Assert.That(stamina.GetStaminaDamage(body, staminaComp), Is.EqualTo(30f));
                Assert.That(new StaminaDamageCondition { Min = 20 }.Condition(Args(entities, body)), Is.True);
                Assert.That(new StaminaDamageCondition { Min = 20, Max = 25 }.Condition(Args(entities, body)),
                    Is.False, "Max is an exclusive upper bound.");
            });

            // P4-D5: the Scale != 1 gate. WG's MetabolizerSystem scale is structurally [0, 1]
            // (mostToRemove / rate), so a partial tick must not apply a partial stun.
            new TakeStaminaDamage { Amount = 30 }.Effect(
                new EntityEffectReagentArgs(body, entities, null, null, FixedPoint2.New(1), null, null,
                    FixedPoint2.New(0.5f)));
            Assert.That(stamina.GetStaminaDamage(body, staminaComp), Is.EqualTo(30f),
                "a partial metabolism tick must be a no-op, not a partial hit.");
        });
    }

    /// <summary>
    /// PLAN4 §6.2 T-REAGENT-PROTOTYPE-SANITY. Prototype-only. The only test that can catch CRITIQUE4 B2 or a
    /// mistranslated <c>!type:</c> (§8.5 risks 4 and 5): the Release lint cannot tell a valid-but-wrong
    /// metabolism group from a right one.
    /// </summary>
    [Test]
    public async Task ShippedReagentsCarryTheRightEffectInTheRightGroupTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var prototypes = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            // Every expected value below is WP12-2's shipped YAML, re-read at authoring time:
            // PROTO K alcohol.yml (Cognac), PROTO I Reagents/medicine.yml (Bicaridine), PROTO J narcotics.yml
            // (Desoxyephedrine, Happiness) and _Onyx/Reagents/Medicine/medicine.yml (the five new ones).
            var expected = new (string Reagent, string Group, FixedPoint2 Amount, double Decay, string Identifier, float Recovery)[]
            {
                ("Cognac", "Drink", FixedPoint2.New(0.25), 9, "Painkiller", 1.1f),
                ("Bicaridine", "Medicine", FixedPoint2.New(0.75), 18, "Bicaridine", 1.75f),
                // Desoxyephedrine carries BOTH a Poison and a Narcotic group (narcotics.yml); the suppression
                // belongs in Narcotic with the rest of the drug effects. A Poison placement lints clean and is
                // silently wrong, which is the whole reason this row exists.
                ("Desoxyephedrine", "Narcotic", FixedPoint2.New(0.75), 9, "Desoxyephedrine", 1.75f),
                ("Happiness", "Narcotic", FixedPoint2.New(0.4), 9, "Happiness", 1.5f),
                ("Ibuprofen", "Medicine", FixedPoint2.New(0.5), 27, "Ibuprofen", 2.5f),
                ("Ketorolac", "Medicine", FixedPoint2.New(0.9), 50, "Ketorolac", 3f),
                ("Tramadol", "Medicine", FixedPoint2.New(1.25), 45, "Tramadol", 3.5f),
                ("Oxycodone", "Medicine", FixedPoint2.New(2), 60, "Oxycodone", 4.5f),
            };

            Assert.Multiple(() =>
            {
                foreach (var (id, group, amount, decay, identifier, recovery) in expected)
                {
                    var effects = EffectsIn(prototypes, id, group);
                    var suppress = effects.OfType<SuppressPain>().ToList();
                    Assert.That(suppress, Has.Count.EqualTo(1), $"{id} must carry exactly one SuppressPain in {group}");
                    Assert.That(suppress[0].Amount, Is.EqualTo(amount), $"{id} suppression amount");
                    Assert.That(suppress[0].DecayDuration.TotalSeconds, Is.EqualTo(decay), $"{id} decay duration");
                    Assert.That(suppress[0].Identifier, Is.EqualTo(identifier), $"{id} suppression identifier");
                    Assert.That(suppress[0].RecoveryMultiplier, Is.EqualTo(recovery), $"{id} recovery multiplier");
                }

                // PROTO H: Stasizium is the universal fracture cure (`wounds: []`, all grades, amount 10).
                var stasizium = EffectsIn(prototypes, "Stasizium", "Medicine").OfType<MendFractures>().ToList();
                Assert.That(stasizium, Has.Count.EqualTo(1));
                Assert.That(stasizium[0].Amount, Is.EqualTo(FixedPoint2.New(10)));
                Assert.That(stasizium[0].Wounds, Is.Empty, "`wounds: []` is what makes Stasizium universal.");
                Assert.That(stasizium[0].MinimumGrade, Is.EqualTo(FractureGrade.Hairline));
                Assert.That(stasizium[0].MaximumGrade, Is.EqualTo(FractureGrade.Comminuted));

                // Osteogen is the deliberately weak one: BoneFractureWound only, capped at Simple.
                var osteogen = EffectsIn(prototypes, "Osteogen", "Medicine").OfType<MendFractures>().ToList();
                Assert.That(osteogen, Has.Count.EqualTo(1));
                Assert.That(osteogen[0].Amount, Is.EqualTo(FixedPoint2.New(1)));
                Assert.That(osteogen[0].Wounds, Is.EquivalentTo(new[] { new ProtoId<WoundPrototype>("BoneFractureWound") }));
                Assert.That(osteogen[0].MaximumGrade, Is.EqualTo(FractureGrade.Simple),
                    "a Displaced or Comminuted fracture must still need a surgeon.");
            });

            // §8.5 risk 4: a copied Onyx `Bloodstream:` / `Digestion:` header is a prototype-load failure for the
            // whole file. Assert every group key of every reagent phase 4 shipped or edited actually resolves.
            var touched = new[]
            {
                "Cognac", "Bicaridine", "Desoxyephedrine", "Happiness", "Stasizium",
                "Osteogen", "Ibuprofen", "Ketorolac", "Tramadol", "Oxycodone",
            };

            Assert.Multiple(() =>
            {
                foreach (var id in touched)
                {
                    var reagent = prototypes.Index<ReagentPrototype>(id);
                    Assert.That(reagent.Metabolisms, Is.Not.Null, $"{id} must declare metabolisms");
                    foreach (var key in reagent.Metabolisms!.Keys)
                        Assert.That(prototypes.HasIndex<MetabolismGroupPrototype>(key), Is.True,
                            $"{id} declares metabolism group '{key.Id}', which does not exist in Wolfgate");
                }
            });
        });
    }

    private static IReadOnlyList<EntityEffect> EffectsIn(IPrototypeManager prototypes, string reagentId, string group)
    {
        var reagent = prototypes.Index<ReagentPrototype>(reagentId);
        Assert.That(reagent.Metabolisms, Is.Not.Null, $"{reagentId} must declare metabolisms");
        Assert.That(reagent.Metabolisms!.ContainsKey(group), Is.True,
            $"{reagentId} must declare a '{group}' metabolism group");
        return reagent.Metabolisms[group].Effects;
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

    private static (EntityUid Body, EntityUid Head, EntityUid Arm) Spawn(
        IEntityManager entities,
        Robust.Shared.Map.EntityCoordinates coords,
        string prototype)
    {
        var body = entities.SpawnEntity(prototype, coords);
        var parts = entities.System<SharedBodySystem>().GetBodyChildren(body).ToList();
        return (body,
            parts.Single(part => part.Component.PartType == BodyPartType.Head).Id,
            parts.Single(part => part.Component.PartType == BodyPartType.Arm).Id);
    }

    private static EntityEffectReagentArgs Args(IEntityManager entities, EntityUid target)
    {
        // §6.1 trap 8: a full-strength metabolism tick, constructed directly - no chemistry involved.
        return new EntityEffectReagentArgs(target, entities, null, null, FixedPoint2.New(1), null, null,
            FixedPoint2.New(1));
    }

    private static DamageSpecifier Spec(string type, int amount) => new()
    {
        DamageDict = { [new ProtoId<DamageTypePrototype>(type)] = FixedPoint2.New(amount) },
    };
}
