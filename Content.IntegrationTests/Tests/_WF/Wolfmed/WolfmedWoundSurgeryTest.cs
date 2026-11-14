using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._Shitmed.Medical.Surgery; // WOLFGATE: GetSingleton lives on the concrete server system.
using Content.Server._WF.Wolfmed.Surgery;
using Content.Shared._Onyx.Body.Systems;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._Shitmed.Medical.Surgery;
using Content.Shared._Shitmed.Medical.Surgery.Conditions;
using Content.Shared._Shitmed.Medical.Surgery.Steps;
using Content.Shared._WF.Wolfmed.Body;
using Content.Shared._WF.Wolfmed.Compat;
using Content.Shared.Body.Organ;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.CCVar;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Speech.Muting;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// The six wound surgeries and the organ heal, driven through Shitmed's step machinery: each effect component
/// gets its <see cref="SurgeryStepEvent"/> raised directly and each condition its
/// <see cref="SurgeryStepCompleteCheckEvent"/> or <see cref="SurgeryValidEvent"/>.
/// </summary>
/// <remarks>
/// PLAN4 §6.2 T-SURG-BLEED / -FRACTURE / -INTERNAL / -AMPCONSEQ / -ORGAN / -WINDOW / -SCAR / -PAIN (WP12-9).
/// Per §6.1 trap 9 the do-after, the BUI, PreviousStepsComplete and CanPerformStep are all bypassed: the step
/// entity is spawned bare and the event raised on it, which is exactly what SharedSurgerySystem.OnTargetDoAfter
/// ends up doing. Bare single-component step prototypes are used rather than WP12-5's shipped ones so a failure
/// here is a failure of the effect, not of the surrounding step's emote or sprite; the shipped prototypes are
/// covered by T-SURGERY-PROTOTYPE-SANITY in WolfmedExplosionTest.
/// </remarks>
[TestFixture]
[TestOf(typeof(WolfmedWoundSurgerySystem))]
public sealed class WolfmedWoundSurgeryTest : GameTest
{
    // WOLFGATE: the body fixture carries `- type: MobState` and an explicit `- type: StatusEffects` allow-list
    // (P2-D24) because T-SURG-PAIN moves real pain on a real mob. The bare step entities carry exactly one
    // Wolfmed effect component each and nothing else.
    [TestPrototypes]
    private const string Prototypes = @"
- type: body
  id: WolfmedSurgeryBodyGraph
  name: ""wolfmed surgery body""
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
  id: WolfmedSurgeryBody
  parent: [InventoryBase, MobBloodstream]
  components:
  - type: Body
    prototype: WolfmedSurgeryBodyGraph
  - type: Damageable
    damageContainer: Biological
  - type: MobState
  - type: StatusEffects
    allowed:
    - Stun
    - KnockedDown
    - Jitter
  - type: WoundHost

- type: entity
  id: WolfmedSurgeryControlBody
  parent: [InventoryBase, MobBloodstream]
  components:
  - type: Body
    prototype: WolfmedSurgeryBodyGraph
  - type: Damageable
    damageContainer: Biological
  - type: MobState

- type: entity
  id: WolfmedStepClamp
  categories: [ HideSpawnMenu ]
  components:
  - type: WolfmedSurgeryClampBleedingEffect
    amount: 10

- type: entity
  id: WolfmedStepSetBone
  categories: [ HideSpawnMenu ]
  components:
  - type: WolfmedSurgeryMendFractureEffect
    treatment: Reduced

- type: entity
  id: WolfmedStepMendBone
  categories: [ HideSpawnMenu ]
  components:
  - type: WolfmedSurgeryMendFractureEffect
    treatment: Mended

- type: entity
  id: WolfmedStepStopInternal
  categories: [ HideSpawnMenu ]
  components:
  - type: WolfmedSurgeryTreatWoundEffect
    internalBleeding: true

- type: entity
  id: WolfmedStepHealAmputation
  categories: [ HideSpawnMenu ]
  components:
  - type: WolfmedSurgeryTreatWoundEffect
    woundPrototype: AmputationConsequenceWound

- type: entity
  id: WolfmedStepHealHeartTest
  categories: [ HideSpawnMenu ]
  components:
  - type: WolfmedSurgeryOrganHealEffect
    slot: heart
    amount: 3

- type: entity
  id: WolfmedStepHealFuncOrganTest
  categories: [ HideSpawnMenu ]
  components:
  - type: WolfmedSurgeryOrganHealEffect
    slot: wolfmedfunc
    amount: 3

- type: entity
  id: WolfmedStepSurgeryPain
  categories: [ HideSpawnMenu ]
  components:
  - type: WolfmedSurgeryPainEffect
    amount: 12

- type: entity
  id: WolfmedStepOpenIncisionWound
  categories: [ HideSpawnMenu ]
  components:
  - type: WolfmedSurgeryIncisionWoundEffect
    severity: 10

- type: entity
  id: WolfmedStepClampIncision
  categories: [ HideSpawnMenu ]
  components:
  - type: WolfmedSurgeryIncisionTreatmentEffect
    treatment: Clamp

- type: entity
  id: WolfmedStepCloseIncision
  categories: [ HideSpawnMenu ]
  components:
  - type: WolfmedSurgeryIncisionTreatmentEffect
    treatment: Close
";

