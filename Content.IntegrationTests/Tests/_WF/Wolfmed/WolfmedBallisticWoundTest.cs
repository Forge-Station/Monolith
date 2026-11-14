#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server._WF.Wolfmed.Wounds;
using Content.Shared._Onyx.Targeting;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Wolfmed.Compat;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// W1: which wound a hit makes depends on what fired it, and what a lodged round leaves behind.
/// </summary>
/// <remarks>
/// The whole mapping is data: <c>wolfmedWoundRule</c> prototypes matched against the hit's damage type,
/// per-hit size and causes. Causes come from the hit's tool, which is the projectile for gunfire and the
/// swung weapon for melee, so these tests drive the real damage pipeline with a real projectile entity
/// rather than calling the rule system directly.
/// </remarks>
[TestFixture]
[TestOf(typeof(WolfmedWoundRuleSystem))]
public sealed class WolfmedBallisticWoundTest : GameTest
{
    /// <summary>A round that barely caught the limb grazes it; a rifle-weight one stays in.</summary>
    [Test]
    public async Task ProjectileBandsPickTheWoundTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        // Lodging is a roll now; the heavy band below asserts the lodged outcome, so the roll is forced to land.
        await server.WaitPost(() => server.System<WolfmedWoundRuleSystem>().ForcedRoll = 0f);
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var wounds = entities.System<WoundSystem>();
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var bullet = entities.SpawnEntity("BulletMinigun", map.GridCoords);

            Hit(entities, body, TargetBodyPart.LeftArm, bullet, 5);
            Hit(entities, body, TargetBodyPart.RightArm, bullet, 25);

            var grazed = Part(entities, body, BodyPartType.Arm, BodyPartSymmetry.Left);
            var shot = Part(entities, body, BodyPartType.Arm, BodyPartSymmetry.Right);

            Assert.Multiple(() =>
            {
                Assert.That(Prototypes(entities, wounds, grazed), Is.EquivalentTo(new[] { "WFWolfmedGrazeWound" }),
                    "a 5 point hit grazes, and replaces the default puncture rather than adding to it.");
                Assert.That(Prototypes(entities, wounds, shot), Is.EquivalentTo(new[] { "WFWolfmedLodgedRoundWound" }));
            });

