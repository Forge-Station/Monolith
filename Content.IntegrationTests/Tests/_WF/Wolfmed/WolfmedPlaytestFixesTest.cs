#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server.Body.Systems;
using Content.Server.Medical;
using Content.Server._WF.Wolfmed.Autodoc;
using Content.Server._WF.Wolfmed.Life;
using Content.Shared._Onyx.Body.Systems;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Wolfmed.Autodoc;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Mobs.Systems;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// The owner's playtest findings: an arrested patient nothing would shock, an autofix pod that operated for
/// ever, a pod that held for a patient it had been asked to operate on precisely because they were dead,
/// and queue reorder buttons that sent messages the server threw away.
/// </summary>
[TestFixture]
[TestOf(typeof(AutodocSystem))]
public sealed class WolfmedPlaytestFixesTest : GameTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WolfmedFixesAutodoc
  parent: WFMachineAutodoc
  suffix: playtest fixes
  components:
  - type: Autodoc
    stepTime: 0.02
    bestStepTime: 0.02
    malfunctionChance: 0
    autoPlanInterval: 0.2
  - type: ApcPowerReceiver
    needsPower: false

- type: entity
  id: WolfmedFixesBloodJug
  parent: Jug
  suffix: blood
  components:
  - type: SolutionContainerManager
    solutions:
      beaker:
        maxVol: 400
        reagents:
        - ReagentId: Blood
          Quantity: 400
