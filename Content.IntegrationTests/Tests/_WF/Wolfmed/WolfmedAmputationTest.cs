using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Shared._Onyx.Targeting; // WOLFGATE: DamageDistribution.
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Wolfmed.Compat; // WOLFGATE: TryDetachPart + the D12 damage facade.
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Configuration;
using Content.Shared._WF.Wolfmed.CCVar;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// Wolfgate-specific amputation behaviour: the P3-D1 vital-loss charge, the DECISIONS.md §8.6-1 gun/laser
/// balance data, the data-gated overflow mechanism, the explosion branch and repeated amputations.
/// </summary>
/// <remarks>
/// PLAN3 §6.2 T-AMP-VITAL, T-AMP-GUN (which REPLACES PLAN3's T-AMP-NOGUN per DECISIONS.md §8.6-1),
/// T-AMP-OVERFLOW, T-AMP-EXPLOSION and T-AMP-CONSEQUENCE-SEPARATE. Every expected value is derived from
/// shipped prototype data at its assertion; the hit counts were cross-checked against WP11-1's live
/// measurements on a real MobHuman.
/// </remarks>
[TestFixture]
[TestOf(typeof(AmputationSystem))]
public sealed class WolfmedAmputationTest : GameTest
{
    // WOLFGATE: Shitmed body graphs (PLAN3 §6.1 trap 9); Onyx's part-level datafields move to
    // `- type: WolfmedBodyPart` (D8). WolfmedAmputationTorso carries amputationConsequenceSeverity: 50
    // for the same reason AmputationConsequenceTest's torso does - 35 is both the component default and
    // the WolfmedBodyPartSystem.Get fallback, so only a non-default value can prove the severity is read
    // off the parent stump (PLAN3 §8.7 hazard 7).
    //
    // WolfmedAmputationOverflowPart is the only part in these tests with a nonzero maxDamage. Every
    // shipped limb has maxDamage 0 (only WFWolfmedBaseTorso sets one, and the torso is excluded from
    // amputation outright), which is exactly what T-AMP-OVERFLOW measures.
    [TestPrototypes]
    private const string Prototypes = @"
- type: body
  id: WolfmedAmputationBodyGraph
  name: ""wolfmed amputation body""
  root: torso
  slots:
    torso:
      part: WolfmedAmputationTorso
      connections:
      - head
      - left arm
      - right arm
    head:
      part: HeadHuman
    left arm:
      part: LeftArmHuman
    right arm:
      part: RightArmHuman

- type: entity
  id: WolfmedAmputationBody
  parent: InventoryBase
  components:
  - type: Body
    prototype: WolfmedAmputationBodyGraph
  - type: Damageable
    damageContainer: Biological
  - type: WoundHost

- type: entity
  id: WolfmedAmputationTorso
  parent: TorsoHuman
  components:
  - type: WolfmedBodyPart
    amputationConsequenceSeverity: 50

- type: body
  id: WolfmedAmputationOverflowGraph
  name: ""wolfmed amputation overflow body""
  root: torso
  slots:
    torso:
      part: TorsoHuman
      connections:
      - left arm
    left arm:
      part: WolfmedAmputationOverflowPart

- type: entity
  id: WolfmedAmputationOverflowBody
  parent: InventoryBase
  components:
  - type: Body
    prototype: WolfmedAmputationOverflowGraph
  - type: Damageable
    damageContainer: Biological
  - type: WoundHost

- type: entity
  id: WolfmedAmputationOverflowPart
  parent: LeftArmHuman
  components:
  - type: WolfmedBodyPart
    maxDamage: 50
";

    /// <summary>
    /// PLAN3 §6.2 T-AMP-VITAL. (a) a wound host's mob-state readout never drops when a vital part leaves,
    /// and (b) an entity WITHOUT WoundHostComponent is untouched by P3-D1 (D2).
    /// </summary>
    [Test]
    public async Task DecapitationNeverReducesVitalDamageTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            // (a) the wound-host half.
            var body = entities.SpawnEntity("WolfmedAmputationBody", map.GridCoords);
            var graph = entities.System<SharedBodySystem>();
            var routing = entities.System<WoundDamageRoutingSystem>();
            var thresholds = entities.System<MobThresholdSystem>();
            var wfDamage = entities.System<WolfmedDamageableSystem>(); // WOLFGATE: D12
            var damageable = entities.GetComponent<DamageableComponent>(body);
            var head = graph.GetBodyChildren(body)
                .Single(part => part.Component.PartType == BodyPartType.Head).Id;

