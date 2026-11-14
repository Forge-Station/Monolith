#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._WF.Wolfmed.Autodoc;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Wolfmed.Autodoc;
using Content.Shared._WF.Wolfmed.Reagents;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Bed.Sleep;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// AUTODOC4: the pod may never loop, and it can now clear what used to block it. The two hand-only
/// treatments are real surgeries, a step that achieves nothing three times running abandons its procedure,
/// and the automatic planner stops looking at a body it is not changing.
/// </summary>
/// <remarks>
/// Two seam surgeries stand in for the shapes that used to loop, without pinning any shipped prototype:
/// <c>WolfmedSeamStallSurgery</c> treats a dislocation for zero units, so its completion check can never
/// pass and the part never changes; <c>WolfmedSeamLoopSurgery</c> is complete the moment it starts and
/// stays valid, which is the procedure an automatic pod would re-plan for ever.
/// </remarks>
[TestFixture]
[TestOf(typeof(AutodocSystem))]
public sealed class WolfmedAutodocLoopTest : GameTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: SurgeryStepBase
  id: WolfmedSeamStallStep
  name: Seam step that never finishes
  categories: [ HideSpawnMenu ]
  components:
  - type: SurgeryStep
    tool:
    - type: Hemostat
    duration: 0.05
  - type: Sprite
    sprite: _Shitmed/Objects/Specific/Medical/Surgery/hemostat.rsi
    state: hemostat
  - type: WolfmedSurgeryTreatWoundEffect
    woundPrototype: WFWolfmedDislocationWound
    amount: 0

- type: entity
  parent: SurgeryStepBase
  id: WolfmedSeamLoopStep
  name: Seam step that is already done
  categories: [ HideSpawnMenu ]
  components:
  - type: SurgeryStep
    tool:
    - type: Hemostat
    duration: 0.05
  - type: Sprite
    sprite: _Shitmed/Objects/Specific/Medical/Surgery/hemostat.rsi
    state: hemostat
  - type: WolfmedSurgeryTreatWoundEffect
    woundPrototype: WFWolfmedCharringWound
    amount: 0

- type: entity
  parent: SurgeryBase
  id: WolfmedSeamStallSurgery
  name: Seam Stall
  categories: [ HideSpawnMenu ]
  components:
  - type: Surgery
    steps:
    - WolfmedSeamStallStep
  - type: WolfmedSurgeryWoundCondition
    woundPrototype: WFWolfmedDislocationWound

- type: entity
  parent: SurgeryBase
  id: WolfmedSeamLoopSurgery
  name: Seam Loop
  categories: [ HideSpawnMenu ]
  components:
  - type: Surgery
    steps:
    - WolfmedSeamLoopStep
  - type: WolfmedSurgeryWoundCondition
    woundPrototype: WFWolfmedDislocationWound

- type: autodocProgram
  id: WolfmedSeamProgram
  name: wolfmed-autodoc-program-base
  surgeries:
  - WolfmedSeamStallSurgery
  - WolfmedSeamLoopSurgery

- type: autodocTriage
  id: WolfmedSeamTriageStall
  steps:
  - surgeries:
    - WolfmedSeamStallSurgery
    - WFSurgeryMendFracture

- type: autodocTriage
  id: WolfmedSeamTriageLoop
  steps:
  - surgeries:
    - WolfmedSeamLoopSurgery

- type: entity
  id: WolfmedSeamAutodoc
  parent: WFMachineAutodoc
  suffix: seam
  components:
  - type: Autodoc
    stepTime: 0.02
    bestStepTime: 0.02
    malfunctionChance: 0
    autoPlanInterval: 0.2
    basePrograms:
    - WFWolfmedAutodocProgramBase
    - WolfmedSeamProgram
    triage: WolfmedSeamTriageStall
  - type: ApcPowerReceiver
    needsPower: false

- type: entity
  id: WolfmedSeamAutodocLoop
  parent: WolfmedSeamAutodoc
  suffix: seam loop
  components:
  - type: Autodoc
    triage: WolfmedSeamTriageLoop

- type: entity
  id: WolfmedLoopTestAutodoc
  parent: WFMachineAutodoc
  suffix: loop test
  components:
  - type: Autodoc
    stepTime: 0.02
    bestStepTime: 0.02
    malfunctionChance: 0
    autoPlanInterval: 0.2
  - type: ApcPowerReceiver
    needsPower: false

