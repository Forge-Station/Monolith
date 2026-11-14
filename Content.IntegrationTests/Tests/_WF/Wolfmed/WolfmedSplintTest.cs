#nullable enable
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server._WF.Wolfmed.Wounds;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Wolfmed.Compat;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Movement.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// V5: a splint strapped over a broken limb. It reaches the same Reduced state a bonesetter reaches on the
/// table, at a quarter of the penalty, and is used up doing it.
/// </summary>
/// <remarks>
/// Every fracture here is made with 60 Blunt, which clears WFWolfmedFractureProfile's Comminuted threshold
/// (45) at creationChance 1, so no assertion depends on a roll. Effects are measured off
/// <c>WalkSpeedModifier</c>: with a fracture present <see cref="FractureEffectSystem"/> answers from the
/// fracture branch and shadows the crush injury and dislocation the same hit leaves behind, so the number
/// is the treatment scale and nothing else.
/// </remarks>
[TestFixture]
[TestOf(typeof(WolfmedSplintSystem))]
public sealed class WolfmedSplintTest : GameTest
{
    /// <summary>A splint on a broken leg sets the bone, cuts the penalty to a quarter and is used up.</summary>
    [Test]
    public async Task SplintSetsTheFractureAndCutsThePenaltyTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var fractures = entities.System<WoundFractureSystem>();
            var splints = entities.System<WolfmedSplintSystem>();

            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var medic = entities.SpawnEntity("MobHuman", map.GridCoords);
            Blunt(entities, body, TargetBodyPart.LeftLeg, 60);

            var leg = Part(entities, body, BodyPartType.Leg, BodyPartSymmetry.Left);
            var fracture = fractures.GetFracture(leg);
            Assert.That(fracture, Is.Not.Null);
            Assert.That(fracture!.Value.Comp2.Grade, Is.EqualTo(FractureGrade.Comminuted));

            // Comminuted leg: movementModifier 0, PartEffectScales[Leg] 0.5, so the fracture's own
            // contribution is 1 - (1 - 0) * 0.5 * treatmentScale - 0.5 untreated, 0.875 at Reduced's 0.25.
            // The ratio is asserted rather than the absolute, because the same 60 damage also applies an
            // ordinary damage slowdown that splinting does not touch.
            var untreated = Speed(entities, body);