";

    /// <summary>
    /// A heart that stopped on a body with blood in it is restarted by a medic's paddles, through the real
    /// defibrillator path rather than the revival system on its own.
    /// </summary>
    [Test]
    public async Task HandDefibrillatorRevivesAPlainArrestTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var life = entities.System<WolfmedLifeSystem>();
            var revival = entities.System<WolfmedRevivalSystem>();
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var medic = entities.SpawnEntity("MobHuman", map.GridCoords);
            var defib = entities.SpawnEntity("Defibrillator", map.GridCoords);

            Assert.That(life.StartArrest(body, "test"), Is.True, "the fixture needs an arrested patient.");
            Assert.That(revival.GetRefusal(body), Is.Null,
                "a patient with a heart, a brain and their blood was refused before the paddles even charged.");

            entities.System<ItemToggleSystem>().TryActivate(defib, medic);
            revival.ForcedRoll = 0f;
            entities.System<DefibrillatorSystem>().Zap(defib, body, medic);
            revival.ForcedRoll = null;

            Assert.Multiple(() =>
            {
                Assert.That(life.InArrest(body), Is.False, "the hand defibrillator did not restart the heart.");
                Assert.That(entities.System<MobStateSystem>().IsDead(body), Is.False);
            });
        });
    }

    /// <summary>
    /// The trap the owner hit: arrest starts at 30% blood and the paddles wanted more than 40%, so a patient
    /// who arrested from blood loss could never be shocked. The refusal now names the blood, and the pod
    /// transfuses before it charges instead of announcing a gate it is standing next to the cure for.
    /// </summary>
    [Test]
    public async Task BloodLossArrestSaysBloodAndThePodTransfusesFirstTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var autodoc = entities.System<AutodocSystem>();
            var life = entities.System<WolfmedLifeSystem>();
            var revival = entities.System<WolfmedRevivalSystem>();
            var slots = entities.System<ItemSlotsSystem>();
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);

            // M1a: the gate is 25%, under the 30% arrest, so the refusal needs a body bled further than that.
            Bleed(entities, body, 0.20f);
            life.Tick(body, 1f);

            Assert.Multiple(() =>
            {
                Assert.That(life.InArrest(body), Is.True, "a body at 20% blood kept a pulse.");
                Assert.That(revival.GetRefusal(body), Is.EqualTo("wolfmed-defib-no-blood"),
                    "the paddles refused for some other reason than the blood.");
            });

            var pod = Pod(entities, map);
            Assert.That(slots.TryInsert(pod.Owner, AutodocComponent.ModuleSlotId,
                entities.SpawnEntity("WFAutodocDefibModule", map.GridCoords), null), Is.True);
            Assert.That(slots.TryInsert(pod.Owner, AutodocComponent.ReservoirSlotIds[0],
                entities.SpawnEntity("WolfmedFixesBloodJug", map.GridCoords), null), Is.True);
            Assert.That(autodoc.TryInsert(pod, body), Is.True);

            revival.ForcedRoll = 0f;
            Assert.That(autodoc.TryDefibrillateOccupant(pod, body), Is.True,
                "the pod said it could not shock instead of transfusing the patient it was holding.");
            revival.ForcedRoll = null;

            Assert.That(life.InArrest(body), Is.False, "the heart never restarted.");
        });
    }

    /// <summary>
    /// AUTO on a fractured arm runs once and stops. Every run leaves an incision and a suture behind it, and
    /// a planner that counted its own handiwork as work planned, operated and planned again for ever. A
    /// bleed that starts afterwards is a real change and does get one more run.
    /// </summary>
    [Test]
    public async Task AutofixRunsOnceAndThenOnlyForSomethingNewTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        Entity<AutodocComponent> pod = default;
        EntityUid body = default;

        await server.WaitAssertion(() =>
        {
            // M1a D: station air. In the test map's vacuum, barotrauma kept landing fresh damage on the patient,
            // which the pod rightly treated as new work; the test failed about half its runs, before M1a too.
            new Scenarios.WolfmedScenario(entities).SetAir(map.MapUid, true);

            var autodoc = entities.System<AutodocSystem>();
            var slots = entities.System<ItemSlotsSystem>();
            body = entities.SpawnEntity("MobHuman", map.GridCoords);
            Damage(entities, body, TargetBodyPart.LeftArm, "Blunt", 60);

            pod = Pod(entities, map);
            Assert.That(slots.TryInsert(pod.Owner, AutodocComponent.AutofixSlotId,
                entities.SpawnEntity("WFAutodocAutofixModule", map.GridCoords), null), Is.True);
            autodoc.SetAuto(pod, true);
            Assert.That(autodoc.TryInsert(pod, body), Is.True);
        });

        // Wait for the first run to finish rather than for a fixed time: the test map has no air, and a
        // patient left in it long enough grows organ damage that is genuinely new work.
        var state = AutodocState.Idle;
        for (var attempt = 0; attempt < 14 && state != AutodocState.Complete; attempt++)
        {
            await Pair.RunTicksSync(50);
            await server.WaitPost(() => state = pod.Comp!.State);
        }

        await server.WaitAssertion(() =>
        {
            Assert.That(state, Is.EqualTo(AutodocState.Complete),
                $"the first run never finished (step {pod.Comp!.CurrentStep}, {Describe(entities, pod, body)}).");

            // The pod cauterises, and a cautery burns whatever part the damage lands on. Every one of those
            // is its own doing, and none of them is a reason to operate again.
            var plan = entities.System<AutodocSystem>().Plan(pod, body);
            Assert.Multiple(() =>
            {
                Assert.That(plan.Any(entry => entry.Part == TargetBodyPart.LeftArm), Is.False,
                    $"the pod planned more work on the arm it had just finished: {Describe(entities, pod, body)}");
                Assert.That(plan.Any(entry => IsWoundWork(entry.Surgery)), Is.False,
                    $"the pod planned to treat its own incisions and burns: {Describe(entities, pod, body)}");
                Assert.That(pod.Comp.AutoSaidNothing, Is.True, "it never said it had nothing left to do.");
            });
        });

        // And it does not quietly start one: this is the loop the owner watched.
        await Pair.RunTicksSync(90);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(pod.Comp!.Queue.Any(queued => queued.Part == TargetBodyPart.LeftArm), Is.False,
                    $"the pod queued the arm again: {Describe(entities, pod, body)}");
                Assert.That(pod.Comp.Queue.Any(queued => IsWoundWork(queued.Surgery)), Is.False,
                    $"the pod queued itself a tend out of its own sutures: {Describe(entities, pod, body)}");
            });
        });

        // Something the pod did not do, and there is work again. The cut has to land while the pod is idle
        // and out of its grace window, because anything that turns up on a patient it is working on is
        // treated as its own doing: the test map's lack of air keeps giving it organ damage to go back for.
        var ready = false;
        for (var attempt = 0; attempt < 20 && !ready; attempt++)
        {
            await Pair.RunTicksSync(20);
            await server.WaitPost(() => ready =
                pod.Comp!.State is AutodocState.Idle or AutodocState.Complete &&
                server.ResolveDependency<IGameTiming>().CurTime >= pod.Comp.PodWoundUntil);
        }

        Assert.That(ready, Is.True, "the pod never stopped long enough to be handed a new injury.");
        await server.WaitPost(() => Damage(entities, body, TargetBodyPart.RightFoot, "Slash", 25));
        await Pair.RunTicksSync(3);

        await server.WaitAssertion(() =>
        {
            Assert.That(entities.System<AutodocSystem>().Plan(pod, body)
                    .Any(entry => entry.Part == TargetBodyPart.RightFoot && IsWoundWork(entry.Surgery)), Is.True,
                $"a fresh cut nothing in the pod made was not treatable work: {Describe(entities, pod, body)}");
        });
    }

    /// <summary>A procedure whose only reason to run is a wound, which is what the pod leaves behind it.</summary>
    private static bool IsWoundWork(EntProtoId surgery) =>
        surgery.Id.Contains("SurgeryTendWounds") || surgery.Id == "WFSurgeryStopBleeding" ||
        surgery.Id == "WFSurgeryGraftSkin";

    /// <summary>
    /// Repairing a brain means operating on a corpse, which is the whole point of the procedure. The pod
    /// used to call for an operator and hold on the patient it had been asked to open.
    /// </summary>
    [Test]
    public async Task BrainDeadOccupantIsOperatedOnWithoutHoldingTest()
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
            var life = entities.System<WolfmedLifeSystem>();
            var slots = entities.System<ItemSlotsSystem>();
            body = entities.SpawnEntity("MobHuman", map.GridCoords);

            var brain = life.GetBrainOrgan(body);
            Assert.That(brain, Is.Not.Null, "the fixture needs a brain to destroy.");
            entities.System<OrganHealthSystem>().SetHealth(brain!.Value, FixedPoint2.Zero);

            pod = Pod(entities, map);
            Assert.That(slots.TryInsert(pod.Owner, AutodocComponent.DiskSlotId,
                entities.SpawnEntity("WFAutodocProgramDiskNeuro", map.GridCoords), null), Is.True);
            Assert.That(autodoc.TryInsert(pod, body), Is.True);
        });

        await Pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var autodoc = entities.System<AutodocSystem>();
            Assert.That(entities.System<MobStateSystem>().IsDead(body), Is.True,
                "the fixture needs a brain-dead patient.");
            Assert.That(autodoc.TryQueue(pod, "WFSurgeryRepairBrain", TargetBodyPart.Head), Is.True);
            Assert.That(autodoc.TryStart(pod, null), Is.True);
        });

        await Pair.RunTicksSync(600);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(pod.Comp!.State, Is.Not.EqualTo(AutodocState.Paused),
                    "the pod held for a patient it was asked to operate on because they were dead.");
                Assert.That(entities.System<WolfmedLifeSystem>().GetBrainActivity(body), Is.GreaterThan(0.9f),
                    $"the brain was never repaired (state {pod.Comp.State}, step {pod.Comp.CurrentStep}).");
            });
        });
    }

    /// <summary>
    /// A patient who dies UNDER the knife is still a reason to stop and call for help, once, and the
    /// operator can send the pod on again instead of watching it hold for ever.
    /// </summary>
    [Test]
    public async Task DeathDuringAProcedureHoldsAndResumesTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        Entity<AutodocComponent> pod = default;
        EntityUid body = default;
        EntityUid leg = default;

        await server.WaitAssertion(() =>
        {
            var autodoc = entities.System<AutodocSystem>();
            body = entities.SpawnEntity("MobHuman", map.GridCoords);
            Damage(entities, body, TargetBodyPart.LeftLeg, "Blunt", 60);
            leg = Part(entities, body, BodyPartType.Leg, BodyPartSymmetry.Left);

            pod = Pod(entities, map);
            Assert.That(autodoc.TryInsert(pod, body), Is.True);
        });

        await Pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var autodoc = entities.System<AutodocSystem>();
            Assert.That(autodoc.TryQueue(pod, "WFSurgeryMendFracture", TargetBodyPart.LeftLeg), Is.True);
            Assert.That(autodoc.TryStart(pod, null), Is.True);
            Assert.That(pod.Comp!.OccupantWasDead, Is.False, "the fixture needs a living patient.");
        });

        await Pair.RunTicksSync(5);
        await server.WaitPost(() => entities.System<WolfmedLifeSystem>().Kill(body));
        await Pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            Assert.That(pod.Comp!.State, Is.EqualTo(AutodocState.Paused),
                "the pod carried on operating on a patient who died under it.");
        });

        // HOLD again is RESUME, and the same death must not stop it a second time.
        await server.WaitPost(() => entities.System<AutodocSystem>().Control(pod, AutodocControl.Pause, null));
        await Pair.RunTicksSync(600);

        await server.WaitAssertion(() =>
        {
            var fracture = entities.System<WoundFractureSystem>().GetFracture(leg);
            Assert.Multiple(() =>
            {
                Assert.That(pod.Comp!.State, Is.Not.EqualTo(AutodocState.Paused),
                    "RESUME did not send the pod on again.");
                Assert.That(fracture == null ||
                            fracture.Value.Comp2.Treatment == FractureTreatment.Mended,
                    Is.True, $"the procedure never finished (state {pod.Comp.State}).");
            });
        });
    }

    /// <summary>
    /// The reorder buttons. Idle, anything swaps with its neighbour; mid-run the entry under the knife is
    /// pinned and everything behind it still moves.
    /// </summary>
    [Test]
    public async Task QueueMoveReordersTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        Entity<AutodocComponent> pod = default;

        await server.WaitAssertion(() =>
        {
            var autodoc = entities.System<AutodocSystem>();
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            foreach (var target in new[] { TargetBodyPart.LeftLeg, TargetBodyPart.RightLeg, TargetBodyPart.LeftArm })
                Damage(entities, body, target, "Blunt", 60);

            pod = Pod(entities, map);
            Assert.That(autodoc.TryInsert(pod, body), Is.True);
        });

        await Pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var autodoc = entities.System<AutodocSystem>();
            foreach (var target in new[] { TargetBodyPart.LeftLeg, TargetBodyPart.RightLeg, TargetBodyPart.LeftArm })
                Assert.That(autodoc.TryQueue(pod, "WFSurgeryMendFracture", target), Is.True);

            var order = pod.Comp!.Queue.Select(queued => queued.Part).ToList();
            Assert.That(autodoc.TryMoveQueued(pod, 1, true), Is.True, "an idle queue refused a swap.");
            Assert.That(pod.Comp.Queue.Select(queued => queued.Part),
                Is.EqualTo(new[] { order[1], order[0], order[2] }), "the swap did not happen.");

            Assert.That(autodoc.TryMoveQueued(pod, 0, true), Is.False, "the top entry moved off the top.");
            Assert.That(autodoc.TryStart(pod, null), Is.True);
        });

        await Pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var autodoc = entities.System<AutodocSystem>();
            var order = pod.Comp!.Queue.Select(queued => queued.Part).ToList();
            Assert.That(order, Has.Count.EqualTo(3), "the fixture needs the whole queue still waiting.");

            Assert.Multiple(() =>
            {
                Assert.That(autodoc.TryMoveQueued(pod, 1, true), Is.False,
                    "an entry was moved onto the one under the knife.");
                Assert.That(autodoc.TryMoveQueued(pod, 0, false), Is.False,
                    "the procedure being performed was pushed down the queue.");
            });

            Assert.That(autodoc.TryMoveQueued(pod, 2, true), Is.True,
                "nothing behind the running procedure could be reordered.");
            Assert.That(pod.Comp.Queue.Select(queued => queued.Part),
                Is.EqualTo(new[] { order[0], order[2], order[1] }));
        });
    }

    private static string Describe(IEntityManager entities, Entity<AutodocComponent> pod, EntityUid body)
    {
        var plan = string.Join(", ", entities.System<AutodocSystem>().Plan(pod, body)
            .Select(entry => $"{entry.Surgery.Id}@{entry.Part}"));
        var wounds = new System.Text.StringBuilder();
        foreach (var (part, _) in entities.System<SharedBodySystem>().GetBodyChildren(body))
        {
            if (!entities.TryGetComponent(part, out WoundableComponent? woundable))
                continue;

            foreach (var wound in entities.System<WoundSystem>().GetWounds((part, woundable)))
                wounds.Append($"{entities.GetComponent<MetaDataComponent>(part).EntityName}:")
                    .Append(wound.Comp.Prototype.Id).Append('/').Append(wound.Comp.State).Append('/')
                    .Append(entities.HasComponent<WolfmedPodWoundComponent>(wound) ? "pod" : "real").Append(' ');
        }

        return $"PLAN[{plan}] WOUNDS[{wounds}] FAILED[{string.Join(", ", pod.Comp.FailedProcedures)}] " +
               $"STATE[{pod.Comp.State}] QUEUE[{string.Join(", ", pod.Comp.Queue.Select(q => $"{q.Surgery.Id}@{q.Part}"))}]";
    }

    private static Entity<AutodocComponent> Pod(IEntityManager entities, TestMapData map)
    {
        var pod = entities.SpawnEntity("WolfmedFixesAutodoc", map.GridCoords);
        return (pod, entities.GetComponent<AutodocComponent>(pod));
    }

    private static void Damage(IEntityManager entities, EntityUid body, TargetBodyPart target, string type, int amount)
    {
        var spec = new DamageSpecifier
        {
            DamageDict = { [new ProtoId<DamageTypePrototype>(type)] = FixedPoint2.New(amount) },
        };

        entities.System<DamageableSystem>().TryChangeDamage(body, spec, origin: null, targetPart: target);
    }

    private static EntityUid Part(IEntityManager entities, EntityUid body, BodyPartType type, BodyPartSymmetry symmetry)
    {
        return entities.System<SharedBodySystem>().GetBodyChildren(body)
            .Single(part => part.Component.PartType == type && part.Component.Symmetry == symmetry)
            .Id;
    }

    /// <summary>Walks the bloodstream down, the way WolfmedBrainTest does: the component is read-only here.</summary>
    private static void Bleed(IEntityManager entities, EntityUid body, float target)
    {
        var bloodstream = entities.System<BloodstreamSystem>();
        for (var step = 0; step < 200 && bloodstream.GetBloodLevelPercentage(body) > target; step++)
            bloodstream.TryModifyBloodLevel(body, FixedPoint2.New(-10));
    }
}