    /// <summary>PLAN4 §6.2 T-SURG-BLEED.</summary>
    [Test]
    public async Task SurgeryClampBleedingReducesTheWorstBleederTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entities.SpawnEntity("WolfmedSurgeryBody", map.GridCoords);
            var (head, _, arm) = Parts(entities, body);
            var wounds = entities.System<WoundSystem>();
            var bleeding = entities.System<WoundBleedingSystem>();
            var step = entities.SpawnEntity("WolfmedStepClamp", map.GridCoords);

            // Two DIFFERENT prototypes: SlashWound and PiercingWound both default to
            // `mergeMode: MergeByPrototype`, so two CreateOrMergeWound calls with the same id would merge into
            // one wound and the "worst bleeder first" ordering could not be measured. Both bleeding behaviours
            // carry `minimumSeverity: 9`, so 20 and 10 both actually bleed.
            var slash = wounds.CreateOrMergeWound(arm, "SlashWound", 20)!.Value;
            var pierce = wounds.CreateOrMergeWound(arm, "PiercingWound", 10)!.Value;
            var other = wounds.CreateOrMergeWound(head, "SlashWound", 20)!.Value;

            Assert.Multiple(() =>
            {
                Assert.That(bleeding.GetPartRate(arm), Is.GreaterThan(0f));
                Assert.That(StepIncomplete(entities, step, body, arm), Is.True,
                    "the step must read as incomplete while a bleeder remains.");
            });

            // `amount: 10` on the fixture step. FindWound picks the highest WOUND severity among bleeders, so
            // the Slash 20 is treated before the Piercing 10.
            RaiseStep(entities, step, body, arm);
            Assert.Multiple(() =>
            {
                Assert.That(entities.GetComponent<WoundBleedingComponent>(slash).BleedingSeverity,
                    Is.EqualTo(FixedPoint2.New(10)), "the worst bleeder loses `amount` bleeding severity first.");
                Assert.That(entities.GetComponent<WoundBleedingComponent>(pierce).BleedingSeverity,
                    Is.EqualTo(FixedPoint2.New(10)), "the lesser bleeder on the same part is untouched.");
                Assert.That(entities.GetComponent<WoundBleedingComponent>(other).BleedingSeverity,
                    Is.EqualTo(FixedPoint2.New(20)), "a wound on a different part is untouched.");
            });

            // Second raise finishes the Slash (WoundBleedingSystem.ReduceBleeding removes the component at 0);
            // the third then finds the Piercing as the only remaining bleeder.
            RaiseStep(entities, step, body, arm);
            Assert.Multiple(() =>
            {
                Assert.That(entities.HasComponent<WoundBleedingComponent>(slash), Is.False);
                Assert.That(StepIncomplete(entities, step, body, arm), Is.True);
            });