            // HeadHuman's Slash threshold is 200 (parts.yml/WFWolfmedBaseHead): this arms the head.
            Assert.That(routing.TryApplyPartDamage(body, head, Spec("Slash", 200)));

            // CheckVitalDamage sums the ATTACHED Head + Torso part damage plus SystemicDamageComponent
            // (_Onyx/Mobs/Systems/MobThresholdSystem.cs). Nothing is systemic yet and the torso is clean,
            // so this reads the head's own 200.
            var before = thresholds.CheckVitalDamage(body, damageable);
            Assert.That(before, Is.EqualTo(FixedPoint2.New(200)),
                "the readout before decapitation must be exactly the head's own damage.");
            Assert.That(wfDamage.GetTotalDamage(head), Is.EqualTo(FixedPoint2.New(200)));

            // Slash 15 == DefaultDismembermentFinishingDamage["Slash"] -> the head comes off carrying 215.
            Assert.That(routing.TryApplyPartDamage(body, head, Spec("Slash", 15)));
            Assert.That(graph.BodyHasChild(body, head), Is.False);

            var after = thresholds.CheckVitalDamage(body, damageable);
            var systemic = entities.GetComponent<SystemicDamageComponent>(body);

            Assert.Multiple(() =>
            {
                // THE CONTRACT (B-1): losing a vital part must never make a mob look healthier.
                Assert.That(after, Is.GreaterThanOrEqualTo(before),
                    "decapitation must never reduce the vital-damage readout.");

                // THE DERIVATION. Two charges land in the same RemovePart call and both are systemic
                // Bloodloss, which CheckVitalDamage counts:
                //   * P3-D1's WolfmedBodyPartLifecycleSystem.ChargeVitalPartLoss - the head's own total
                //     at detach, 200 + 15 = 215, so the readout loses nothing when the part leaves;
                //   * Shitmed's SharedBodySystem.PartRemoveDamage - a flat BodyPartComponent.VitalDamage
                //     of 100, which every body in the game already pays for losing its head.
                // 215 + 100 = 315, and the head's 215 no longer counts as an attached part, so the net
                // movement is exactly the finishing hit (15) plus the flat 100. Playtest 5: Bloodloss is bookkeeping
                // the model never reads, held at wolfmed.airloss_cap; the charge still lands, the readout stops at
                // the cap, and the death itself is the life system's (LostVitalPart).
                var cap = FixedPoint2.New(server.ResolveDependency<IConfigurationManager>().GetCVar(WolfmedCVars.AirlossCap));
                Assert.That(systemic.Damage.DamageDict[new ProtoId<DamageTypePrototype>("Bloodloss")],
                    Is.EqualTo(FixedPoint2.Min(FixedPoint2.New(315), cap)));
                Assert.That(after, Is.EqualTo(FixedPoint2.Min(before + FixedPoint2.New(115), cap)));
            });

            // (b) the D2 half: an entity without WoundHostComponent must behave exactly as it did before
            // the port. This is the assertion the withdrawn HOOK 19 (`BaseHead.vitalDamage: 300`) would
            // have failed - BaseHead has 26 descendants and ten of them are not wound hosts.
            var monkey = entities.SpawnEntity("MobMonkey", map.GridCoords);
            Assert.That(entities.HasComponent<WoundHostComponent>(monkey), Is.False,
                "MobMonkey must stay a non-wound-host for this regression guard to mean anything.");

            var monkeyHead = graph.GetBodyChildren(monkey)
                .Single(part => part.Component.PartType == BodyPartType.Head).Id;
            Assert.That(entities.System<WolfmedBodySystem>().TryDetachPart(monkeyHead));