- type: entity
  id: WolfmedLoopTestOpiateJug
  parent: Jug
  suffix: opiate
  components:
  - type: SolutionContainerManager
    solutions:
      beaker:
        maxVol: 200
        reagents:
        - ReagentId: WFWolfmedOpiate
          Quantity: 100
";

    /// <summary>
    /// The owner's bug: a lodged round in the torso refuses every treatment on the part, so tending it
    /// repeated for ever. The planner now takes the round out first and leaves the rest of the torso alone
    /// until it is gone, and the round ends up on the floor rather than in the patient.
    /// </summary>
    [Test]
    public async Task EmbeddedObjectIsRemovedBeforeAnythingElseOnThePartTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        await server.WaitPost(() => server.System<WolfmedWoundRuleSystem>().ForcedRoll = 0f);
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        Entity<AutodocComponent> pod = default;
        EntityUid torso = default;

        await server.WaitAssertion(() =>
        {
            var autodoc = entities.System<AutodocSystem>();
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var bullet = entities.SpawnEntity("BulletMinigun", map.GridCoords);
            entities.System<DamageableSystem>().TryChangeDamage(body, Spec(25, "Piercing"),
                origin: null, targetPart: TargetBodyPart.Torso, tool: bullet);
            Blunt(entities, body, TargetBodyPart.Torso, 30);

            torso = Part(entities, body, BodyPartType.Torso, BodyPartSymmetry.None);
            Assert.That(entities.System<WolfmedEmbeddedObjectSystem>().GetPartCount(torso), Is.GreaterThan(0),
                "the fixture needs a round lodged in the torso.");

            pod = Pod(entities, map, "WolfmedLoopTestAutodoc");
            Assert.That(autodoc.TryInsert(pod, body), Is.True);
        });

        await Pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var autodoc = entities.System<AutodocSystem>();
            Assert.That(autodoc.TryPlan(pod), Is.GreaterThan(0), "nothing was planned for a shot patient.");

            var planned = pod.Comp!.Queue.Select(queued => queued.Surgery.Id).ToList();
            Assert.Multiple(() =>
            {
                Assert.That(planned[0], Is.EqualTo("WFSurgeryRemoveEmbeddedObjects"),
                    $"the removal is not first in the plan: {string.Join(", ", planned)}");
                // Playtest 3 SAM: the rest of the part follows the removal in the same plan, as follow-ups the pod
                // drops at their turn if the round is still in. Nothing on the part runs ahead of the removal.
                Assert.That(pod.Comp.Queue.Skip(1).Where(queued => queued.Part == TargetBodyPart.Torso).All(queued => queued.FollowUp),
                    Is.True, "the planner queued work on a part that still has a round in it, not waiting on the removal.");
            });

            Assert.That(autodoc.TryStart(pod, null), Is.True);
        });

        await Pair.RunTicksSync(400);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(entities.System<WolfmedEmbeddedObjectSystem>().GetPartCount(torso), Is.Zero,
                    $"the round is still in the torso (state {pod.Comp!.State}, step {pod.Comp.CurrentStep}).");
                Assert.That(Count(entities, "WFWolfmedSpentRound"), Is.GreaterThan(0),
                    "the round never became an item outside the patient.");
                Assert.That(pod.Comp.StepRuns.Values.DefaultIfEmpty(0).Max(), Is.LessThanOrEqualTo(3),
                    $"a step repeated too often: {string.Join(", ", pod.Comp.StepRuns.Select(pair => $"{pair.Key}={pair.Value}"))}");
            });
        });

        // With the blocker gone the torso was ordinary work again, in the same queue (playtest 3 SAM): the bruise
        // was tended after the removal, so there is no tend left to plan on it.
        await server.WaitAssertion(() =>
        {
            var autodoc = entities.System<AutodocSystem>();
            Assert.That(autodoc.GetOccupant(pod), Is.Not.Null);
            Assert.That(autodoc.Plan(pod, autodoc.GetOccupant(pod)!.Value)
                    .Any(entry => entry.Part == TargetBodyPart.Torso && entry.Surgery.Id.Contains("SurgeryTendWounds")),
                Is.False, $"the torso's tend did not follow the removal (state {pod.Comp!.State}).");
        });

        await server.WaitPost(() => server.System<WolfmedWoundRuleSystem>().ForcedRoll = null);
    }

    /// <summary>
    /// A procedure whose step can never read complete is abandoned after three runs that change nothing,
    /// the pod carries on with the next thing in the queue, and the planner never offers it again for this
    /// patient.
    /// </summary>
    [Test]
    public async Task StalledProcedureIsAbandonedAndNeverReplannedTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        Entity<AutodocComponent> pod = default;
        EntityUid leg = default;

        await server.WaitAssertion(() =>
        {
            var autodoc = entities.System<AutodocSystem>();
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            Blunt(entities, body, TargetBodyPart.LeftLeg, 60);
            leg = Part(entities, body, BodyPartType.Leg, BodyPartSymmetry.Left);

            var arm = Part(entities, body, BodyPartType.Arm, BodyPartSymmetry.Left);
            entities.System<WoundSystem>().CreateOrMergeWound(arm, "WFWolfmedDislocationWound", FixedPoint2.New(20));

            pod = Pod(entities, map, "WolfmedSeamAutodoc");
            Assert.That(autodoc.TryInsert(pod, body), Is.True);
        });

        await Pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var autodoc = entities.System<AutodocSystem>();
            Assert.That(autodoc.TryPlan(pod), Is.GreaterThanOrEqualTo(2),
                "the seam triage plans the stall and the fracture.");
            Assert.That(pod.Comp!.Queue[0].Surgery.Id, Is.EqualTo("WolfmedSeamStallSurgery"));
            Assert.That(autodoc.TryStart(pod, null), Is.True);
        });

        await Pair.RunTicksSync(400);

        await server.WaitAssertion(() =>
        {
            var fractures = entities.System<WoundFractureSystem>();
            Assert.Multiple(() =>
            {
                Assert.That(pod.Comp!.FailedProcedures.Any(failed => failed.Surgery == "WolfmedSeamStallSurgery"),
                    Is.True, "the pod never gave up on a procedure it could not finish.");
                Assert.That(pod.Comp.Queue, Is.Empty, $"the queue did not drain (state {pod.Comp.State}).");

                var fracture = fractures.GetFracture(leg);
                Assert.That(fracture == null || fracture.Value.Comp2.Treatment == FractureTreatment.Mended, Is.True,
                    "the pod stopped at the hopeless procedure instead of moving on to the fracture.");
            });

            // And the planner will not hand it back.
            entities.System<AutodocSystem>().TryPlan(pod);
            Assert.That(pod.Comp!.Queue.Select(queued => queued.Surgery.Id),
                Does.Not.Contain("WolfmedSeamStallSurgery"), "the planner re-queued a procedure it had given up on.");
        });
    }

    /// <summary>A dislocated joint is a surgery now, and the pod sets it without a hand or a do-after.</summary>
    [Test]
    public async Task PodRelocatesADislocatedJointTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        Entity<AutodocComponent> pod = default;
        EntityUid arm = default;

        await server.WaitAssertion(() =>
        {
            var autodoc = entities.System<AutodocSystem>();
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            arm = Part(entities, body, BodyPartType.Arm, BodyPartSymmetry.Left);
            entities.System<WoundSystem>().CreateOrMergeWound(arm, "WFWolfmedDislocationWound", FixedPoint2.New(20));

            pod = Pod(entities, map, "WolfmedLoopTestAutodoc");
            Assert.That(autodoc.TryInsert(pod, body), Is.True);
        });

        await Pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var autodoc = entities.System<AutodocSystem>();
            Assert.That(autodoc.TryQueue(pod, "WFSurgeryRelocateJoint", TargetBodyPart.LeftArm), Is.True,
                "relocating a joint is in the base library.");
            Assert.That(autodoc.TryStart(pod, null), Is.True);
        });

        await Pair.RunTicksSync(200);

        await server.WaitAssertion(() =>
        {
            var wounds = entities.System<WoundSystem>();
            var left = wounds.GetWounds((arm, entities.GetComponent<WoundableComponent>(arm)))
                .Count(wound => wound.Comp.Prototype == new ProtoId<WoundPrototype>("WFWolfmedDislocationWound"));

            Assert.That(left, Is.Zero, $"the joint is still out (state {pod.Comp!.State}).");
        });
    }

    /// <summary>
    /// A body the pod cannot change: the seam procedure finishes the instant it starts and lists again, so
    /// an unbounded planner would run it for ever. The module looks a bounded number of times and idles.
    /// </summary>
    [Test]
    public async Task AutofixStopsReplanningABodyItIsNotChangingTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        Entity<AutodocComponent> pod = default;

        await server.WaitAssertion(() =>
        {
            var autodoc = entities.System<AutodocSystem>();
            var slots = entities.System<ItemSlotsSystem>();
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var arm = Part(entities, body, BodyPartType.Arm, BodyPartSymmetry.Left);
            entities.System<WoundSystem>().CreateOrMergeWound(arm, "WFWolfmedDislocationWound", FixedPoint2.New(20));

            pod = Pod(entities, map, "WolfmedSeamAutodocLoop");
            Assert.That(slots.TryInsert(pod.Owner, AutodocComponent.AutofixSlotId,
                entities.SpawnEntity("WFAutodocAutofixModule", map.GridCoords), null), Is.True);
            autodoc.SetAuto(pod, true);
            Assert.That(autodoc.TryInsert(pod, body), Is.True);
        });

        // Long enough for the bound to be reached at autoPlanInterval 0.2 s, short enough that the test
        // map's lack of air has not yet moved the occupant's organs and made the body genuinely different.
        await Pair.RunTicksSync(150);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(pod.Comp!.AutoReplans, Is.LessThanOrEqualTo(pod.Comp.AutoReplanLimit),
                    "the module kept re-planning a body nothing was changing.");
                Assert.That(pod.Comp.AutoSaidNothing, Is.True, "it never said it had nothing left to do.");
                Assert.That(pod.Comp.State, Is.EqualTo(AutodocState.Complete).Or.EqualTo(AutodocState.Idle));
            });
        });
    }

    /// <summary>
    /// Tending reads the part it is working on, not the whole patient. A cut torso closes while a hand is
    /// still hurt, and a moderate cut takes the passes a surgeon would expect rather than twenty.
    /// </summary>
    [Test]
    public async Task TendClosesThePartAndIgnoresTheRestOfTheBodyTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        // A plain cut: the W2 rules would otherwise roll a severed artery, which refuses every treatment
        // while it pumps and is not what "tend a moderate cut" measures.
        await server.WaitPost(() => server.System<WolfmedWoundRuleSystem>().ForcedRoll = 1f);
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        Entity<AutodocComponent> pod = default;
        EntityUid torso = default;
        EntityUid hand = default;

        await server.WaitAssertion(() =>
        {
            var autodoc = entities.System<AutodocSystem>();
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            Slash(entities, body, TargetBodyPart.Torso, 40);
            Slash(entities, body, TargetBodyPart.LeftHand, 10);

            torso = Part(entities, body, BodyPartType.Torso, BodyPartSymmetry.None);
            hand = Part(entities, body, BodyPartType.Hand, BodyPartSymmetry.Left);

            pod = Pod(entities, map, "WolfmedLoopTestAutodoc");
            Assert.That(autodoc.TryInsert(pod, body), Is.True);
        });

        await Pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var autodoc = entities.System<AutodocSystem>();
            Assert.That(autodoc.TryQueue(pod, "SurgeryTendWoundsBrute", TargetBodyPart.Torso), Is.True);
            Assert.That(autodoc.TryStart(pod, null), Is.True);
        });

        await Pair.RunTicksSync(400);

        await server.WaitAssertion(() =>
        {
            var conditions = entities.System<Content.Shared._WF.Wolfmed.Surgery.WolfmedSurgeryConditionSystem>();
            Assert.Multiple(() =>
            {
                Assert.That(pod.Comp!.State, Is.EqualTo(AutodocState.Complete),
                    $"tending never finished (step {pod.Comp.CurrentStep}).");
                // The cut itself is closed. What is left on the torso is the surgery's own trauma and the
                // severed artery, which no amount of tending closes while it is still pumping.
                Assert.That(Wounds(entities, torso), Does.Not.Contain("SlashWound="),
                    "the cut is still open after three tend passes: " + Wounds(entities, torso));
                Assert.That(conditions.GetTreatableGroupSeverity(hand, "Brute"), Is.GreaterThan(FixedPoint2.Zero),
                    "the fixture wanted the hand left hurt, so the completion check is proved to ignore it.");
                Assert.That(pod.Comp.StepRuns.GetValueOrDefault("SurgeryStepRepairBruteTissue"),
                    Is.LessThanOrEqualTo(3), "a moderate cut took more than three tend passes.");
            });
        });

        await server.WaitPost(() => server.System<WolfmedWoundRuleSystem>().ForcedRoll = null);
    }

    /// <summary>
    /// A long queue gets one anaesthetic, not one per procedure. Sedation stays under the pod's own cap,
    /// the patient is asleep while it works (which is what silences the surgery scream) and awake after.
    /// </summary>
    [Test]
    public async Task LongQueueDosesOnceAndWakesThePatientTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        Entity<AutodocComponent> pod = default;
        EntityUid body = default;
        EntityUid jug = default;
        var start = FixedPoint2.Zero;

        await server.WaitAssertion(() =>
        {
            var autodoc = entities.System<AutodocSystem>();
            var slots = entities.System<ItemSlotsSystem>();
            body = entities.SpawnEntity("MobHuman", map.GridCoords);

            // M1a: two broken legs sum to about 200 pain, past the 189 faint line now that the pain shock's
            // adrenaline no longer takes 30% off it. This test is about dosing, so the patient feels nothing.
            entities.EnsureComponent<Content.Shared.Traits.Assorted.PainNumbnessComponent>(body);

            foreach (var target in new[] { TargetBodyPart.LeftLeg, TargetBodyPart.RightLeg })
                Blunt(entities, body, target, 60);

            pod = Pod(entities, map, "WolfmedLoopTestAutodoc");

            // A jug is not a beaker: it carries DrainableSolution and no FitsInDispenser, which is why
            // loading one used to do nothing at all.
            jug = entities.SpawnEntity("WolfmedLoopTestOpiateJug", map.GridCoords);
            Assert.That(slots.TryInsert(pod.Owner, AutodocComponent.ReservoirSlotIds[0], jug, null), Is.True,
                "the reservoir refused a chemistry jug.");
            Assert.That(autodoc.TryGetReservoirSolution(jug, out _, out var solution), Is.True,
                "the pod cannot read a drainable container.");
            start = solution!.Volume;

            Assert.That(autodoc.TryInsert(pod, body), Is.True);
        });

        await Pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var autodoc = entities.System<AutodocSystem>();
            Assert.That(entities.System<Content.Shared.Mobs.Systems.MobStateSystem>().IsCritical(body), Is.False,
                "the fixture needs a conscious patient: the pod does not anaesthetise one who is already out.");

            foreach (var target in new[] { TargetBodyPart.LeftLeg, TargetBodyPart.RightLeg })
                autodoc.TryQueue(pod, "WFSurgeryMendFracture", target);

            Assert.That(pod.Comp!.Queue, Has.Count.GreaterThan(1), "the fixture needs a queue to run.");
            Assert.That(autodoc.TryStart(pod, null), Is.True);
        });

        await Pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
        {
            Assert.That(entities.HasComponent<ForcedSleepingComponent>(body), Is.True,
                "the patient was awake for their own surgery, which is what makes them scream through it.");
        });

        await Pair.RunTicksSync(600);

        await server.WaitAssertion(() =>
        {
            var autodoc = entities.System<AutodocSystem>();
            var relief = entities.System<WolfmedPainReliefSystem>();
            Assert.That(autodoc.TryGetReservoirSolution(jug, out _, out var solution), Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(pod.Comp!.State, Is.EqualTo(AutodocState.Complete),
                    $"the queue never finished (step {pod.Comp.CurrentStep}).");
                Assert.That((start - solution!.Volume).Float(), Is.LessThanOrEqualTo(30f),
                    "the pod dosed more than once plus a top-up over one queue.");
                Assert.That(relief.GetSedation(body), Is.LessThan(0.6f),
                    "sedation went past the pod's cap and on into respiratory depression.");
                // The airloss a body takes from the test map's own lack of atmosphere is not the pod's;
                // what the pod must not do is depress the breathing itself, which is this number.
                Assert.That(relief.GetRespiratoryDepression(body), Is.EqualTo(0f),
                    "the pod sedated the patient into respiratory depression.");
                Assert.That(pod.Comp.Sedated, Is.False, "the pod never woke the patient up.");
                Assert.That(entities.HasComponent<ForcedSleepingComponent>(body), Is.False,
                    "the patient was left asleep in the pod.");
            });
        });
    }

    /// <summary>
    /// Self-service: somebody who climbs in themselves gets treated. FIX ME plans and starts, and a
    /// procedure queued from inside runs even with the autofix module on, which used to wipe the queue
    /// every few seconds.
    /// </summary>
    [Test]
    public async Task SelfServiceOccupantIsTreatedTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        Entity<AutodocComponent> pod = default;
        EntityUid leg = default;

        await server.WaitAssertion(() =>
        {
            var autodoc = entities.System<AutodocSystem>();
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            Blunt(entities, body, TargetBodyPart.LeftLeg, 60);
            leg = Part(entities, body, BodyPartType.Leg, BodyPartSymmetry.Left);

            pod = Pod(entities, map, "WolfmedLoopTestAutodoc");
            Assert.That(autodoc.TryInsert(pod, body), Is.True);
            pod.Comp.SelfService = true;
        });

        await Pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var autodoc = entities.System<AutodocSystem>();
            autodoc.Control(pod, AutodocControl.Plan, null);

            Assert.Multiple(() =>
            {
                Assert.That(pod.Comp!.Queue, Has.Count.EqualTo(1), "FIX ME planned nothing for the occupant.");
                Assert.That(pod.Comp.State, Is.Not.EqualTo(AutodocState.Idle), "and it did not start.");
            });
        });

        await Pair.RunTicksSync(400);

        // And a procedure the occupant picks from inside runs, which is the other half of self-service.
        await server.WaitAssertion(() =>
        {
            var autodoc = entities.System<AutodocSystem>();
            Assert.That(autodoc.TryQueue(pod, "WFSurgeryMendFracture", TargetBodyPart.LeftLeg), Is.True,
                $"the occupant could not queue their own fracture (state {pod.Comp!.State}).");
            Assert.That(autodoc.TryStart(pod, null), Is.True);
        });

        await Pair.RunTicksSync(400);

        await server.WaitAssertion(() =>
        {
            var fracture = entities.System<WoundFractureSystem>().GetFracture(leg);
            Assert.That(fracture == null || fracture.Value.Comp2.Treatment == FractureTreatment.Mended, Is.True,
                $"the occupant's own procedure never ran (state {pod.Comp!.State}).");
        });
    }

    /// <summary>With the module on, a queue somebody typed is run, not thrown away and re-planned over.</summary>
    [Test]
    public async Task AutofixRunsAHandWrittenQueueTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        Entity<AutodocComponent> pod = default;
        EntityUid leg = default;

        await server.WaitAssertion(() =>
        {
            var autodoc = entities.System<AutodocSystem>();
            var slots = entities.System<ItemSlotsSystem>();
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            Blunt(entities, body, TargetBodyPart.LeftLeg, 60);
            leg = Part(entities, body, BodyPartType.Leg, BodyPartSymmetry.Left);

            pod = Pod(entities, map, "WolfmedLoopTestAutodoc");
            Assert.That(slots.TryInsert(pod.Owner, AutodocComponent.AutofixSlotId,
                entities.SpawnEntity("WFAutodocAutofixModule", map.GridCoords), null), Is.True);
            autodoc.SetAuto(pod, true);
            Assert.That(autodoc.TryInsert(pod, body), Is.True);
        });

        await Pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var autodoc = entities.System<AutodocSystem>();
            Assert.That(autodoc.TryQueue(pod, "WFSurgeryMendFracture", TargetBodyPart.LeftLeg), Is.True);
        });

        await Pair.RunTicksSync(400);

        await server.WaitAssertion(() =>
        {
            var fracture = entities.System<WoundFractureSystem>().GetFracture(leg);
            Assert.That(fracture == null || fracture.Value.Comp2.Treatment == FractureTreatment.Mended, Is.True,
                $"the hand-written queue never ran (state {pod.Comp!.State}, queue {pod.Comp.Queue.Count}).");
        });
    }

    /// <summary>
    /// A brain at three per cent is alive, and used to have no listed procedure at all: the repair surgery
    /// wanted a destroyed brain and the heal wanted a damaged one that the analyzer never pointed anybody at.
    /// </summary>
    [Test]
    public async Task DamagedBrainListsAndIsRepairedTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        Entity<AutodocComponent> pod = default;
        EntityUid body = default;

        await server.WaitAssertion(() =>
        {
            var autodoc = entities.System<AutodocSystem>();
            var slots = entities.System<ItemSlotsSystem>();
            var life = entities.System<Content.Server._WF.Wolfmed.Life.WolfmedLifeSystem>();
            body = entities.SpawnEntity("MobHuman", map.GridCoords);

            var brain = life.GetBrainOrgan(body);
            Assert.That(brain, Is.Not.Null, "the fixture needs a brain to damage.");
            entities.System<Content.Shared._Onyx.Body.Systems.OrganHealthSystem>()
                .SetHealth(brain!.Value, FixedPoint2.New(0.5));

            var head = Part(entities, body, BodyPartType.Head, BodyPartSymmetry.None);
            Assert.That(entities.System<Content.Shared._Shitmed.Medical.Surgery.SharedSurgerySystem>()
                    .WolfmedSurgeryValid(body, head, "WFSurgeryRepairBrain"), Is.True,
                "a brain at three per cent has no repair listed.");

            pod = Pod(entities, map, "WolfmedLoopTestAutodoc");
            slots.TryInsert(pod.Owner, AutodocComponent.DiskSlotId,
                entities.SpawnEntity("WFAutodocProgramDiskNeuro", map.GridCoords), null);
            Assert.That(autodoc.TryInsert(pod, body), Is.True);
        });

        await Pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var autodoc = entities.System<AutodocSystem>();
            Assert.That(autodoc.TryQueue(pod, "WFSurgeryRepairBrain", TargetBodyPart.Head), Is.True,
                "the neuro disk does not unlock the repair.");
            Assert.That(autodoc.TryStart(pod, null), Is.True);
        });

        await Pair.RunTicksSync(500);

        await server.WaitAssertion(() =>
        {
            var life = entities.System<Content.Server._WF.Wolfmed.Life.WolfmedLifeSystem>();
            Assert.That(life.GetBrainActivity(body), Is.GreaterThan(0.9f),
                $"the brain was not repaired (state {pod.Comp!.State}, step {pod.Comp.CurrentStep}).");
        });
    }

    private static string Wounds(IEntityManager entities, EntityUid part)
    {
        return string.Join(", ", entities.System<WoundSystem>()
            .GetWounds((part, entities.GetComponent<WoundableComponent>(part)))
            .Select(wound => $"{wound.Comp.Prototype.Id}={wound.Comp.Severity}/{wound.Comp.State}"));
    }

    private static int Count(IEntityManager entities, string prototype)
    {
        var found = 0;
        var query = entities.AllEntityQueryEnumerator<MetaDataComponent>();
        while (query.MoveNext(out _, out var meta))
        {
            if (meta.EntityPrototype?.ID == prototype)
                found++;
        }

        return found;
    }

    private static Entity<AutodocComponent> Pod(IEntityManager entities, TestMapData map, string prototype)
    {
        var pod = entities.SpawnEntity(prototype, map.GridCoords);
        return (pod, entities.GetComponent<AutodocComponent>(pod));
    }

    private static void Blunt(IEntityManager entities, EntityUid body, TargetBodyPart target, int amount)
    {
        entities.System<DamageableSystem>().TryChangeDamage(body, Spec(amount), origin: null, targetPart: target);
    }

    private static void Slash(IEntityManager entities, EntityUid body, TargetBodyPart target, int amount)
    {
        entities.System<DamageableSystem>()
            .TryChangeDamage(body, Spec(amount, "Slash"), origin: null, targetPart: target);
    }

    private static EntityUid Part(IEntityManager entities, EntityUid body, BodyPartType type, BodyPartSymmetry symmetry)
    {
        return entities.System<SharedBodySystem>().GetBodyChildren(body)
            .Single(part => part.Component.PartType == type && part.Component.Symmetry == symmetry)
            .Id;
    }

    private static DamageSpecifier Spec(int amount, string type = "Blunt") => new()
    {
        DamageDict = { [new ProtoId<DamageTypePrototype>(type)] = FixedPoint2.New(amount) },
    };
}