            var lodged = FindWound(entities, wounds, shot, "WFWolfmedLodgedRoundWound");
            Assert.That(entities.TryGetComponent(lodged, out WolfmedEmbeddedObjectComponent? embedded));
            Assert.Multiple(() =>
            {
                Assert.That(embedded!.Count, Is.EqualTo(1));
                Assert.That(embedded.Item.Id, Is.EqualTo("WFWolfmedSpentRound"));
            });
        });
        await server.WaitPost(() => server.System<WolfmedWoundRuleSystem>().ForcedRoll = null);
    }

    /// <summary>
    /// The middle band is the through-and-through one: a fifth of those rounds lodge instead, and nothing
    /// in the band ever leaves Onyx's plain puncture.
    /// </summary>
    [Test]
    public async Task MidBandGoesThroughOrLodgesTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var wounds = entities.System<WoundSystem>();
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var bullet = entities.SpawnEntity("BulletMinigun", map.GridCoords);

            // 12 hits of 12 on the torso: under its 250 damage cap, and a run of twelve that never goes
            // through would be a 1 in 200 million roll.
            for (var i = 0; i < 12; i++)
                Hit(entities, body, TargetBodyPart.Torso, bullet, 12);

            var torso = Part(entities, body, BodyPartType.Torso, BodyPartSymmetry.None);
            var found = Prototypes(entities, wounds, torso);
            Assert.Multiple(() =>
            {
                Assert.That(found, Does.Contain("WFWolfmedGunshotWound"));
                Assert.That(found, Does.Not.Contain("PiercingWound"));
                Assert.That(found.All(id =>
                        id is "WFWolfmedGunshotWound" or "WFWolfmedLodgedRoundWound" or "InternalBleedingWound"),
                    Is.True, $"unexpected wounds: {string.Join(", ", found)}");
            });
        });
    }

    /// <summary>A spear is not a gun: melee Piercing keeps making Onyx's puncture.</summary>
    [Test]
    public async Task MeleePiercingIsNotBallisticTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var wounds = entities.System<WoundSystem>();
            var rules = entities.System<WolfmedWoundRuleSystem>();
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var attacker = entities.SpawnEntity("MobHuman", map.GridCoords);
            var spear = entities.SpawnEntity("Spear", map.GridCoords);

            Assert.That(rules.GetCause(attacker, spear, false), Is.EqualTo(WolfmedWoundCause.Melee));
            Assert.That(rules.GetCause(attacker, attacker, false), Is.EqualTo(WolfmedWoundCause.Unarmed));
            Assert.That(rules.GetCause(null, null, false), Is.EqualTo(WolfmedWoundCause.Environmental));

            entities.System<DamageableSystem>().TryChangeDamage(body, Spec("Piercing", 25),
                origin: attacker, targetPart: TargetBodyPart.LeftLeg, tool: spear);

            var leg = Part(entities, body, BodyPartType.Leg, BodyPartSymmetry.Left);
            Assert.That(Prototypes(entities, wounds, leg), Does.Contain("PiercingWound"));
            Assert.That(Prototypes(entities, wounds, leg),
                Does.Not.Contain("WFWolfmedLodgedRoundWound").And.Not.Contain("WFWolfmedGunshotWound"));
        });
    }

    /// <summary>
    /// P6: a beam is its own cause. Railgun and coilgun fire is Piercing, so it makes a gunshot wound that
    /// never lodges a round; a laser is Heat and never touches the ballistic rules at all.
    /// </summary>
    [Test]
    public async Task HitscanIsItsOwnCauseTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var wounds = entities.System<WoundSystem>();
            var rules = entities.System<WolfmedWoundRuleSystem>();
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var gun = entities.SpawnEntity("MobHuman", map.GridCoords);
            var slug = entities.SpawnEntity("Magnum45", map.GridCoords); // Piercing 35, railgun-class.
            var beam = entities.SpawnEntity("RedLaser", map.GridCoords); // Heat 18.

            Assert.Multiple(() =>
            {
                Assert.That(rules.GetCause(gun, slug, false), Is.EqualTo(WolfmedWoundCause.Hitscan));
                Assert.That(rules.GetCause(gun, beam, false), Is.EqualTo(WolfmedWoundCause.Hitscan));
            });

            // 25 Piercing is well past WFWolfmedRuleLodgedRoundHeavy's 18 floor; only the Projectile causes
            // reach that rule, so the hit must come out as a clean through-and-through.
            Hit(entities, body, TargetBodyPart.LeftArm, slug, 25);
            var arm = Part(entities, body, BodyPartType.Arm, BodyPartSymmetry.Left);

            entities.System<DamageableSystem>().TryChangeDamage(body, Spec("Heat", 25),
                origin: gun, targetPart: TargetBodyPart.RightArm, tool: beam);
            var burned = Part(entities, body, BodyPartType.Arm, BodyPartSymmetry.Right);

            Assert.Multiple(() =>
            {
                Assert.That(Prototypes(entities, wounds, arm), Does.Contain("WFWolfmedGunshotWound"));
                Assert.That(Prototypes(entities, wounds, arm), Does.Not.Contain("WFWolfmedLodgedRoundWound"));
                Assert.That(Prototypes(entities, wounds, burned), Does.Not.Contain("WFWolfmedGunshotWound"));
            });
        });
    }

    /// <summary>Blasts and buckshot both leave fragments, by the same rule.</summary>
    [Test]
    public async Task ExplosionsAndBuckshotMakeShrapnelTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var wounds = entities.System<WoundSystem>();
            var routing = entities.System<WoundDamageRoutingSystem>();
            var embedded = entities.System<WolfmedEmbeddedObjectSystem>();

            var blasted = entities.SpawnEntity("MobHuman", map.GridCoords);
            Assert.That(routing.TryApplyDistributedDamage(blasted, Spec("Piercing", 40), TargetBodyPart.All,
                DamageDistribution.SplitEvenly, isExplosion: true));

            var torso = Part(entities, blasted, BodyPartType.Torso, BodyPartSymmetry.None);
            Assert.Multiple(() =>
            {
                Assert.That(Prototypes(entities, wounds, torso), Does.Contain("WFWolfmedShrapnelWound"));
                Assert.That(Prototypes(entities, wounds, torso), Does.Not.Contain("PiercingWound"));
                Assert.That(embedded.GetPartCount(torso), Is.GreaterThan(0));
            });

            // A buckshot pellet declares Fragment on the prototype and matches the same rule.
            var shot = entities.SpawnEntity("MobHuman", map.GridCoords);
            var pellet = entities.SpawnEntity("Pellet12_gauge", map.GridCoords);
            Hit(entities, shot, TargetBodyPart.RightLeg, pellet, 11);

            var leg = Part(entities, shot, BodyPartType.Leg, BodyPartSymmetry.Right);
            Assert.That(Prototypes(entities, wounds, leg), Is.EquivalentTo(new[] { "WFWolfmedShrapnelWound" }));
        });
    }

    /// <summary>
    /// A round still in the limb keeps it open: it never clots on its own and refuses every treatment
    /// until it is out. Taking it out hands both back.
    /// </summary>
    [Test]
    public async Task LodgedRoundBleedsAndBlocksTreatmentTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        // Lodging is a roll now; this test is about what a lodged round does, so the roll is forced to land.
        await server.WaitPost(() => server.System<WolfmedWoundRuleSystem>().ForcedRoll = 0f);
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var wounds = entities.System<WoundSystem>();
            var bleeding = entities.System<WoundBleedingSystem>();
            var removal = entities.System<WolfmedEmbeddedRemovalSystem>();

            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var bullet = entities.SpawnEntity("BulletMinigun", map.GridCoords);
            Hit(entities, body, TargetBodyPart.LeftArm, bullet, 25);

            var arm = Part(entities, body, BodyPartType.Arm, BodyPartSymmetry.Left);
            var wound = FindWound(entities, wounds, arm, "WFWolfmedLodgedRoundWound");
            var severity = entities.GetComponent<WoundComponent>(wound).Severity;

            Assert.Multiple(() =>
            {
                Assert.That(bleeding.GetPartRate(arm), Is.GreaterThan(0f));
                Assert.That(entities.GetComponent<WoundBleedingComponent>(wound).AutomaticClottingAt, Is.Null,
                    "`clottingMultiplier: 0` means the bleed has no clotting deadline at all.");
                Assert.That(wounds.TreatWound(wound, FixedPoint2.New(5)), Is.False,
                    "sutures, gauze and surgery all go through TreatWound, and all are refused.");
                Assert.That(entities.GetComponent<WoundComponent>(wound).Severity, Is.EqualTo(severity));
            });

            var host = (body, entities.GetComponent<WoundHostComponent>(body));
            var item = removal.TryRemoveOne(host, wound, body, clean: true);
            Assert.That(item, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(entities.GetComponent<MetaDataComponent>(item!.Value).EntityPrototype?.ID,
                    Is.EqualTo("WFWolfmedSpentRound"));
                Assert.That(entities.HasComponent<WolfmedEmbeddedObjectComponent>(wound), Is.False,
                    "an empty wound drops the component, so it stops blocking anything.");
                Assert.That(wounds.TreatWound(wound, FixedPoint2.New(5)), Is.True);
            });

            Assert.That(removal.TryRemoveOne(host, wound, body, clean: true), Is.Null,
                "there is nothing left to pull out.");
        });
        await server.WaitPost(() => server.System<WolfmedWoundRuleSystem>().ForcedRoll = null);
    }

    /// <summary>Digging with a knife costs a fresh cut and pain; forceps do not. Both are found by the tool check.</summary>
    [Test]
    public async Task SharpToolRemovalHurtsTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        // Lodging is a roll now; this test is about what a lodged round does, so the roll is forced to land.
        await server.WaitPost(() => server.System<WolfmedWoundRuleSystem>().ForcedRoll = 0f);
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var wounds = entities.System<WoundSystem>();
            var pain = entities.System<PainSystem>();
            var removal = entities.System<WolfmedEmbeddedRemovalSystem>();

            var knife = entities.SpawnEntity("KitchenKnife", map.GridCoords);
            var hemostat = entities.SpawnEntity("Hemostat", map.GridCoords);
            var wrench = entities.SpawnEntity("Wrench", map.GridCoords);
            Assert.Multiple(() =>
            {
                Assert.That(removal.TryGetTool(hemostat, out var cleanTool), Is.True);
                Assert.That(cleanTool, Is.True, "a hemostat is the clean way to do this.");
                Assert.That(removal.TryGetTool(knife, out var knifeClean), Is.True);
                Assert.That(knifeClean, Is.False);
                Assert.That(removal.TryGetTool(wrench, out _), Is.False, "a wrench is not sharp.");
            });

            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var bullet = entities.SpawnEntity("BulletMinigun", map.GridCoords);
            Hit(entities, body, TargetBodyPart.LeftArm, bullet, 25);

            var arm = Part(entities, body, BodyPartType.Arm, BodyPartSymmetry.Left);
            var wound = FindWound(entities, wounds, arm, "WFWolfmedLodgedRoundWound");
            var painBefore = pain.GetPain(arm);

            var host = (body, entities.GetComponent<WoundHostComponent>(body));
            Assert.That(removal.TryRemoveOne(host, wound, body, clean: false), Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(Prototypes(entities, wounds, arm), Does.Contain("SlashWound"),
                    "the knife leaves a cut of its own.");
                Assert.That(pain.GetPain(arm), Is.GreaterThan(painBefore));
            });
        });
        await server.WaitPost(() => server.System<WolfmedWoundRuleSystem>().ForcedRoll = null);
    }

    /// <summary>
    /// The two ways a removal can end badly. An item id that is not a prototype must not eat the fragment
    /// on the way to spawning nothing, and a limb that came off during the do-after must not be dug into.
    /// </summary>
    [Test]
    public async Task RemovalRefusesBadIdAndDetachedLimbTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        // Lodging is a roll now; this test is about what a lodged round does, so the roll is forced to land.
        await server.WaitPost(() => server.System<WolfmedWoundRuleSystem>().ForcedRoll = 0f);
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var wounds = entities.System<WoundSystem>();
            var pain = entities.System<PainSystem>();
            var removal = entities.System<WolfmedEmbeddedRemovalSystem>();

            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var bullet = entities.SpawnEntity("BulletMinigun", map.GridCoords);
            Hit(entities, body, TargetBodyPart.LeftArm, bullet, 25);

            var arm = Part(entities, body, BodyPartType.Arm, BodyPartSymmetry.Left);
            var wound = FindWound(entities, wounds, arm, "WFWolfmedLodgedRoundWound");
            var host = (body, entities.GetComponent<WoundHostComponent>(body));
            var embedded = entities.GetComponent<WolfmedEmbeddedObjectComponent>(wound);
            var count = embedded.Count;
            Assert.That(count, Is.GreaterThan(0));

            embedded.Item = "WolfmedNoSuchItemPrototype";
            Assert.That(removal.TryRemoveOne(host, wound, body, clean: true), Is.Null);
            Assert.That(entities.GetComponent<WolfmedEmbeddedObjectComponent>(wound).Count, Is.EqualTo(count),
                "a bad id is refused before the fragment is consumed.");

            embedded.Item = "WFWolfmedSpentRound";
            var painBefore = pain.GetPain(arm);
            Assert.That(entities.System<WolfmedBodySystem>().TryDetachPart(arm), Is.True);

            Assert.That(removal.TryRemoveOne(host, wound, body, clean: false), Is.Null,
                "the arm is on the floor; nothing comes out of the patient.");
            Assert.Multiple(() =>
            {
                Assert.That(entities.GetComponent<WolfmedEmbeddedObjectComponent>(wound).Count, Is.EqualTo(count));
                Assert.That(pain.GetPain(arm), Is.EqualTo(painBefore),
                    "and a severed limb is not charged the knife's pain.");
            });
        });
        await server.WaitPost(() => server.System<WolfmedWoundRuleSystem>().ForcedRoll = null);
    }

    /// <summary>The analyzer says how many objects are still in the part, by name.</summary>
    [Test]
    public async Task AnalyzerReportsEmbeddedObjectsTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        // Lodging is a roll now; this test is about what a lodged round does, so the roll is forced to land.
        await server.WaitPost(() => server.System<WolfmedWoundRuleSystem>().ForcedRoll = 0f);
        var entities = server.ResolveDependency<IEntityManager>();
        var locale = server.ResolveDependency<ILocalizationManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var analyzer = entities.System<Content.Server.Medical.HealthAnalyzerSystem>();
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var bullet = entities.SpawnEntity("BulletMinigun", map.GridCoords);
            Hit(entities, body, TargetBodyPart.LeftArm, bullet, 25);

            var diagnostics = analyzer.BuildWoundDiagnostics(body);
            Assert.That(diagnostics, Is.Not.Null);
            Assert.That(diagnostics!.Parts.TryGetValue(TargetBodyPart.LeftArm, out var arm));
            Assert.Multiple(() =>
            {
                Assert.That(arm.EmbeddedObjects, Is.EqualTo(1));
                Assert.That(arm.VisibleWounds.Any(wound => wound.Name == "wolfmed-wound-name-lodged-round"));
                Assert.That(locale.HasString("wolfmed-wound-name-lodged-round"));
                Assert.That(locale.HasString("wolfmed-wound-name-gunshot"));
                Assert.That(locale.HasString("wolfmed-wound-name-graze"));
                Assert.That(locale.HasString("wolfmed-wound-name-shrapnel"));
                Assert.That(locale.HasString("health-analyzer-wound-embedded-short"));
            });
        });
        await server.WaitPost(() => server.System<WolfmedWoundRuleSystem>().ForcedRoll = null);
    }

    /// <summary>
    /// A lodged round with nothing lodged in it is an ordinary gunshot wound. It used to stay in the analyzer
    /// list as "lodged round" after the last round came out. Lodging itself is a roll: the same heavy hit
    /// goes straight through when the roll misses.
    /// </summary>
    [Test]
    public async Task ClearedLodgedRoundBecomesAGunshotWoundTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var wounds = entities.System<WoundSystem>();
            var rules = entities.System<WolfmedWoundRuleSystem>();
            var removal = entities.System<WolfmedEmbeddedRemovalSystem>();
            try
            {
                // The roll misses: a heavy round goes through.
                rules.ForcedRoll = 0.99f;
                var lucky = entities.SpawnEntity("MobHuman", map.GridCoords);
                Hit(entities, lucky, TargetBodyPart.LeftArm, entities.SpawnEntity("BulletMinigun", map.GridCoords), 25);
                var luckyArm = Part(entities, lucky, BodyPartType.Arm, BodyPartSymmetry.Left);
                Assert.That(Prototypes(entities, wounds, luckyArm), Does.Contain("WFWolfmedGunshotWound"));
                Assert.That(Prototypes(entities, wounds, luckyArm), Does.Not.Contain("WFWolfmedLodgedRoundWound"));

                // The roll lands: it stays in.
                rules.ForcedRoll = 0f;
                var body = entities.SpawnEntity("MobHuman", map.GridCoords);
                Hit(entities, body, TargetBodyPart.LeftArm, entities.SpawnEntity("BulletMinigun", map.GridCoords), 25);
                var arm = Part(entities, body, BodyPartType.Arm, BodyPartSymmetry.Left);
                var wound = FindWound(entities, wounds, arm, "WFWolfmedLodgedRoundWound");
                var severity = entities.GetComponent<WoundComponent>(wound).Severity;

                var host = (body, entities.GetComponent<WoundHostComponent>(body));
                Assert.That(removal.TryRemoveOne(host, wound, body, clean: true), Is.Not.Null);

                Assert.Multiple(() =>
                {
                    Assert.That(Prototypes(entities, wounds, arm), Does.Not.Contain("WFWolfmedLodgedRoundWound"),
                        "nothing is lodged any more.");
                    Assert.That(Prototypes(entities, wounds, arm), Does.Contain("WFWolfmedGunshotWound"));
                    Assert.That(entities.GetComponent<WoundComponent>(FindWound(entities, wounds, arm, "WFWolfmedGunshotWound")).Severity,
                        Is.EqualTo(severity), "the hole is as bad as it was.");
                });
            }
            finally
            {
                rules.ForcedRoll = null;
            }
        });
    }

    /// <summary>
    /// Sutures close a gunshot wound in a sane number of uses. The wound's healingMultiplier (how much removed
    /// damage takes off it) was also applied to items working on the wound directly, so a suture closed three
    /// severity a time and a critical wound outlasted twenty of them.
    /// </summary>
    [Test]
    public async Task SuturesCloseACriticalGunshotWoundTest()
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
            var torso = Part(entities, body, BodyPartType.Torso, BodyPartSymmetry.None);

            // The wound alone, with no damage left on the part: the state a patient is in once the damage has been
            // treated and only the hole remains.
            Assert.That(wounds.CreateOrMergeWound(torso, "WFWolfmedGunshotWound", 100), Is.Not.Null);

            var suture = entities.SpawnEntity("MedicatedSuture", map.GridCoords);
            var item = (suture, entities.GetComponent<Content.Server.Medical.Components.HealingComponent>(suture));
            var uses = 0;
            while (uses < 30 && healing.TryApplyHealing(body, torso, item, body, out _, out _))
                uses++;

            Assert.Multiple(() =>
            {
                Assert.That(Prototypes(entities, wounds, torso), Does.Not.Contain("WFWolfmedGunshotWound"));
                Assert.That(uses, Is.LessThanOrEqualTo(8), "a critical wound is a handful of sutures, not a box of them.");
            });
        });
    }

    private static void Hit(IEntityManager entities, EntityUid body, TargetBodyPart target, EntityUid tool, int piercing)
    {
        entities.System<DamageableSystem>().TryChangeDamage(body, Spec("Piercing", piercing),
            origin: null, targetPart: target, tool: tool);
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
