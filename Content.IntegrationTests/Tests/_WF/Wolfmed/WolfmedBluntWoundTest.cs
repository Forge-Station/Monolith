#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server._WF.Wolfmed.Wounds;
using Content.Server.Medical.Components;
using Content.Server.Speech.Components;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Wolfmed.Body;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Bed.Sleep;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Eye.Blinding.Components;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Stunnable;
using Content.Shared.Verbs;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// W3: what a heavy blunt hit leaves behind - a crushed limb, a rattled head, a popped joint, a bruised
/// organ - and what each of them takes to clear.
/// </summary>
/// <remarks>
/// The wounds and their thresholds are data (<c>_WF/Wolfmed/Wounds/blunt.yml</c> plus their rules). The new
/// behaviour is <see cref="WolfmedConcussionSystem"/>, <see cref="WolfmedDislocationSystem"/> and
/// <see cref="WolfmedOrganContusionSystem"/>, all three fed by the
/// <see cref="WolfmedWoundLifecycleEvent"/> relay. Effects are asserted off freshly created wounds rather
/// than off damage wherever a fracture roll (25-100 % between 12 and 45 Blunt) would shadow them.
/// </remarks>
[TestFixture]
[TestOf(typeof(WolfmedConcussionSystem))]
public sealed class WolfmedBluntWoundTest : GameTest
{
    // M6: WFWolfmedFractureProfile with every grade's creation chance at 1, so FractureGradeTest is not a roll.
    [TestPrototypes]
    private const string TestProtos = @"
- type: fractureProfile
  id: WolfmedTestFractureProfileCertain
  damageType: Blunt
  wound: BoneFractureWound
  severityMultiplier: 1
  resetTreatmentOnDamage: true
  worsenMinimumDamage: 5
  minimumHitDamage: 3
  accumulationMultiplier: 0.8
  reductionMinimumGrade: Simple
  removeWoundWhenMended: true
  alert: BrokenBones
  alertMinimumGrade: Simple
  alertHiddenTreatments: [Mended]
  treatmentEffectScales:
    None: 1
    Reduced: 0.25
    Mended: 0
  grades:
    Hairline:
      threshold: 12
      creationChance: 1
      movementModifier: 0.625
      manipulationModifier: 1.1
    Simple:
      threshold: 20
      creationChance: 1
      movementModifier: 0.5
      manipulationModifier: 1.25
    Displaced:
      threshold: 32
      creationChance: 1
      movementModifier: 0
      manipulationModifier: 1.5
    Comminuted:
      threshold: 45
      creationChance: 1
      movementModifier: 0
      manipulationModifier: 2.0

- type: entity
  id: WolfmedTestFractureHeldItem
  components:
  - type: Item
";

    /// <summary>
    /// <c>FractureGradeTest</c> (plan §12 M6, P30): a fracture made from accumulated damage starts at the grade that
    /// trauma earned. It used to start at the last hit's severity alone, under its own lowest grade: no grade, no
    /// penalty, no treatment, and it shadowed any other limb penalty. And the grade is the penalty: a worse grade slows
    /// the hand more, and a reduction scales it down.
    /// </summary>
    [Test]
    public async Task FractureGradeTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var fractures = entities.System<WoundFractureSystem>();
            var effects = entities.System<FractureEffectSystem>();
            var hands = entities.System<SharedHandsSystem>();
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var arm = Part(entities, body, BodyPartType.Arm, BodyPartSymmetry.Left);
            entities.GetComponent<WolfmedBodyPartComponent>(arm).FractureProfile = "WolfmedTestFractureProfileCertain";

            // The manipulation multiplier reads the arm on the side of the hand holding the item.
            var item = entities.SpawnEntity("WolfmedTestFractureHeldItem", map.GridCoords);
            var handsComp = entities.GetComponent<HandsComponent>(body);
            var left = hands.EnumerateHands(body, handsComp).Single(hand => hand.Location == HandLocation.Left);
            Assert.That(hands.TryPickup(body, item, left, checkActionBlocker: false, animate: false, handsComp: handsComp));
            Assert.That(effects.GetDurationMultiplier(body, item), Is.EqualTo(1f).Within(0.001f));