            // HeadMonkey inherits BaseHead -> WFWolfmedBaseHead, so it carries Wolfmed part data, but
            // ChargeVitalPartLoss is only reachable through <WoundHostComponent, BodyPartRemovedEvent>.
            // Only Shitmed's flat VitalDamage 100 is charged, exactly as before phase 3.
            Assert.That(entities.GetComponent<DamageableComponent>(monkey).Damage
                    .DamageDict[new ProtoId<DamageTypePrototype>("Bloodloss")],
                Is.EqualTo(FixedPoint2.New(100)));
        });
    }

    /// <summary>
    /// PLAN3 §6.2 T-AMP-GUN, which REPLACES T-AMP-NOGUN. DECISIONS.md §8.6-1 is a deliberate Wolfgate
    /// balance deviation from Onyx: guns and lasers CAN sever, expressed only in
    /// Resources/Prototypes/_WF/Wolfmed/Body/parts.yml.
    /// </summary>
    [Test]
    public async Task GunsAndLasersAmputateOverThresholdLimbsTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            // M1b: a shooter behind every round. Damage with no origin is ambient harm and stops at a hand's
            // ceiling (0.8 x 150 = 120), short of these thresholds.
            var shooter = entities.SpawnEntity("MobHuman", map.GridCoords);
            Assert.That(entities.HasComponent<WoundHostComponent>(body), Is.True,
                "MobHuman is not a wound host; the D21/D32 species wiring is missing.");

            var graph = entities.System<SharedBodySystem>();
            var routing = entities.System<WoundDamageRoutingSystem>();
            var parts = graph.GetBodyChildren(body).ToList();
            var leftHand = Find(parts, BodyPartType.Hand, BodyPartSymmetry.Left);
            var rightHand = Find(parts, BodyPartType.Hand, BodyPartSymmetry.Right);
            var leftFoot = Find(parts, BodyPartType.Foot, BodyPartSymmetry.Left);
            var rightFoot = Find(parts, BodyPartType.Foot, BodyPartSymmetry.Right);
            var leftArm = Find(parts, BodyPartType.Arm, BodyPartSymmetry.Left);

            // WOLFGATE: every accumulation below deliberately stays under Shitmed's own `Destructible`
            // GibPartBehavior triggers, which fire from DamageChangedEvent (i.e. before AmputationSystem
            // ever sees the hit) and would DESTROY the limb instead of amputating it:
            // `MinorLimb` hands/feet Blunt 270 / Slash 180 / Heat 320, `MajorLimb` arms/legs Blunt 400 /
            // Slash 210 / Heat 350 (Resources/Prototypes/Body/Parts/base.yml; M3 raised Blunt and Heat above
            // every sever threshold, P19). Piercing has no trigger at all. The tight case is the Slash arm
            // (peaks at 192 against 210); every detached part is asserted alive below, which is what
            // distinguishes an amputation from a gib.

            // --- BaseBullet: Piercing 14 (projectiles.yml). Hand Piercing threshold 200, per-part
            // dismembermentFinishingDamage Piercing 12 (parts.yml, DECISIONS §8.6-1: lowered from Onyx's
            // host default of 40 precisely so a standard round can finish an over-threshold limb).
            var leftHandWoundable = entities.GetComponent<WoundableComponent>(leftHand);
            for (var i = 0; i < 14; i++)
                Assert.That(routing.TryApplyPartDamage(body, leftHand, Spec("Piercing", 14), shooter));

            Assert.Multiple(() =>
            {
                // 14 * 14 = 196; progress 196/200 = 0.98 < 1.
                Assert.That(leftHandWoundable.Severable, Is.False);
                Assert.That(graph.BodyHasChild(body, leftHand), Is.True);
            });

            // Hit 15 takes the hand to 210 (progress 1.05) and only ARMS it - the threshold hit never
            // detaches.
            Assert.That(routing.TryApplyPartDamage(body, leftHand, Spec("Piercing", 14), shooter));
            Assert.Multiple(() =>
            {
                Assert.That(leftHandWoundable.Severable, Is.True);
                Assert.That(graph.BodyHasChild(body, leftHand), Is.True);
            });

            // ONE more 14-Piercing round finishes it: pre-hit progress 210/200 >= 1 and 14 >= 12.
            Assert.That(routing.TryApplyPartDamage(body, leftHand, Spec("Piercing", 14), shooter));
            Assert.That(graph.BodyHasChild(body, leftHand), Is.False,
                "a limb over its Piercing threshold must be severed by one 14-Piercing round (hit 16).");
            Assert.That(entities.Deleted(leftHand), Is.False, "amputated, not gibbed.");

            // --- BulletLaser: Heat 16 (projectiles.yml). Heat is in NO Onyx amputationThresholds dict, so
            // before DECISIONS §8.6-1 lasers could never sever at any volume; parts.yml now gives every
            // limb a Heat threshold equal to its Piercing one (Hand 200) and a Heat finishing minimum of 15.
            var rightHandWoundable = entities.GetComponent<WoundableComponent>(rightHand);
            for (var i = 0; i < 12; i++)
                Assert.That(routing.TryApplyPartDamage(body, rightHand, Spec("Heat", 16), shooter));

            // 12 * 16 = 192; progress 192/200 = 0.96 < 1.
            Assert.That(rightHandWoundable.Severable, Is.False);

            // Hit 13 -> 208, progress 1.04, arms the hand.
            Assert.That(routing.TryApplyPartDamage(body, rightHand, Spec("Heat", 16), shooter));
            Assert.Multiple(() =>
            {
                Assert.That(rightHandWoundable.Severable, Is.True);
                Assert.That(graph.BodyHasChild(body, rightHand), Is.True);
            });

            // ONE more 16-Heat shot finishes it: 16 >= the Heat finishing minimum of 15 (hit 14).
            Assert.That(routing.TryApplyPartDamage(body, rightHand, Spec("Heat", 16), shooter));
            Assert.That(graph.BodyHasChild(body, rightHand), Is.False,
                "a limb over its Heat threshold must be severed by one 16-Heat laser shot (hit 14).");
            Assert.That(entities.Deleted(rightHand), Is.False, "amputated, not gibbed.");

            // --- A BELOW-threshold limb is severed by neither. Foot Piercing/Heat thresholds are 220.
            for (var i = 0; i < 5; i++)
                Assert.That(routing.TryApplyPartDamage(body, leftFoot, Spec("Piercing", 14), shooter));
            for (var i = 0; i < 3; i++)
                Assert.That(routing.TryApplyPartDamage(body, rightFoot, Spec("Heat", 16), shooter));

            Assert.Multiple(() =>
            {
                // 5 * 14 = 70; progress 70/220 = 0.32.
                Assert.That(entities.GetComponent<WoundableComponent>(leftFoot).Severable, Is.False);
                Assert.That(graph.BodyHasChild(body, leftFoot), Is.True,
                    "five bullets must not take a foot off; the threshold is 220 Piercing.");
                // 3 * 16 = 48; progress 48/220 = 0.22.
                Assert.That(entities.GetComponent<WoundableComponent>(rightFoot).Severable, Is.False);
                Assert.That(graph.BodyHasChild(body, rightFoot), Is.True,
                    "three laser shots must not take a foot off; the threshold is 220 Heat.");
            });

            // --- The melee case is UNCHANGED by DECISIONS §8.6-1: Slash is deliberately absent from the
            // per-part dismembermentFinishingDamage dict, so it still falls back to Onyx's host default (15).
            // (M3 added Blunt 10 on limbs, P19; see StumpTest.) Machete/large-melee baseline is Slash 32 (sword.yml); the arm
            // Slash threshold is 130, so PLAN3 §8.2's table predicts ceil(130/32) + 1 = 6 hits.
            var leftArmWoundable = entities.GetComponent<WoundableComponent>(leftArm);
            for (var i = 0; i < 4; i++)
                Assert.That(routing.TryApplyPartDamage(body, leftArm, Spec("Slash", 32), shooter));

            // 4 * 32 = 128; progress 128/130 = 0.985.
            Assert.That(leftArmWoundable.Severable, Is.False);

            Assert.That(routing.TryApplyPartDamage(body, leftArm, Spec("Slash", 32), shooter)); // 160, arms it
            Assert.Multiple(() =>
            {
                Assert.That(leftArmWoundable.Severable, Is.True);
                Assert.That(graph.BodyHasChild(body, leftArm), Is.True);
            });

            Assert.That(routing.TryApplyPartDamage(body, leftArm, Spec("Slash", 32), shooter)); // 32 >= 15 -> detach
            Assert.That(graph.BodyHasChild(body, leftArm), Is.False,
                "six machete-grade slashes must take an arm off (PLAN3 §8.2).");
            Assert.That(entities.Deleted(leftArm), Is.False, "amputated, not gibbed.");
        });
    }

    /// <summary>
    /// PLAN3 §6.2 T-AMP-OVERFLOW. A canary, not a play-path test: the overflow branch is data-gated on
    /// WolfmedBodyPartComponent.MaxDamage, which no shipped limb sets, so it is inert today (P3-D12).
    /// </summary>
    [Test]
    public async Task OverflowAmputationMechanismIsInertOnShippedLimbsTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            // (a) a real limb: the overflow accumulator never engages, and the THRESHOLD path is what
            // severs.
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            // M1b: an attacker behind every hit. Damage with no origin is ambient harm and stops at the head's
            // ceiling (0.8 x 500 = 400), which is exactly where this test's 16 chunks leave it.
            var attacker = entities.SpawnEntity("MobHuman", map.GridCoords);
            var graph = entities.System<SharedBodySystem>();
            var routing = entities.System<WoundDamageRoutingSystem>();
            var head = graph.GetBodyChildren(body)
                .Single(part => part.Component.PartType == BodyPartType.Head).Id;
            var woundable = entities.GetComponent<WoundableComponent>(head);

            // The chunk size is load-bearing (CRITIQUE3 M3-4): DefaultDismembermentFinishingDamage["Blunt"]
            // is 50, so any chunk >= 50 would make the hit after the threshold a finishing hit and the part
            // would come off immediately; 25 keeps every hit sub-finishing, which is the only way the part
            // can still be attached well past its threshold for the AmputationOverflow assertion to mean
            // anything (once detached, bodyPart.Body == null short-circuits HandlePartDamageApplied).
            //
            // WOLFGATE: the HEAD is used rather than PLAN3's arm because, when this was written, Shitmed's own
            // `Destructible` gibbed a limb before its Blunt amputation threshold was reachable (arm Blunt 190
            // against a threshold of 250). M3 (P19) raised the limb rungs above severing, and limbs now finish
            // on Blunt 10, so a limb would come off on the first chunk past its threshold; the head keeps the
            // host's Blunt 50 minimum, which is what this test needs. `BaseHead`'s own thresholds are Blunt
            // 500 / Slash 600 / Heat 700, and the head's Blunt amputation threshold is 350; this test peaks at 450.
            for (var i = 1; i <= 16; i++)
            {
                Assert.That(routing.TryApplyPartDamage(body, head, Spec("Blunt", 25), attacker));
                // AccumulateAmputationOverflow early-returns because WolfmedBodyPartComponent.MaxDamage is
                // 0 on every shipped limb, so the accumulator can never leave zero.
                Assert.That(woundable.AmputationOverflow, Is.EqualTo(FixedPoint2.Zero),
                    $"AmputationOverflow must stay zero on a maxDamage-0 part (hit {i}).");
                // The head's Blunt threshold is 350, reached on hit 14 (14 * 25).
                Assert.That(woundable.Severable, Is.EqualTo(i >= 14),
                    $"Severable must flip exactly at the 350 Blunt threshold (hit {i}).");
                Assert.That(graph.BodyHasChild(body, head), Is.True,
                    $"sub-finishing chunks must never detach the part (hit {i}).");
            }

            // One Blunt 50 - exactly the finishing minimum - takes it off at 450, proving the threshold path
            // is alive and that only the overflow half is inert.
            Assert.That(routing.TryApplyPartDamage(body, head, Spec("Blunt", 50), attacker));
            Assert.That(graph.BodyHasChild(body, head), Is.False);

            // (b) the same mechanism on a bespoke part with maxDamage: 50 accumulates, proving it is
            // data-gated rather than code-dead.
            var overflowBody = entities.SpawnEntity("WolfmedAmputationOverflowBody", map.GridCoords);
            var overflowArm = graph.GetBodyChildren(overflowBody)
                .Single(part => part.Component.PartType == BodyPartType.Arm).Id;
            var overflowWoundable = entities.GetComponent<WoundableComponent>(overflowArm);

            // 40 fits under maxDamage 50 entirely, so nothing overflows yet.
            Assert.That(routing.TryApplyPartDamage(overflowBody, overflowArm, Spec("Blunt", 40)));
            Assert.That(overflowWoundable.AmputationOverflow, Is.EqualTo(FixedPoint2.Zero));

            // The second 40 finds only 10 of structural room left: 10 is applied as damage and the
            // remaining 30 becomes tear-off pressure (WoundDamageRoutingSystem.AccumulateAmputationOverflow).
            Assert.That(routing.TryApplyPartDamage(overflowBody, overflowArm, Spec("Blunt", 40)));
            Assert.Multiple(() =>
            {
                Assert.That(overflowWoundable.AmputationOverflow, Is.GreaterThan(FixedPoint2.Zero));
                Assert.That(overflowWoundable.AmputationOverflow, Is.EqualTo(FixedPoint2.New(30)));
                // 30 < maxDamage 50, so OnPartDamageOverflowed has not armed the part yet.
                Assert.That(overflowWoundable.Severable, Is.False);
                Assert.That(graph.BodyHasChild(overflowBody, overflowArm), Is.True);
            });
        });
    }

    /// <summary>
    /// PLAN3 §6.2 T-AMP-EXPLOSION. This exercises the routing API's explosion branch, NOT an end-to-end
    /// grenade path: D24/P3-D3 keep ExplosionSystem unhooked, so nothing in the game reaches this today.
    /// </summary>
    [Test]
    public async Task ExplosionAmputatesDeterministicallyTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var graph = entities.System<SharedBodySystem>();
            var routing = entities.System<WoundDamageRoutingSystem>();
            var parts = graph.GetBodyChildren(body).ToList();
            var leftArm = Find(parts, BodyPartType.Arm, BodyPartSymmetry.Left);
            var rightArm = Find(parts, BodyPartType.Arm, BodyPartSymmetry.Right);

            // Determinism has nothing to do with the two explosion CVars (they still have no live consumer
            // - P3-D3). It comes from two places:
            //   * the single-part mask leaves PickExplosionAmputationCandidate exactly one candidate, so
            //     the _random.NextFloat() weight roll cannot change which limb is picked;
            //   * progress = 500 / (arm Piercing threshold) 250 = 2.0, and the sever chance is
            //     clamp(progress * 0.5, 0, 1), which saturates at exactly 1.0, so _random.Prob(1.0) is
            //     always true.
            // IsFinishingHit also passes: Piercing 500 >= the per-part finishing minimum of 12.
            //
            // WOLFGATE: PLAN3 specifies Slash 260 here (arm Slash threshold 130). That number is
            // unusable: `MajorLimb` carries a Shitmed `Destructible` GibPartBehavior at Slash 210
            // (Resources/Prototypes/Body/Parts/base.yml:288-293) which fires from DamageChangedEvent -
            // i.e. inside TryChangeDamage, BEFORE PartDamageAppliedEvent reaches AmputationSystem - so the
            // arm would be gibbed rather than amputated and the test would pass for the wrong reason.
            // Piercing has no Destructible trigger on any body part in the game, so it is the only damage
            // type on which the x2 progress needed to saturate the explosion chance is reachable.
            Assert.That(routing.TryRouteDistributedDamage(body,
                Spec("Piercing", 500),
                TargetBodyPart.LeftArm,
                DamageDistribution.SplitEvenly,
                variation: 0f,
                isExplosion: true));

            var torso = parts.Single(part => part.Component.PartType == BodyPartType.Torso).Id;
            var dismemberments = entities.System<WoundSystem>()
                .GetWounds((torso, entities.GetComponent<WoundableComponent>(torso)))
                .Where(wound => wound.Comp.Prototype == new ProtoId<WoundPrototype>("DismembermentWound"))
                .ToList();

            Assert.Multiple(() =>
            {
                Assert.That(graph.BodyHasChild(body, leftArm), Is.False,
                    "the explosion branch must detach the masked limb in this single call.");
                // The limb must have been AMPUTATED, not destroyed: a gibbed part is deleted and leaves no
                // dismemberment wound on the stump.
                Assert.That(entities.Deleted(leftArm), Is.False);
                Assert.That(dismemberments, Has.Count.EqualTo(1));
                // WoundHostComponent.DismembermentSeverities[Arm] = 120.
                Assert.That(dismemberments[0].Comp.Severity, Is.EqualTo(FixedPoint2.New(120)));
                // Proves the mask drove the candidate pick rather than a lucky roll over both arms.
                Assert.That(graph.BodyHasChild(body, rightArm), Is.True);
                Assert.That(entities.GetComponent<DamageableComponent>(rightArm).TotalDamage,
                    Is.EqualTo(FixedPoint2.Zero));
            });
        });
    }

    /// <summary>
    /// PLAN3 §6.2 T-AMP-CONSEQUENCE-SEPARATE. Replaces tests.md's T-AMP-CONSEQUENCE-MERGE, which asserted
    /// the opposite (P3-D10).
    /// </summary>
    [Test]
    public async Task RepeatedAmputationCreatesSeparateConsequenceWoundsTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entities.SpawnEntity("WolfmedAmputationBody", map.GridCoords);
            var graph = entities.System<SharedBodySystem>();
            var routing = entities.System<WoundDamageRoutingSystem>();
            var parts = graph.GetBodyChildren(body).ToList();
            var leftArm = Find(parts, BodyPartType.Arm, BodyPartSymmetry.Left);
            var rightArm = Find(parts, BodyPartType.Arm, BodyPartSymmetry.Right);
            var torso = parts.Single(part => part.Component.PartType == BodyPartType.Torso).Id;

            // Arm Slash threshold 130 arms the limb; Slash 15 == the host finishing minimum detaches it.
            // Both arms hang off the same torso, so both stumps land on the same parent.
            foreach (var arm in new[] { leftArm, rightArm })
            {
                Assert.That(routing.TryApplyPartDamage(body, arm, Spec("Slash", 130)));
                Assert.That(entities.GetComponent<WoundableComponent>(arm).Severable, Is.True);
                Assert.That(routing.TryApplyPartDamage(body, arm, Spec("Slash", 15)));
                Assert.That(graph.BodyHasChild(body, arm), Is.False);
            }

            var wounds = entities.System<WoundSystem>()
                .GetWounds((torso, entities.GetComponent<WoundableComponent>(torso)))
                .ToList();
            var consequences = wounds
                .Where(wound => wound.Comp.Prototype == new ProtoId<WoundPrototype>("AmputationConsequenceWound"))
                .ToList();
            var dismemberments = wounds
                .Where(wound => wound.Comp.Prototype == new ProtoId<WoundPrototype>("DismembermentWound"))
                .ToList();

            Assert.Multiple(() =>
            {
                // Both prototypes ship `mergeMode: SeparateInstances` (wounds.yml, byte-identical to
                // Onyx), and WoundSystem.CreateOrMergeWoundInternal only looks for an existing wound under
                // MergeByPrototype - so a second amputation onto the same stump always spawns a new wound
                // entity rather than raising the first one's severity.
                Assert.That(consequences, Has.Count.EqualTo(2),
                    "SeparateInstances means two amputations leave two consequence wounds, not one.");
                Assert.That(dismemberments, Has.Count.EqualTo(2));

                // 50 is the fixture TORSO's amputationConsequenceSeverity: the severity comes off the
                // parent stump, never off the severed arm (which keeps the 35 default).
                Assert.That(consequences.Select(wound => wound.Comp.Severity),
                    Is.All.EqualTo(FixedPoint2.New(50)));
                // WoundHostComponent.DismembermentSeverities[Arm] = 120.
                Assert.That(dismemberments.Select(wound => wound.Comp.Severity),
                    Is.All.EqualTo(FixedPoint2.New(120)));
            });
        });
    }

    private static EntityUid Find(
        List<(EntityUid Id, BodyPartComponent Component)> parts,
        BodyPartType type,
        BodyPartSymmetry symmetry)
    {
        return parts.Single(part => part.Component.PartType == type && part.Component.Symmetry == symmetry).Id;
    }

    private static DamageSpecifier Spec(string type, int amount) => new()
    {
        DamageDict = { [new ProtoId<DamageTypePrototype>(type)] = FixedPoint2.New(amount) },
    };
}