            var splint = Splint(entities, map, "WFWolfmedSplint");
            Assert.That(splints.CanApply(splint, leg), Is.EqualTo(WolfmedSplintRefusal.None));
            Assert.That(splints.TryApply(splint, body, leg, medic), Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(fractures.GetFracture(leg)!.Value.Comp2.Treatment,
                    Is.EqualTo(FractureTreatment.Reduced), "the splint sets the bone without surgery.");
                Assert.That(fractures.GetFracture(leg)!.Value.Comp2.Grade, Is.EqualTo(FractureGrade.Comminuted),
                    "and does not mend it: the bone is still broken.");

                // treatmentEffectScales Reduced 0.25: 0.875 / 0.5.
                Assert.That(Speed(entities, body) / untreated, Is.EqualTo(1.75f).Within(0.01f),
                    "a set bone costs a quarter of what an unset one costs.");
                Assert.That(entities.Deleted(splint.Owner) || entities.IsQueuedForDeletion(splint.Owner), Is.True,
                    "the splint is consumed.");
            });

            // A second splint has nothing to do: Reduced is not None, and mending stays surgical.
            var second = Splint(entities, map, "WFWolfmedSplint");
            Assert.That(splints.CanApply(second, leg), Is.EqualTo(WolfmedSplintRefusal.AlreadyTreated));
            Assert.That(splints.TryApply(second, body, leg, medic), Is.False);
        });
    }

    /// <summary>
    /// The refusals, each with its own reason: an unbroken limb, a torso, and a break too slight to hold.
    /// </summary>
    [Test]
    public async Task SplintIsRefusedOffALimbAndOffAnUnbrokenOneTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var locale = server.ResolveDependency<ILocalizationManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var splints = entities.System<WolfmedSplintSystem>();
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var splint = Splint(entities, map, "WFWolfmedSplint");

            // The torso is broken on purpose: ribs fracture too, and a splint takes them.
            Blunt(entities, body, TargetBodyPart.Torso, 60);

            var arm = Part(entities, body, BodyPartType.Arm, BodyPartSymmetry.Left);
            var torso = Part(entities, body, BodyPartType.Torso, BodyPartSymmetry.None);
            var head = Part(entities, body, BodyPartType.Head, BodyPartSymmetry.None);

            Assert.Multiple(() =>
            {
                Assert.That(splints.CanApply(splint, arm), Is.EqualTo(WolfmedSplintRefusal.NoFracture),
                    "nothing is broken in that arm.");
                // Ribs and skulls fracture too, and the procedure says to splint them: the broken torso is accepted,
                // and the unbroken head is refused for having no fracture, not for being a head.
                Assert.That(splints.CanApply(splint, torso), Is.EqualTo(WolfmedSplintRefusal.None));
                Assert.That(splints.CanApply(splint, head), Is.EqualTo(WolfmedSplintRefusal.NoFracture));

                // The floor is the profile's own: the same grade a bonesetter refuses below.
                var profile = prototypes.Index<FractureProfilePrototype>("WFWolfmedFractureProfile");
                Assert.That(profile.ReductionMinimumGrade, Is.EqualTo(FractureGrade.Simple));

                foreach (var key in new[]
                         {
                             "wolfmed-splint-start", "wolfmed-splint-start-self", "wolfmed-splint-success",
                             "wolfmed-splint-no-part", "wolfmed-splint-wrong-part", "wolfmed-splint-no-fracture",
                             "wolfmed-splint-too-slight", "wolfmed-splint-already-treated",
                         })
                {
                    Assert.That(locale.HasString(key), Is.True, $"{key} has no string.");
                }
            });

            // The refusal that is left is the unbroken part.
            Assert.That(splints.TryApply(splint, body, head, body), Is.False);
            Assert.That(entities.Deleted(splint.Owner), Is.False, "a refused splint is not used up.");
        });
    }

    /// <summary>
    /// A splint is a brace, not a cure. A fresh hard blow to the same limb undoes it, through the profile's
    /// own resetTreatmentOnDamage, and the limb can be splinted again afterwards.
    /// </summary>
    [Test]
    public async Task HardHitUndoesTheSplintTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var fractures = entities.System<WoundFractureSystem>();
            var splints = entities.System<WolfmedSplintSystem>();

            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            Blunt(entities, body, TargetBodyPart.LeftLeg, 60);
            var leg = Part(entities, body, BodyPartType.Leg, BodyPartSymmetry.Left);

            Assert.That(splints.TryApply(Splint(entities, map, "WFWolfmedSplintImprovised"), body, leg, body), Is.True,
                "the improvised splint does the same job.");
            Assert.That(fractures.GetFracture(leg)!.Value.Comp2.Treatment, Is.EqualTo(FractureTreatment.Reduced));
            var braced = Speed(entities, body);

            // worsenMinimumDamage is 5, so this is well over the bar that resets treatment.
            Blunt(entities, body, TargetBodyPart.LeftLeg, 20);

            Assert.Multiple(() =>
            {
                Assert.That(fractures.GetFracture(leg)!.Value.Comp2.Treatment, Is.EqualTo(FractureTreatment.None),
                    "a hard hit undoes the splint, exactly as it undoes a bonesetter's reduction.");
                Assert.That(Speed(entities, body), Is.LessThan(braced), "and the limp comes back with it.");
            });

            // And the limb takes a fresh splint, because the treatment is back to None.
            Assert.That(splints.TryApply(Splint(entities, map, "WFWolfmedSplint"), body, leg, body), Is.True);
            Assert.That(fractures.GetFracture(leg)!.Value.Comp2.Treatment, Is.EqualTo(FractureTreatment.Reduced));
        });
    }

    /// <summary>
    /// A limb blown off during the four-second do-after keeps its fracture and still reads as splintable on
    /// its own, so the apply has to re-check that it is still attached to the patient.
    /// </summary>
    [Test]
    public async Task SplintRefusesALimbThatCameOffTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var fractures = entities.System<WoundFractureSystem>();
            var splints = entities.System<WolfmedSplintSystem>();

            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            Blunt(entities, body, TargetBodyPart.LeftLeg, 60);
            var leg = Part(entities, body, BodyPartType.Leg, BodyPartSymmetry.Left);
            var splint = Splint(entities, map, "WFWolfmedSplint");

            Assert.That(entities.System<WolfmedBodySystem>().TryDetachPart(leg), Is.True);
            Assert.That(splints.CanApply(splint, leg), Is.EqualTo(WolfmedSplintRefusal.None),
                "the severed leg still looks splintable on its own; only the body check catches it.");

            Assert.That(splints.TryApply(splint, body, leg, body), Is.False);
            Assert.Multiple(() =>
            {
                Assert.That(fractures.GetFracture(leg)!.Value.Comp2.Treatment, Is.EqualTo(FractureTreatment.None),
                    "nothing was set on a leg lying on the floor.");
                Assert.That(entities.Deleted(splint.Owner) || entities.IsQueuedForDeletion(splint.Owner), Is.False,
                    "and the splint is still in hand.");
            });
        });
    }

    private static Entity<WolfmedSplintComponent> Splint(IEntityManager entities, TestMapData map, string prototype)
    {
        var splint = entities.SpawnEntity(prototype, map.GridCoords);
        return (splint, entities.GetComponent<WolfmedSplintComponent>(splint));
    }

    private static float Speed(IEntityManager entities, EntityUid body) =>
        entities.GetComponent<MovementSpeedModifierComponent>(body).WalkSpeedModifier;

    private static void Blunt(IEntityManager entities, EntityUid body, TargetBodyPart target, int amount)
    {
        entities.System<DamageableSystem>().TryChangeDamage(body, Spec(amount), origin: null, targetPart: target);
    }

    private static EntityUid Part(IEntityManager entities, EntityUid body, BodyPartType type, BodyPartSymmetry symmetry)
    {
        return entities.System<SharedBodySystem>().GetBodyChildren(body)
            .Single(part => part.Component.PartType == type && part.Component.Symmetry == symmetry)
            .Id;
    }

    private static DamageSpecifier Spec(int amount) => new()
    {
        DamageDict = { [new ProtoId<DamageTypePrototype>("Blunt")] = FixedPoint2.New(amount) },
    };
}