            // 10 Blunt is under Hairline's 12: no fracture, and the arm keeps the 10.
            Blunt(entities, body, TargetBodyPart.LeftArm, 10);
            Assert.That(fractures.GetFracture(arm), Is.Null, "10 Blunt broke a bone.");

            // Another 10: 10 + 10 x 0.8 = 18 of trauma, a Hairline fracture, although the hit alone is under every grade.
            Blunt(entities, body, TargetBodyPart.LeftArm, 10);
            var fracture = fractures.GetFracture(arm);
            Assert.That(fracture, Is.Not.Null, "accumulated trauma of 18 made no fracture.");
            Assert.Multiple(() =>
            {
                Assert.That(fracture!.Value.Comp2.Grade, Is.EqualTo(FractureGrade.Hairline),
                    "a fracture from accumulated damage has no grade (P30).");
                Assert.That(fracture.Value.Comp1.Severity.Float(), Is.EqualTo(18f).Within(0.05f),
                    "the fracture did not start at the trauma that graded it.");
            });
            var hairline = effects.GetDurationMultiplier(body, item);
            Assert.That(hairline, Is.EqualTo(1.1f).Within(0.001f), "a Hairline fracture does not slow the hand.");

            // 20 more worsens it by the hit: 38, Displaced, and the hand slows further.
            Blunt(entities, body, TargetBodyPart.LeftArm, 20);
            fracture = fractures.GetFracture(arm);
            Assert.That(fracture!.Value.Comp2.Grade, Is.EqualTo(FractureGrade.Displaced));
            var displaced = effects.GetDurationMultiplier(body, item);
            Assert.That(displaced, Is.EqualTo(1.5f).Within(0.001f));
            Assert.That(displaced, Is.GreaterThan(hairline), "a worse grade is not a worse penalty.");