            RaiseStep(entities, step, body, arm);
            Assert.Multiple(() =>
            {
                Assert.That(entities.HasComponent<WoundBleedingComponent>(pierce), Is.False);
                Assert.That(bleeding.GetPartRate(arm), Is.Zero);
                Assert.That(StepIncomplete(entities, step, body, arm), Is.False,
                    "the step completes once nothing on the part bleeds.");
                Assert.That(bleeding.GetPartRate(head), Is.GreaterThan(0f),
                    "and the untouched part is still bleeding, so the surgery is genuinely per-part.");
            });

            // A raise with nothing left to clamp must be a no-op, not an exception.
            Assert.DoesNotThrow(() => RaiseStep(entities, step, body, arm));
        });
    }

    /// <summary>PLAN4 §6.2 T-SURG-FRACTURE, including the P4-D20 Hairline regression.</summary>
    [Test]
    public async Task SurgeryFractureLadderReducesThenMendsTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entities.SpawnEntity("WolfmedSurgeryBody", map.GridCoords);
            var (_, _, arm) = Parts(entities, body);
            var routing = entities.System<WoundDamageRoutingSystem>();
            var fractures = entities.System<WoundFractureSystem>();
            var setBone = entities.SpawnEntity("WolfmedStepSetBone", map.GridCoords);
            var mendBone = entities.SpawnEntity("WolfmedStepMendBone", map.GridCoords);

            // P2-D23: 75 Blunt clears the Comminuted threshold (60) whose creationChance is 1. Anything lower is
            // a dice roll.
            Assert.That(routing.TryApplyPartDamage(body, arm, Spec("Blunt", 75)));
            var fracture = fractures.GetFracture(arm)!.Value;
            Assert.Multiple(() =>
            {
                Assert.That(fracture.Comp2.Grade, Is.EqualTo(FractureGrade.Comminuted));
                Assert.That(fracture.Comp2.Treatment, Is.EqualTo(FractureTreatment.None));
                Assert.That(StepIncomplete(entities, setBone, body, arm), Is.True);
            });

            // BoneSetter half of the ladder. CanTreat allows Reduced because Comminuted >= the organic profile's
            // reductionMinimumGrade (Simple, wounds.yml:47) and the treatment is still None.
            RaiseStep(entities, setBone, body, arm);
            Assert.Multiple(() =>
            {
                Assert.That(fracture.Comp2.Treatment, Is.EqualTo(FractureTreatment.Reduced));
                Assert.That(StepIncomplete(entities, setBone, body, arm), Is.False);
                Assert.That(StepIncomplete(entities, mendBone, body, arm), Is.True);
            });

            // BoneGel half. `removeWoundWhenMended: true` on OrganicFractureProfile makes TryMend delete the
            // wound outright, so the fracture is gone rather than merely flagged.
            RaiseStep(entities, mendBone, body, arm);
            Assert.Multiple(() =>
            {
                Assert.That(fractures.GetFracture(arm), Is.Null);
                Assert.That(StepIncomplete(entities, mendBone, body, arm), Is.False);
            });

            Assert.DoesNotThrow(() => RaiseStep(entities, mendBone, body, arm),
                "a raise with no fracture present must be a no-op.");

            // P4-D20 regression. A Hairline fracture can NEVER reach Reduced (CanTreat gates it on
            // Grade >= Simple), so a naive "wait until Treatment == Reduced" check would stall the surgery
            // forever on the commonest grade. Drive a fresh Comminuted fracture down into the Hairline band
            // (WFWolfmedFractureProfile: Hairline 12, Simple starts at 20) and assert the set-bone step reports
            // itself complete anyway.
            // M6 (P30): the new fracture starts at the arm's whole trauma (75 + the 75 already stored x 0.8 = 135), not
            // at the hit alone, so it is driven down to 15 from wherever it starts.
            Assert.That(routing.TryApplyPartDamage(body, arm, Spec("Blunt", 75)));
            var second = fractures.GetFracture(arm)!.Value;
            Assert.That(second.Comp1.Severity, Is.GreaterThan(FixedPoint2.New(75)),
                "a second blow to a battered arm did not count the trauma already there.");
            Assert.That(entities.System<WoundSystem>().ChangeSeverity(second.Owner, FixedPoint2.New(15) - second.Comp1.Severity));
            Assert.Multiple(() =>
            {
                Assert.That(second.Comp1.Severity, Is.EqualTo(FixedPoint2.New(15)));
                Assert.That(second.Comp2.Grade, Is.EqualTo(FractureGrade.Hairline));
                Assert.That(StepIncomplete(entities, setBone, body, arm), Is.False,
                    "an unreachable treatment target must count as reached, or WFSurgeryMendFracture stalls (P4-D20).");
                // The gel half is still reachable: CanTreat(Mended) only requires Treatment != Mended.
                Assert.That(StepIncomplete(entities, mendBone, body, arm), Is.True);
            });

            RaiseStep(entities, mendBone, body, arm);
            Assert.That(fractures.GetFracture(arm), Is.Null);
        });
    }

    /// <summary>PLAN4 §6.2 T-SURG-INTERNAL.</summary>
    [Test]
    public async Task SurgeryStopInternalBleedingRemovesTheWoundTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entities.SpawnEntity("WolfmedSurgeryBody", map.GridCoords);
            var (_, torso, _) = Parts(entities, body);
            var wounds = entities.System<WoundSystem>();
            var step = entities.SpawnEntity("WolfmedStepStopInternal", map.GridCoords);

            // The organ path (a destroyed lung) produces the same wound; creating it directly keeps the test off
            // the ~1-4 % per-hit organ roll, exactly as WolfmedOrganTest does.
            var wound = wounds.CreateOrMergeWound(torso, "InternalBleedingWound", 30)!.Value;
            Assert.Multiple(() =>
            {
                Assert.That(entities.GetComponent<WoundInternalBleedingComponent>(wound).Severity,
                    Is.EqualTo(FixedPoint2.New(30)));
                Assert.That(Wounds(entities, wounds, torso, "InternalBleedingWound"), Has.Count.EqualTo(1));
                Assert.That(StepIncomplete(entities, step, body, torso), Is.True);
            });

            // WolfmedSurgeryTreatWoundEffectComponent.Amount defaults to FixedPoint2.MaxValue, and
            // WoundSystem.TreatWound -> ChangeSeverity removes a wound that reaches zero severity. One step.
            RaiseStep(entities, step, body, torso);
            Assert.Multiple(() =>
            {
                Assert.That(Wounds(entities, wounds, torso, "InternalBleedingWound"), Is.Empty,
                    "the default MaxValue amount clears internal bleeding in one step.");
                Assert.That(StepIncomplete(entities, step, body, torso), Is.False);
            });
        });
    }

    /// <summary>PLAN4 §6.2 T-SURG-AMPCONSEQ.</summary>
    [Test]
    public async Task SurgeryHealAmputationConsequenceRemovesTheWoundTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            // WolfmedAmputationBody is WolfmedAmputationTest's fixture: its torso carries
            // `amputationConsequenceSeverity: 50`, which is the only value that can prove the severity is read
            // off the parent stump rather than the severed limb (both defaults are 35).
            var body = entities.SpawnEntity("WolfmedAmputationBody", map.GridCoords);
            var graph = entities.System<SharedBodySystem>();
            var wounds = entities.System<WoundSystem>();
            var routing = entities.System<WoundDamageRoutingSystem>();
            var parts = graph.GetBodyChildren(body).ToList();
            var torso = parts.Single(part => part.Component.PartType == BodyPartType.Torso).Id;
            var arm = parts.Single(part =>
                part.Component.PartType == BodyPartType.Arm &&
                part.Component.Symmetry == BodyPartSymmetry.Left).Id;
            var step = entities.SpawnEntity("WolfmedStepHealAmputation", map.GridCoords);

            // Arm Slash threshold 130 arms the limb; Slash 15 is exactly
            // WoundHostComponent.DefaultDismembermentFinishingDamage["Slash"] and detaches it.
            Assert.That(routing.TryApplyPartDamage(body, arm, Spec("Slash", 130)));
            Assert.That(routing.TryApplyPartDamage(body, arm, Spec("Slash", 15)));
            Assert.That(graph.BodyHasChild(body, arm), Is.False);

            var consequence = Wounds(entities, wounds, torso, "AmputationConsequenceWound");
            Assert.Multiple(() =>
            {
                Assert.That(consequence, Has.Count.EqualTo(1));
                Assert.That(consequence[0].Comp.Severity, Is.EqualTo(FixedPoint2.New(50)),
                    "severity is read off the parent stump - WolfmedAmputationTorso's 50, not the 35 default.");
                Assert.That(StepIncomplete(entities, step, body, torso), Is.True);
            });

            RaiseStep(entities, step, body, torso);

            Assert.Multiple(() =>
            {
                // AmputationConsequenceWound has `damageTypes: {}`, so no reagent, topical or TryHealWounds path
                // can touch it - this surgery is the only cure, by design.
                Assert.That(Wounds(entities, wounds, torso, "AmputationConsequenceWound"), Is.Empty);
                Assert.That(StepIncomplete(entities, step, body, torso), Is.False);
                // The dismemberment wound is a different prototype and must survive - it is what bleeds.
                Assert.That(Wounds(entities, wounds, torso, "DismembermentWound"), Has.Count.EqualTo(1));
            });
        });
    }

    /// <summary>PLAN4 §6.2 T-SURG-ORGAN, including the function-restore half.</summary>
    [Test]
    public async Task SurgeryHealOrganRestoresHealthTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var graph = entities.System<SharedBodySystem>();
            var organHealth = entities.System<OrganHealthSystem>();
            var torso = graph.GetBodyChildren(body)
                .Single(part => part.Component.PartType == BodyPartType.Torso).Id;
            var heart = graph.GetPartOrgans(torso)
                .Single(organ => entities.GetComponent<OrganComponent>(organ.Id).SlotId == "heart").Id;
            var health = entities.GetComponent<WolfmedOrganComponent>(heart);
            var step = entities.SpawnEntity("WolfmedStepHealHeartTest", map.GridCoords);

            // SetHealth directly rather than through damage: PLAN3 §8.4 measured the per-hit organ roll at
            // ~1-4 %, which would make this flaky. MaxHealth is 25 on the Wolfmed heart base since playtest 4 (the
            // component default is 15).
            Assert.That(health.MaxHealth, Is.EqualTo(FixedPoint2.New(25)));
            organHealth.SetHealth((heart, health), FixedPoint2.New(16));

            Assert.That(StepIncomplete(entities, step, body, torso), Is.True);

            // `amount: 3` is P4-D23's balance number: five 2 s repeats from zero, three from 16.
            RaiseStep(entities, step, body, torso);
            Assert.That(health.Health, Is.EqualTo(FixedPoint2.New(19)));
            RaiseStep(entities, step, body, torso);
            Assert.That(health.Health, Is.EqualTo(FixedPoint2.New(22)));
            Assert.That(StepIncomplete(entities, step, body, torso), Is.True);

            RaiseStep(entities, step, body, torso);
            Assert.Multiple(() =>
            {
                Assert.That(health.Health, Is.EqualTo(FixedPoint2.New(25)));
                Assert.That(StepIncomplete(entities, step, body, torso), Is.False);
            });

            // SetHealth clamps to [0, MaxHealth], so a further raise cannot overshoot.
            RaiseStep(entities, step, body, torso);
            Assert.That(health.Health, Is.EqualTo(FixedPoint2.New(25)));
        });
    }

    /// <summary>
    /// PLAN4 §6.2 T-SURG-ORGAN, function-restore half. Uses WolfmedOrganTest's MutedComponent fixture organ and
    /// the one-tick window in which an organ is at zero health but OrganHealthSystem.Update has not yet
    /// destroyed it (P4-D24).
    /// </summary>
    [Test]
    public async Task SurgeryHealOrganRestoresGrantedComponentsTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        var body = EntityUid.Invalid;
        var organ = EntityUid.Invalid;

        await server.WaitAssertion(() =>
        {
            body = entities.SpawnEntity("WolfmedOrganFuncBody", map.GridCoords);
            var graph = entities.System<SharedBodySystem>();
            var torso = graph.GetBodyChildren(body)
                .Single(part => part.Component.PartType == BodyPartType.Torso).Id;
            organ = graph.GetPartOrgans(torso).Single().Id;
            var health = entities.GetComponent<WolfmedOrganComponent>(organ);
            var step = entities.SpawnEntity("WolfmedStepHealFuncOrganTest", map.GridCoords);

            Assert.That(entities.HasComponent<MutedComponent>(body), Is.True,
                "the organ's onAdd grant must be live before the test can prove it is restored.");

            // No tick: zero health revokes the grant immediately via OrganFunctionChangedEvent(false), and
            // OrganHealthSystem.Update would destroy the organ on the next one.
            entities.System<OrganHealthSystem>().SetHealth((organ, health), FixedPoint2.Zero);
            Assert.That(entities.HasComponent<MutedComponent>(body), Is.False);

            RaiseStep(entities, step, body, torso);

            Assert.Multiple(() =>
            {
                Assert.That(health.Health, Is.EqualTo(FixedPoint2.New(3)));
                Assert.That(entities.HasComponent<MutedComponent>(body), Is.True,
                    "healing an organ back above zero must re-raise OrganFunctionChangedEvent(true) and restore its grants.");
            });
        });

        await Pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
            Assert.Multiple(() =>
            {
                Assert.That(entities.Deleted(organ), Is.False,
                    "an organ healed inside the one-tick window must survive OrganHealthSystem.Update.");
                Assert.That(entities.HasComponent<MutedComponent>(body), Is.True);
            }));
    }

    /// <summary>PLAN4 §6.2 T-SURG-WINDOW, including the D2 canary.</summary>
    [Test]
    public async Task WoundSeverityWindowSelectsShallowOrDeepTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var surgeries = entities.System<SurgerySystem>();
            var shallow = surgeries.GetSingleton("SurgeryTendWoundsBrute")!.Value;
            var deep = surgeries.GetSingleton("WFSurgeryTendWoundsBruteDeep")!.Value;
            var routing = entities.System<WoundDamageRoutingSystem>();

            // 60 Blunt -> one BluntWound at severity 60 (`severityMultiplier: 1`). The Brute damage group is
            // Blunt/Slash/Piercing, and BoneFractureWound's `damageTypes: {}` keeps the fracture out of the sum,
            // so GetGroupSeverity(arm, Brute) reads exactly 60.
            var light = entities.SpawnEntity("WolfmedSurgeryBody", map.GridCoords);
            var lightArm = Parts(entities, light).Arm;
            Assert.That(routing.TryApplyPartDamage(light, lightArm, Spec("Blunt", 60)));

            Assert.Multiple(() =>
            {
                Assert.That(SurgeryCancelled(entities, shallow, light, lightArm), Is.False,
                    "60 is inside PROTO F's `maxWoundSeverity: 99.99`, so the shallow tend surgery lists.");
                Assert.That(SurgeryCancelled(entities, deep, light, lightArm), Is.True,
                    "and below `minWoundSeverity: 100`, so the deep one does not.");
            });

            // 100 Blunt -> severity 100, the other side of the window.
            var heavy = entities.SpawnEntity("WolfmedSurgeryBody", map.GridCoords);
            var heavyArm = Parts(entities, heavy).Arm;
            Assert.That(routing.TryApplyPartDamage(heavy, heavyArm, Spec("Blunt", 100)));

            Assert.Multiple(() =>
            {
                Assert.That(SurgeryCancelled(entities, shallow, heavy, heavyArm), Is.True);
                Assert.That(SurgeryCancelled(entities, deep, heavy, heavyArm), Is.False);
            });

            // The D2 canary. WolfmedWoundWindowFails returns false for any body without WoundHostComponent, so
            // HOOK 24 is structurally unreachable there and OnWoundedValid behaves exactly as it did before
            // phase 4: a damaged part lists the shallow tend surgery, with no severity window involved.
            var control = entities.SpawnEntity("WolfmedSurgeryControlBody", map.GridCoords);
            Assert.That(entities.HasComponent<WoundHostComponent>(control), Is.False);
            var controlArm = Parts(entities, control).Arm;
            entities.System<WolfmedDamageableSystem>().SetDamage(controlArm, Spec("Blunt", 150));

            Assert.Multiple(() =>
            {
                Assert.That(SurgeryCancelled(entities, shallow, control, controlArm), Is.False,
                    "a non-wound-host must still list the shallow tend surgery at any damage level (D2).");
                // The deep surgery is hidden on a non-host too - but for the pre-existing reason (it has no
                // wounds at all), not because of the window.
                Assert.That(entities.HasComponent<WoundableComponent>(controlArm), Is.False);
            });
        });
    }

    /// <summary>
    /// PLAN4 §6.2 T-SURG-SCAR. The whole justification for P4-D21: if this is cut, cut the mechanic.
    /// </summary>
    [Test]
    public async Task SurgeryIncisionBleedsClampsAndScarsTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var configuration = server.ResolveDependency<IConfigurationManager>();
        var map = await Pair.CreateTestMap();

        try
        {
            await server.WaitAssertion(() =>
            {
                var body = entities.SpawnEntity("WolfmedSurgeryBody", map.GridCoords);
                var (_, _, arm) = Parts(entities, body);
                var wounds = entities.System<WoundSystem>();
                var bleeding = entities.System<WoundBleedingSystem>();
                var incision = entities.SpawnEntity("WolfmedStepOpenIncisionWound", map.GridCoords);
                var clamp = entities.SpawnEntity("WolfmedStepClampIncision", map.GridCoords);
                var close = entities.SpawnEntity("WolfmedStepCloseIncision", map.GridCoords);

                configuration.SetCVar(CCVars.SurgeryScarChance, 0f);

                RaiseStep(entities, incision, body, arm);
                var opened = Wounds(entities, wounds, arm, "SurgicalIncisionWound");
                Assert.Multiple(() =>
                {
                    // `severity: 10` on the fixture step, matching PROTO G's addition to
                    // SurgeryStepOpenIncisionScalpel.
                    Assert.That(opened, Has.Count.EqualTo(1));
                    Assert.That(opened[0].Comp.Severity, Is.EqualTo(FixedPoint2.New(10)));
                    // SurgicalIncisionWound's WoundBleedingBehavior is `rate: 0.1, awakeMultiplier: 3` with the
                    // default `chance: 1`, so the bleeder is deterministic. The exact rate is clamped
                    // downstream by BloodstreamComponent.MaxBleedAmount, which is why only the sign is asserted.
                    Assert.That(bleeding.GetPartRate(arm), Is.GreaterThan(0f),
                        "a real incision bleeds - that is the point of P4-D21.");
                    Assert.That(StepIncomplete(entities, clamp, body, arm), Is.True);
                });

                RaiseStep(entities, clamp, body, arm);
                Assert.Multiple(() =>
                {
                    // BleedingTreatment.Clamped is a 0f multiplier.
                    Assert.That(bleeding.GetPartRate(arm), Is.Zero);
                    Assert.That(StepIncomplete(entities, clamp, body, arm), Is.False);
                    Assert.That(StepIncomplete(entities, close, body, arm), Is.True);
                });

                // Close at chance 0: cauterise, close, no scar, remove.
                RaiseStep(entities, close, body, arm);
                Assert.Multiple(() =>
                {
                    Assert.That(Wounds(entities, wounds, arm, "SurgicalIncisionWound"), Is.Empty);
                    Assert.That(Wounds(entities, wounds, arm, "MedicalScarWound"), Is.Empty);
                    Assert.That(StepIncomplete(entities, close, body, arm), Is.False);
                });

                // A second operation, this time with the scar roll forced on.
                configuration.SetCVar(CCVars.SurgeryScarChance, 1f);
                RaiseStep(entities, incision, body, arm);
                RaiseStep(entities, clamp, body, arm);
                RaiseStep(entities, close, body, arm);

                Assert.Multiple(() =>
                {
                    Assert.That(Wounds(entities, wounds, arm, "SurgicalIncisionWound"), Is.Empty);
                    Assert.That(Wounds(entities, wounds, arm, "MedicalScarWound"), Has.Count.EqualTo(1),
                        "surgery can scar for the first time in Wolfgate (P4-D21).");
                });

                // PROTO G puts the Close effect on BOTH SurgeryStepCloseIncision and SurgeryStepSealTendWound,
                // so the same component is raised twice in one operation. It must not mint a second scar.
                RaiseStep(entities, close, body, arm);
                Assert.That(Wounds(entities, wounds, arm, "MedicalScarWound"), Has.Count.EqualTo(1),
                    "a second Close in the same operation must not create a second scar.");
            });
        }
        finally
        {
            await server.WaitPost(() =>
                configuration.SetCVar(CCVars.SurgeryScarChance, CCVars.SurgeryScarChance.DefaultValue));
        }
    }

    /// <summary>PLAN4 §6.2 T-SURG-PAIN (P4-D22).</summary>
    [Test]
    public async Task SurgeryStepInflictsPainTest()
    {
        await OverrideCVar(Side.Server, WolfmedCVars.PainScale, 1f); // playtest 5: this test pins Onyx's figures

        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entities.SpawnEntity("WolfmedSurgeryBody", map.GridCoords);
            var (_, _, arm) = Parts(entities, body);
            var pain = entities.System<PainSystem>();
            var partPain = entities.GetComponent<PainComponent>(arm);
            var bodyPain = entities.GetComponent<PainComponent>(body);
            var step = entities.SpawnEntity("WolfmedStepSurgeryPain", map.GridCoords);

            Assert.Multiple(() =>
            {
                Assert.That(pain.GetRawPain((arm, partPain)), Is.EqualTo(FixedPoint2.Zero));
                Assert.That(pain.GetRawPain((body, bodyPain)), Is.EqualTo(FixedPoint2.Zero));
            });

            // `amount: 12` on the fixture step - Onyx's mend-fracture figure (P4-D22). PainComponent lives on
            // the PART (PainSystem.cs:69) and SetPain propagates the delta up to the body's own component.
            RaiseStep(entities, step, body, arm);

            Assert.Multiple(() =>
            {
                Assert.That(pain.GetRawPain((arm, partPain)), Is.EqualTo(FixedPoint2.New(12)));
                Assert.That(pain.GetRawPain((body, bodyPain)), Is.EqualTo(FixedPoint2.New(12)),
                    "part pain must aggregate onto the body, which is what the pain HUD and pain shock read.");
            });
        });
    }

    private static void RaiseStep(IEntityManager entities, EntityUid step, EntityUid body, EntityUid part)
    {
        // WOLFGATE: WG's SurgeryStepEvent is a 5-tuple (Onyx's is 4). None of the Wolfmed handlers reads User or
        // Surgery, so the body stands in for the surgeon and the step entity for the surgery singleton.
        var ev = new SurgeryStepEvent(body, body, part, new List<EntityUid>(), step);
        entities.EventBus.RaiseLocalEvent(step, ref ev);
    }

    private static bool StepIncomplete(IEntityManager entities, EntityUid step, EntityUid body, EntityUid part)
    {
        var ev = new SurgeryStepCompleteCheckEvent(body, part, step);
        entities.EventBus.RaiseLocalEvent(step, ref ev);
        return ev.Cancelled;
    }

    private static bool SurgeryCancelled(IEntityManager entities, EntityUid surgery, EntityUid body, EntityUid part)
    {
        var ev = new SurgeryValidEvent(body, part);
        entities.EventBus.RaiseLocalEvent(surgery, ref ev);
        return ev.Cancelled;
    }

    private static List<Entity<WoundComponent>> Wounds(
        IEntityManager entities,
        WoundSystem wounds,
        EntityUid part,
        string prototype)
    {
        return wounds.GetWounds((part, entities.GetComponent<WoundableComponent>(part)))
            .Where(wound => wound.Comp.Prototype == new ProtoId<WoundPrototype>(prototype))
            .ToList();
    }

    private static (EntityUid Head, EntityUid Torso, EntityUid Arm) Parts(IEntityManager entities, EntityUid body)
    {
        var parts = entities.System<SharedBodySystem>().GetBodyChildren(body).ToList();
        return (parts.Single(part => part.Component.PartType == BodyPartType.Head).Id,
            parts.Single(part => part.Component.PartType == BodyPartType.Torso).Id,
            parts.Single(part => part.Component.PartType == BodyPartType.Arm).Id);
    }

    private static DamageSpecifier Spec(string type, int amount) => new()
    {
        DamageDict = { [new ProtoId<DamageTypePrototype>(type)] = FixedPoint2.New(amount) },
    };
}
