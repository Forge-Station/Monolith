#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server.Medical.Components;
using Content.Shared._Onyx.Medical.Tourniquet;
using Content.Shared._Onyx.Targeting;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// W2: what a deep cut and an animal bite leave behind, and what it takes to treat each of them.
/// </summary>
/// <remarks>
/// The three wounds are data (<c>_WF/Wolfmed/Wounds/slash_bite.yml</c> plus their rules); the only new
/// behaviour is <see cref="WolfmedArterialBleedBehavior"/>, read by <see cref="WolfmedWoundTraitSystem"/>
/// and by the Wolfmed halves of the bleeding and healing systems. These tests drive the real damage
/// pipeline, the real gauze item and the real tourniquet rather than those systems directly.
/// </remarks>
[TestFixture]
[TestOf(typeof(WolfmedWoundTraitSystem))]
public sealed class WolfmedSlashBiteWoundTest : GameTest
{
    /// <summary>
    /// A deep cut to a limb opens an artery and severs a tendon on top of the cut itself; a shallow one
    /// does neither, and a torso cut has no tendon to sever.
    /// </summary>
    [Test]
    public async Task DeepCutOpensArteryAndTendonTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var wounds = entities.System<WoundSystem>();
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);

            Slash(entities, body, TargetBodyPart.LeftArm, 25);
            Slash(entities, body, TargetBodyPart.RightArm, 10);
            Slash(entities, body, TargetBodyPart.Torso, 25);

            var deep = Part(entities, body, BodyPartType.Arm, BodyPartSymmetry.Left);
            var shallow = Part(entities, body, BodyPartType.Arm, BodyPartSymmetry.Right);
            var torso = Part(entities, body, BodyPartType.Torso, BodyPartSymmetry.None);

            Assert.Multiple(() =>
            {
                Assert.That(Prototypes(entities, wounds, deep), Is.EquivalentTo(new[]
                {
                    "SlashWound", "WFWolfmedArterialBleedWound", "WFWolfmedTendonCutWound",
                }), "the rules add to the cut rather than replacing it, so one bad swing does all three.");
                Assert.That(Prototypes(entities, wounds, shallow), Is.EquivalentTo(new[] { "SlashWound" }));
                Assert.That(Prototypes(entities, wounds, torso), Does.Contain("WFWolfmedArterialBleedWound")
                    .And.Not.Contain("WFWolfmedTendonCutWound"),
                    "the tendon rule is limbs only.");
            });

            // severityMultiplier 0.5 on both rules, with a floor under it.
            var artery = FindWound(entities, wounds, deep, "WFWolfmedArterialBleedWound");
            Assert.That(entities.GetComponent<WoundComponent>(artery).Severity, Is.EqualTo(FixedPoint2.New(12.5)));
        });
    }

    /// <summary>
    /// The bleed itself: far faster than an ordinary wound, never clotting, slowed but not taken by gauze,
    /// stopped by a tourniquet, and closeable only once it has stopped.
    /// </summary>
    [Test]
    public async Task ArterialBleedAnswersOnlyToATourniquetTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var wounds = entities.System<WoundSystem>();
            var bleeding = entities.System<WoundBleedingSystem>();
            var healing = entities.System<WoundHealingSystem>();
            var tourniquet = entities.System<TourniquetSystem>();

            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            Slash(entities, body, TargetBodyPart.LeftLeg, 25);

            var leg = Part(entities, body, BodyPartType.Leg, BodyPartSymmetry.Left);
            var artery = FindWound(entities, wounds, leg, "WFWolfmedArterialBleedWound");
            var bleed = entities.GetComponent<WoundBleedingComponent>(artery);
            var openRate = bleed.CurrentRate;
            var severity = entities.GetComponent<WoundComponent>(artery).Severity;

            Assert.Multiple(() =>
            {
                Assert.That(openRate, Is.GreaterThan(0f));
                Assert.That(bleed.AutomaticClottingAt, Is.Null,
                    "`clottingMultiplier: 0` means an artery never gets a clotting deadline.");
                Assert.That(wounds.TreatWound(artery, FixedPoint2.New(5)), Is.False,
                    "nothing closes it while it is still pumping.");
            });

            var gauze = entities.SpawnEntity("Gauze1", map.GridCoords);
            Assert.That(healing.TryApplyHealing(body, leg,
                (gauze, entities.GetComponent<HealingComponent>(gauze)), body, out _, out _), Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(bleed.Treatment, Is.EqualTo(BleedingTreatment.Bandaged));
                Assert.That(bleed.CurrentRate, Is.GreaterThan(0f).And.LessThan(openRate),
                    "a dressing slows an artery, it does not stop it.");
                Assert.That(bleed.BleedingSeverity, Is.EqualTo(severity),
                    "and it never takes the wound's bleeding severity.");
                Assert.That(entities.GetComponent<WoundComponent>(artery).Severity, Is.EqualTo(severity),
                    "gauze removes Slash damage, and that path is refused too.");
            });

            Assert.That(tourniquet.Apply(body, leg), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(bleed.Treatment, Is.EqualTo(BleedingTreatment.Clamped));
                Assert.That(bleed.CurrentRate, Is.EqualTo(0f));
                Assert.That(wounds.TreatWound(artery, FixedPoint2.New(5)), Is.True,
                    "with the flow stopped, sutures and the surgery's second step can close it.");
            });
        });
    }

    /// <summary>An artery in the torso has nothing to tie around; the ordinary cut beside it still has.</summary>
    [Test]
    public async Task TorsoArteryCannotBeTourniquetedTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var wounds = entities.System<WoundSystem>();
            var traits = entities.System<WolfmedWoundTraitSystem>();
            var tourniquet = entities.System<TourniquetSystem>();

            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            Slash(entities, body, TargetBodyPart.Torso, 25);
            Slash(entities, body, TargetBodyPart.LeftArm, 25);

            var torso = Part(entities, body, BodyPartType.Torso, BodyPartSymmetry.None);
            var arm = Part(entities, body, BodyPartType.Arm, BodyPartSymmetry.Left);
            var torsoArtery = FindWound(entities, wounds, torso, "WFWolfmedArterialBleedWound");
            var armArtery = FindWound(entities, wounds, arm, "WFWolfmedArterialBleedWound");

            Assert.Multiple(() =>
            {
                Assert.That(traits.CanTourniquet(torsoArtery, torso), Is.False);
                Assert.That(traits.CanTourniquet(armArtery, arm), Is.True);
            });

            // The torso cut bleeds too, so the tourniquet does something - just not to the artery.
            Assert.That(tourniquet.Apply(body, torso), Is.True);
            var torsoBleed = entities.GetComponent<WoundBleedingComponent>(torsoArtery);
            Assert.Multiple(() =>
            {
                Assert.That(torsoBleed.Treatment, Is.EqualTo(BleedingTreatment.None));
                Assert.That(torsoBleed.CurrentRate, Is.GreaterThan(0f));
                Assert.That(traits.CanTourniquetPart(torso), Is.False,
                    "with the cut clamped, the only bleed left here is one a tourniquet cannot reach.");
                Assert.That(tourniquet.Apply(body, torso), Is.False, "so a second one achieves nothing.");
            });
        });
    }

    /// <summary>
    /// A cut tendon costs the limb its use through the same path a fracture does: a limp in a leg, slower
    /// hands in an arm. Repairing the wound hands both back.
    /// </summary>
    [Test]
    public async Task TendonCutPenaltiesApplyAndClearTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var wounds = entities.System<WoundSystem>();
            var traits = entities.System<WolfmedWoundTraitSystem>();
            var effects = entities.System<FractureEffectSystem>();
            var movement = entities.System<MovementSpeedModifierSystem>();

            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var speed = entities.GetComponent<MovementSpeedModifierComponent>(body);
            movement.RefreshMovementSpeedModifiers(body);
            var walkBefore = speed.WalkSpeedModifier;

            Slash(entities, body, TargetBodyPart.LeftLeg, 25);
            var leg = Part(entities, body, BodyPartType.Leg, BodyPartSymmetry.Left);
            var legTendon = FindWound(entities, wounds, leg, "WFWolfmedTendonCutWound");

            Assert.Multiple(() =>
            {
                Assert.That(traits.TryGetLimbPenalty(leg, true, out var legModifier), Is.True);
                Assert.That(legModifier, Is.EqualTo(0.7f));
                Assert.That(speed.WalkSpeedModifier, Is.LessThan(walkBefore),
                    "a cut tendon in a leg limps, through the same modifier a fracture uses.");
            });

            var walkWithLeg = speed.WalkSpeedModifier;
            Slash(entities, body, TargetBodyPart.RightArm, 25);
            var arm = Part(entities, body, BodyPartType.Arm, BodyPartSymmetry.Right);
            var armTendon = FindWound(entities, wounds, arm, "WFWolfmedTendonCutWound");

            Assert.Multiple(() =>
            {
                Assert.That(traits.TryGetLimbPenalty(arm, false, out var armModifier), Is.True);
                Assert.That(armModifier, Is.EqualTo(1.5f));
                Assert.That(effects.GetDurationMultiplier(body), Is.GreaterThan(1f),
                    "and one in an arm slows everything that hand does.");
                Assert.That(speed.WalkSpeedModifier, Is.EqualTo(walkWithLeg).Within(0.001),
                    "an arm is not a mobility part.");
            });

            // A tendon is mechanical damage: the surgery step is the only thing that reaches it.
            Assert.That(wounds.TreatWound(legTendon, FixedPoint2.MaxValue), Is.True);
            Assert.That(wounds.TreatWound(armTendon, FixedPoint2.MaxValue), Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(traits.TryGetLimbPenalty(leg, true, out _), Is.False);
                Assert.That(traits.TryGetLimbPenalty(arm, false, out _), Is.False);
                Assert.That(speed.WalkSpeedModifier, Is.EqualTo(walkBefore).Within(0.001));
                Assert.That(effects.GetDurationMultiplier(body), Is.EqualTo(1f).Within(0.001));
            });
        });
    }

    /// <summary>Gauze and damage removal never mend a tendon, however much of either is used.</summary>
    [Test]
    public async Task TendonCutIgnoresTopicalsTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var wounds = entities.System<WoundSystem>();
            var healing = entities.System<WoundHealingSystem>();

            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            Slash(entities, body, TargetBodyPart.LeftLeg, 25);

            var leg = Part(entities, body, BodyPartType.Leg, BodyPartSymmetry.Left);
            var tendon = FindWound(entities, wounds, leg, "WFWolfmedTendonCutWound");
            var severity = entities.GetComponent<WoundComponent>(tendon).Severity;

            foreach (var item in new[] { "Gauze1", "BrutepackAdvanced1" })
            {
                var topical = entities.SpawnEntity(item, map.GridCoords);
                healing.TryApplyHealing(body, leg,
                    (topical, entities.GetComponent<HealingComponent>(topical)), body, out _, out _);
            }

            Assert.That(entities.GetComponent<WoundComponent>(tendon).Severity, Is.EqualTo(severity),
                "`healingMultiplier: 0` keeps every topical out of it.");
        });
    }

    /// <summary>An animal's unarmed attack is a bite, and a bite tears rather than cuts.</summary>
    [Test]
    public async Task BiteMakesAnAvulsionTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var wounds = entities.System<WoundSystem>();
            var rules = entities.System<WolfmedWoundRuleSystem>();
            var traits = entities.System<WolfmedWoundTraitSystem>();

            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var animal = entities.SpawnEntity("MobMouse", map.GridCoords);

            Assert.That(rules.GetCause(animal, animal, false),
                Is.EqualTo(WolfmedWoundCause.Unarmed | WolfmedWoundCause.Bite),
                "the component declares Bite on top of the unarmed attack the tool check derives.");

            entities.System<DamageableSystem>().TryChangeDamage(body, Spec("Slash", 12),
                origin: animal, targetPart: TargetBodyPart.RightArm, tool: animal);

            var arm = Part(entities, body, BodyPartType.Arm, BodyPartSymmetry.Right);
            Assert.That(Prototypes(entities, wounds, arm), Is.EquivalentTo(new[] { "WFWolfmedAvulsionWound" }),
                "a bite replaces the cut rather than adding to it.");

            var avulsion = FindWound(entities, wounds, arm, "WFWolfmedAvulsionWound");
            Assert.Multiple(() =>
            {
                Assert.That(traits.GetInfectionRisk(avulsion), Is.EqualTo(2.5f),
                    "the field W5's infection timer reads.");
                Assert.That(traits.GetPartInfectionRisk(arm), Is.EqualTo(2.5f));
            });

            // A knife cut on the same body is an ordinary wound at ordinary risk.
            var cut = entities.SpawnEntity("MobHuman", map.GridCoords);
            Slash(entities, cut, TargetBodyPart.LeftArm, 12);
            var cutArm = Part(entities, cut, BodyPartType.Arm, BodyPartSymmetry.Left);
            Assert.That(traits.GetInfectionRisk(FindWound(entities, wounds, cutArm, "SlashWound")),
                Is.EqualTo(1f), "and everything that says nothing is ordinary risk.");
        });
    }

    /// <summary>The analyzer names the three wounds, and the two repair surgeries exist.</summary>
    [Test]
    public async Task NamesAndSurgeriesExistTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var locale = server.ResolveDependency<ILocalizationManager>();
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var analyzer = entities.System<Content.Server.Medical.HealthAnalyzerSystem>();
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            Slash(entities, body, TargetBodyPart.LeftArm, 25);

            var diagnostics = analyzer.BuildWoundDiagnostics(body);
            Assert.That(diagnostics, Is.Not.Null);
            Assert.That(diagnostics!.Parts.TryGetValue(TargetBodyPart.LeftArm, out var arm));

            Assert.Multiple(() =>
            {
                Assert.That(arm.VisibleWounds.Any(wound => wound.Name == "wolfmed-wound-name-arterial-bleed"));
                Assert.That(arm.VisibleWounds.Any(wound => wound.Name == "wolfmed-wound-name-tendon-cut"));
                Assert.That(locale.HasString("wolfmed-wound-name-arterial-bleed"));
                Assert.That(locale.HasString("wolfmed-wound-name-tendon-cut"));
                Assert.That(locale.HasString("wolfmed-wound-name-avulsion"));
                Assert.That(locale.HasString("wolfmed-tourniquet-nowhere-to-tie"));
                Assert.That(prototypes.HasIndex<EntityPrototype>("WFSurgeryRepairArtery"));
                Assert.That(prototypes.HasIndex<EntityPrototype>("WFSurgeryRepairTendon"));
            });
        });
    }

    private static void Slash(IEntityManager entities, EntityUid body, TargetBodyPart target, int amount)
    {
        entities.System<DamageableSystem>().TryChangeDamage(body, Spec("Slash", amount),
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
            .Single(wound => wound.Comp.Prototype == new ProtoId<WoundPrototype>(prototype))
            .Owner;
    }

    private static DamageSpecifier Spec(string type, int amount) => new()
    {
        DamageDict = { [new ProtoId<DamageTypePrototype>(type)] = FixedPoint2.New(amount) },
    };
}