            // A graded fracture takes treatment: reduced, the penalty is a quarter.
            Assert.That(fractures.TryReduce(fracture.Value.Owner), Is.True, "the fracture cannot be reduced.");
            Assert.That(effects.GetDurationMultiplier(body, item), Is.EqualTo(1f + 0.5f * 0.25f).Within(0.001f));
        });
    }

    /// <summary>
    /// A heavy swing crushes the limb in place of the ordinary bruise and pops the joint on the way; a
    /// light one does neither.
    /// </summary>
    [Test]
    public async Task CrushInjuryReplacesTheBruiseTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var wounds = entities.System<WoundSystem>();
            var traits = entities.System<WolfmedWoundTraitSystem>();
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);

            Blunt(entities, body, TargetBodyPart.LeftArm, 35);
            Blunt(entities, body, TargetBodyPart.RightArm, 8);

            var crushed = Part(entities, body, BodyPartType.Arm, BodyPartSymmetry.Left);
            var bruised = Part(entities, body, BodyPartType.Arm, BodyPartSymmetry.Right);

            Assert.Multiple(() =>
            {
                Assert.That(Prototypes(entities, wounds, crushed), Does.Contain("WFWolfmedCrushInjuryWound")
                        .And.Not.Contain("BluntWound"),
                    "at this size the bruise IS the crush injury, so the rule replaces it.");
                Assert.That(Prototypes(entities, wounds, crushed), Does.Contain("WFWolfmedDislocationWound"),
                    "`continue` lets the same swing pop the joint.");
                Assert.That(Prototypes(entities, wounds, bruised), Does.Contain("BluntWound")
                        .And.Not.Contain("WFWolfmedCrushInjuryWound")
                        .And.Not.Contain("WFWolfmedDislocationWound"),
                    "a light hit is still just a bruise.");
                Assert.That(traits.TryGetLimbPenalty(crushed, true, out _), Is.True,
                    "a crushed limb costs its use, through the same modifier a fracture uses.");
            });

            var crush = FindWound(entities, wounds, crushed, "WFWolfmedCrushInjuryWound");
            Assert.That(entities.GetComponent<WoundComponent>(crush).Severity, Is.EqualTo(FixedPoint2.New(35)));
        });
    }

    /// <summary>
    /// A crushing blow can start a bleed nothing external will reach. M3 (OD15): a band, not a roll. Every blow
    /// of 40 or more bleeds inside, and a crush under it never does.
    /// </summary>
    [Test]
    public async Task CrushCanBleedInternallyTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var wounds = entities.System<WoundSystem>();

            for (var i = 0; i < 4; i++)
            {
                var heavy = entities.SpawnEntity("MobHuman", map.GridCoords);
                var light = entities.SpawnEntity("MobHuman", map.GridCoords);
                Blunt(entities, heavy, TargetBodyPart.Torso, 40);
                Blunt(entities, light, TargetBodyPart.Torso, 35);

                var heavyTorso = Part(entities, heavy, BodyPartType.Torso, BodyPartSymmetry.None);
                var lightTorso = Part(entities, light, BodyPartType.Torso, BodyPartSymmetry.None);
                Assert.Multiple(() =>
                {
                    Assert.That(Prototypes(entities, wounds, heavyTorso), Does.Contain("WFWolfmedCrushInjuryWound")
                        .And.Contain("InternalBleedingWound"), $"run {i}: a Blunt 40 blow did not bleed inside.");
                    Assert.That(Prototypes(entities, wounds, lightTorso), Does.Contain("WFWolfmedCrushInjuryWound")
                        .And.Not.Contain("InternalBleedingWound"), $"run {i}: a Blunt 35 crush bled inside.");
                });
            }
        });
    }

    /// <summary>
    /// A knock to the head blurs sight, slurs speech and drops the patient; no item touches it, it fades
    /// on its own, and it fades four times faster in bed.
    /// </summary>
    [Test]
    public async Task ConcussionFadesOnlyWithTimeTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var wounds = entities.System<WoundSystem>();
            var concussions = entities.System<WolfmedConcussionSystem>();
            var healing = entities.System<WoundHealingSystem>();

            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            Blunt(entities, body, TargetBodyPart.Head, 25);

            // M3 (plan §8): Blunt 25 also passes the head's reach line (15) and takes 3 off the brain, and a brain
            // under 90% slurs on its own. This test is about the wound's stages, so the brain is made whole again.
            var brain = entities.System<Content.Server._WF.Wolfmed.Life.WolfmedLifeSystem>().GetBrainOrgan(body)!.Value;
            Assert.That(brain.Comp.Health, Is.LessThan(brain.Comp.MaxHealth), "the blow did not reach the brain.");
            entities.System<Content.Shared._Onyx.Body.Systems.OrganHealthSystem>().SetHealth(brain, brain.Comp.MaxHealth);

            var head = Part(entities, body, BodyPartType.Head, BodyPartSymmetry.None);
            var concussion = FindWound(entities, wounds, head, "WFWolfmedConcussionWound");

            Assert.Multiple(() =>
            {
                // severityMultiplier 0.6: 25 Blunt is a moderate concussion.
                Assert.That(entities.GetComponent<WoundComponent>(concussion).Severity,
                    Is.EqualTo(FixedPoint2.New(15)));
                Assert.That(entities.TryGetComponent(body, out WolfmedConcussionComponent? state), Is.True);
                Assert.That(state!.Blur, Is.EqualTo(3f), "the moderate stage's blur.");
                Assert.That(state.Stutter, Is.True);
                Assert.That(entities.GetComponent<BlurryVisionComponent>(body).Magnitude, Is.GreaterThanOrEqualTo(3f));
                Assert.That(entities.HasComponent<StutteringAccentComponent>(body), Is.True);
                Assert.That(entities.HasComponent<KnockedDownComponent>(body), Is.True,
                    "the blow itself puts them on the floor.");
            });

            // Nothing in a medkit is a cure; the wound carries no damage types to heal.
            var brutepack = entities.SpawnEntity("BrutepackAdvanced1", map.GridCoords);
            healing.TryApplyHealing(body, head,
                (brutepack, entities.GetComponent<HealingComponent>(brutepack)), body, out _, out _);
            Assert.That(entities.GetComponent<WoundComponent>(concussion).Severity, Is.EqualTo(FixedPoint2.New(15)));

            // A minute on their feet is worth two severity, and drops the stage back to minor.
            concussions.Recover(body, 60f);
            Assert.Multiple(() =>
            {
                Assert.That(entities.GetComponent<WoundComponent>(concussion).Severity,
                    Is.EqualTo(FixedPoint2.New(13)));
                Assert.That(entities.GetComponent<WolfmedConcussionComponent>(body).Blur, Is.EqualTo(1.5f));
                Assert.That(entities.GetComponent<WolfmedConcussionComponent>(body).Stutter, Is.False,
                    "the slur belongs to the moderate stage and up.");
            });

            // The same minute asleep is worth four times as much.
            entities.AddComponent<SleepingComponent>(body);
            Assert.That(concussions.IsResting(body), Is.True);
            concussions.Recover(body, 60f);
            Assert.That(entities.GetComponent<WoundComponent>(concussion).Severity, Is.EqualTo(FixedPoint2.New(5)));

            // The wound entity itself is only queued for deletion here, and the blur overlay is removed
            // deferred; what is immediate is that the part no longer carries it and the body is clear.
            concussions.Recover(body, 600f);
            Assert.Multiple(() =>
            {
                Assert.That(Prototypes(entities, wounds, head), Does.Not.Contain("WFWolfmedConcussionWound"),
                    "it heals all the way out.");
                Assert.That(entities.HasComponent<WolfmedConcussionComponent>(body), Is.False);
            });
        });
    }

    /// <summary>
    /// A dislocated joint costs the limb its use, refuses every treatment, and comes back only when
    /// someone sets it by hand.
    /// </summary>
    [Test]
    public async Task DislocationIsSetByHandTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var wounds = entities.System<WoundSystem>();
            var traits = entities.System<WolfmedWoundTraitSystem>();
            var joints = entities.System<WolfmedDislocationSystem>();
            var healing = entities.System<WoundHealingSystem>();
            var verbs = entities.System<SharedVerbSystem>();

            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var medic = entities.SpawnEntity("MobHuman", map.GridCoords);
            Blunt(entities, body, TargetBodyPart.LeftLeg, 25);

            var leg = Part(entities, body, BodyPartType.Leg, BodyPartSymmetry.Left);
            var dislocation = FindWound(entities, wounds, leg, "WFWolfmedDislocationWound");

            Assert.Multiple(() =>
            {
                Assert.That(entities.GetComponent<WoundComponent>(dislocation).Severity,
                    Is.EqualTo(FixedPoint2.New(10)));
                Assert.That(traits.TryGetLimbPenalty(leg, true, out var modifier), Is.True);
                Assert.That(modifier, Is.EqualTo(0.65f), "a popped joint limps like a broken one.");
                Assert.That(joints.FindDislocation(body, medic)?.Owner, Is.EqualTo(dislocation));
                Assert.That(verbs.GetLocalVerbs(body, medic, typeof(AlternativeVerb))
                        .Any(verb => verb.Text == Loc.GetString("wolfmed-relocate-verb")), Is.True,
                    "the medic is offered the relocate verb on the patient.");
            });

            foreach (var item in new[] { "Gauze1", "BrutepackAdvanced1" })
            {
                var topical = entities.SpawnEntity(item, map.GridCoords);
                healing.TryApplyHealing(body, leg,
                    (topical, entities.GetComponent<HealingComponent>(topical)), body, out _, out _);
            }

            Assert.That(entities.GetComponent<WoundComponent>(dislocation).Severity,
                Is.EqualTo(FixedPoint2.New(10)), "no damage type means no topical can reach it.");

            Assert.That(joints.TryRelocate(body, dislocation, medic), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(Prototypes(entities, wounds, leg), Does.Not.Contain("WFWolfmedDislocationWound"));
                Assert.That(traits.TryGetLimbPenalty(leg, true, out _), Is.False, "and the limp goes with it.");
                Assert.That(joints.FindDislocation(body, medic), Is.Null);
            });

            // Doing it to yourself is the same procedure, slower and twice as painful.
            var prototype = prototypes.Index<WoundPrototype>("WFWolfmedDislocationWound");
            Assert.That(prototype.TryGetBehavior(FixedPoint2.New(10), out WolfmedDislocationBehavior behavior));
            Assert.Multiple(() =>
            {
                Assert.That(behavior.SelfMultiplier, Is.GreaterThan(1f));
                Assert.That(behavior.SelfPainMultiplier, Is.GreaterThan(1f));
                Assert.That(behavior.Pain, Is.GreaterThan(FixedPoint2.Zero));
            });
        });
    }

    /// <summary>A blow to the chest bruises an organ and never finishes one off.</summary>
    [Test]
    public async Task OrganContusionBruisesWithoutDestroyingTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var wounds = entities.System<WoundSystem>();
            var contusions = entities.System<WolfmedOrganContusionSystem>();

            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var torso = Part(entities, body, BodyPartType.Torso, BodyPartSymmetry.None);
            var before = OrganHealth(entities, torso);

            Blunt(entities, body, TargetBodyPart.Torso, 25);

            Assert.Multiple(() =>
            {
                Assert.That(Prototypes(entities, wounds, torso), Does.Contain("WFWolfmedOrganContusionWound"));
                Assert.That(OrganHealth(entities, torso), Is.LessThan(before),
                    "the blow reached something inside the chest.");
                Assert.That(Organs(entities, torso).All(organ => organ.Comp.Health > FixedPoint2.Zero), Is.True,
                    "and left all of it working.");
            });

            // The clamp, at a size no hit would ever deal: a contusion bruises, it never destroys.
            var bruised = contusions.TryBruise(torso, FixedPoint2.New(1000), FixedPoint2.New(1));
            Assert.That(bruised, Is.Not.Null);
            Assert.That(entities.GetComponent<WolfmedOrganComponent>(bruised!.Value).Health,
                Is.EqualTo(FixedPoint2.New(1)));
        });
    }

    /// <summary>The analyzer names the four wounds and the guide has words for them.</summary>
    [Test]
    public async Task NamesExistTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var locale = server.ResolveDependency<ILocalizationManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var analyzer = entities.System<Content.Server.Medical.HealthAnalyzerSystem>();
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            Blunt(entities, body, TargetBodyPart.LeftArm, 35);

            var diagnostics = analyzer.BuildWoundDiagnostics(body);
            Assert.That(diagnostics, Is.Not.Null);
            Assert.That(diagnostics!.Parts.TryGetValue(TargetBodyPart.LeftArm, out var arm));

            Assert.Multiple(() =>
            {
                Assert.That(arm.VisibleWounds.Any(wound => wound.Name == "wolfmed-wound-name-crush-injury"));
                Assert.That(locale.HasString("wolfmed-wound-name-crush-injury"));
                Assert.That(locale.HasString("wolfmed-wound-name-concussion"));
                Assert.That(locale.HasString("wolfmed-wound-name-dislocation"));
                Assert.That(locale.HasString("wolfmed-wound-name-organ-contusion"));
                Assert.That(locale.HasString("wolfmed-relocate-verb"));
                Assert.That(locale.HasString("wolfmed-relocate-start"));
                Assert.That(locale.HasString("wolfmed-relocate-start-self"));
                Assert.That(locale.HasString("wolfmed-relocate-success"));
            });
        });
    }

    /// <summary>
    /// A contusion must not outlive its treatment. A bruise pack removes damage, and damage removal only shrinks
    /// the wound by its healingMultiplier, so the damage ran out first and left a contusion nothing could touch.
    /// Once the damage is gone the pack works on the wound itself.
    /// </summary>
    [Test]
    public async Task BrutePackFinishesTheContusionTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var wounds = entities.System<WoundSystem>();
            var healing = entities.System<Content.Shared._Onyx.Wounds.WoundHealingSystem>();
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var arm = Part(entities, body, BodyPartType.Arm, BodyPartSymmetry.Left);

            // Ten, under the fracture floor, so the only wound is the bruise.
            Blunt(entities, body, TargetBodyPart.LeftArm, 10);
            Assert.That(Prototypes(entities, wounds, arm), Does.Contain("BluntWound"));

            var pack = entities.SpawnEntity("Brutepack", map.GridCoords);
            var item = (pack, entities.GetComponent<HealingComponent>(pack));
            var uses = 0;
            while (uses < 20 && healing.TryApplyHealing(body, arm, item, body, out _, out _))
                uses++;

            Assert.Multiple(() =>
            {
                Assert.That(uses, Is.LessThan(20), "treatment has to come to an end.");
                Assert.That(entities.GetComponent<DamageableComponent>(arm).TotalDamage, Is.EqualTo(FixedPoint2.Zero));
                Assert.That(Prototypes(entities, wounds, arm), Does.Not.Contain("BluntWound"),
                    "the bruise goes with the damage that caused it.");
            });
        });
    }

    private static void Blunt(IEntityManager entities, EntityUid body, TargetBodyPart target, int amount)
    {
        entities.System<DamageableSystem>().TryChangeDamage(body, Spec("Blunt", amount),
            origin: null, targetPart: target);
    }

    private static EntityUid Part(IEntityManager entities, EntityUid body, BodyPartType type, BodyPartSymmetry symmetry)
    {
        return entities.System<SharedBodySystem>().GetBodyChildren(body)
            .Single(part => part.Component.PartType == type && part.Component.Symmetry == symmetry)
            .Id;
    }

    private static List<string> Prototypes(IEntityManager entities, WoundSystem wounds, EntityUid part)
    {
        return wounds.GetWounds((part, entities.GetComponent<WoundableComponent>(part)))
            .Select(wound => wound.Comp.Prototype.Id)
            .ToList();
    }

    private static EntityUid FindWound(IEntityManager entities, WoundSystem wounds, EntityUid part, string prototype)
    {
        return wounds.GetWounds((part, entities.GetComponent<WoundableComponent>(part)))
            .First(wound => wound.Comp.Prototype == new ProtoId<WoundPrototype>(prototype))
            .Owner;
    }

    private static List<Entity<WolfmedOrganComponent>> Organs(IEntityManager entities, EntityUid part)
    {
        return entities.System<SharedBodySystem>().GetPartOrgans(part)
            .Where(organ => entities.HasComponent<WolfmedOrganComponent>(organ.Id))
            .Select(organ => new Entity<WolfmedOrganComponent>(organ.Id,
                entities.GetComponent<WolfmedOrganComponent>(organ.Id)))
            .ToList();
    }

    private static FixedPoint2 OrganHealth(IEntityManager entities, EntityUid part)
    {
        var total = FixedPoint2.Zero;
        foreach (var organ in Organs(entities, part))
            total += organ.Comp.Health;

        return total;
    }

    private static DamageSpecifier Spec(string type, int amount) => new()
    {
        DamageDict = { [new ProtoId<DamageTypePrototype>(type)] = FixedPoint2.New(amount) },
    };
}
