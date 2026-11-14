#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._WF.Wolfmed.Autodoc;
using Content.Server._WF.Wolfmed.Hud;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Wolfmed.Autodoc;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.Hud;
using Content.Shared._WF.Wolfmed.Surgery;
using Content.Shared.Body.Part;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.IgnitionSource;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Tools.Components;
using NUnit.Framework;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Wolfmed.Scenarios;

/// <summary>
/// Playtest 3, IPC round 2: the pod left an IPC's chassis damage where it was. It now welds and rewires a chassis the way
/// a hand welder and a cable coil do, on the Mechanical triage step, and the organic tends stay off machine parts.
/// </summary>
[TestFixture]
[TestOf(typeof(AutodocSystem))]
public sealed class WolfmedPodWeldsChassisTest : GameTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WolfmedWeldTestAutodoc
  parent: WFMachineAutodoc
  suffix: weld test
  components:
  - type: Autodoc
    stepTime: 0.02
    bestStepTime: 0.02
    malfunctionChance: 0
    autoPlanInterval: 0.2
  - type: ApcPowerReceiver
    needsPower: false
";

    /// <summary>Every wound the two chassis procedures are for.</summary>
    private static readonly string[] ChassisWounds =
    [
        "IpcMechanicalDamageWound", "WFWolfmedDentWound", "WFWolfmedBreachWound", "CyberneticMechanicalDamageWound",
        "WFWolfmedShortCircuitWound", "ElectricalWound",
    ];

    /// <summary>The HUD rows those wounds put on the readout.</summary>
    private static readonly string[] ChassisRows =
    [
        "wolfmed-synthetic-line-frame", "wolfmed-synthetic-line-dent", "wolfmed-synthetic-line-breach",
        "wolfmed-synthetic-line-short-circuit",
    ];

    private readonly List<string> _notes = new();

    [TearDown]
    public void WriteNotes()
    {
        foreach (var line in _notes)
            TestContext.Out.WriteLine(line);
    }

    /// <summary>
    /// <c>PodWeldsChassisTest</c>: an IPC with a 90-severity generic wound on the torso, a dent on an arm, a bleeding
    /// breach on a leg and a short circuit on the other arm, and a second one hurt by real hits (the welder's damage
    /// path rather than its wound path). AUTO clears all four kinds on both, the readout rows for them go, each pod says
    /// QUEUE COMPLETE once and abandons nothing, its welder never lights, and a human's plan has no machine work in it
    /// and keeps its tends.
    /// </summary>
    [Test]
    public async Task PodWeldsChassisTest()
    {
        await OverrideCVar(Side.Server, WolfmedCVars.Consciousness, true);
        var map = await Pair.CreateTestMap();
        EntityUid ipc = default, battered = default, human = default;
        Entity<AutodocComponent> pod = default, batteredPod = default, humanPod = default;

        await Server.WaitPost(() =>
        {
            var s = new WolfmedScenario(SEntMan);
            s.SetAir(map.MapUid, true);
            s.KeepGrid(map.Grid);
            ipc = SEntMan.SpawnEntity("MobIPC", map.GridCoords);
            battered = SEntMan.SpawnEntity("MobIPC", map.GridCoords);
            human = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
        });
        await RunSeconds(1);

        await Server.WaitAssertion(() =>
        {
            var s = new WolfmedScenario(SEntMan);
            var wounds = SEntMan.System<WoundSystem>();
            var damage = SEntMan.System<DamageableSystem>();

            // The spec's chassis, wound by wound.
            wounds.CreateOrMergeWound(s.Part(ipc, BodyPartType.Torso), "IpcMechanicalDamageWound", FixedPoint2.New(90));
            wounds.CreateOrMergeWound(s.Part(ipc, BodyPartType.Arm, BodyPartSymmetry.Left), "WFWolfmedDentWound", FixedPoint2.New(40));
            var breach = wounds.CreateOrMergeWound(s.Part(ipc, BodyPartType.Leg, BodyPartSymmetry.Left), "WFWolfmedBreachWound",
                FixedPoint2.New(30));
            wounds.CreateOrMergeWound(s.Part(ipc, BodyPartType.Arm, BodyPartSymmetry.Right), "WFWolfmedShortCircuitWound",
                FixedPoint2.New(30));
            Assert.That(breach, Is.Not.Null);
            Assert.That(SEntMan.TryGetComponent<WoundBleedingComponent>(breach!.Value, out var bleed) && bleed.CurrentRate > 0f,
                Is.True, "the fixture breach does not bleed.");

            // The same kinds of harm from real hits, so the welder and the coil have damage to remove.
            damage.TryChangeDamage(battered, WolfmedScenario.Spec("Blunt", 60), targetPart: TargetBodyPart.Torso);
            damage.TryChangeDamage(battered, WolfmedScenario.Spec("Piercing", 30), targetPart: TargetBodyPart.LeftLeg);
            damage.TryChangeDamage(battered, WolfmedScenario.Spec("Shock", 30), targetPart: TargetBodyPart.RightArm);

            // An organic control: a bruised arm and a cut leg.
            damage.TryChangeDamage(human, WolfmedScenario.Spec("Blunt", 40), targetPart: TargetBodyPart.LeftArm);
            damage.TryChangeDamage(human, WolfmedScenario.Spec("Slash", 25), targetPart: TargetBodyPart.RightLeg);

            _notes.Add("before: " + Describe(ipc) + " || battered: " + Describe(battered));

            var hud = SEntMan.System<WolfmedSyntheticHudSystem>();
            var readout = SEntMan.EnsureComponent<WolfmedSyntheticHudComponent>(ipc);
            hud.Refresh((ipc, readout));
            Assert.Multiple(() =>
            {
                Assert.That(readout.Faults.Any(f => f.Part == TargetBodyPart.Torso && f.Line == "wolfmed-synthetic-line-frame"), Is.True,
                    "the fixture's torso has no FRAME DAMAGE row.");
                Assert.That(readout.Faults.Any(f => f.Part == TargetBodyPart.LeftArm && f.Line == "wolfmed-synthetic-line-dent"), Is.True);
                Assert.That(readout.Faults.Any(f => f.Part == TargetBodyPart.LeftLeg && f.Line == "wolfmed-synthetic-line-breach"), Is.True);
                Assert.That(readout.Faults.Any(f => f.Part == TargetBodyPart.RightArm && f.Line == "wolfmed-synthetic-line-short-circuit"),
                    Is.True);
            });

            pod = Pod(map);
            batteredPod = Pod(map);
            humanPod = Pod(map);
            var autodoc = SEntMan.System<AutodocSystem>();
            Assert.That(autodoc.TryInsert(pod, ipc), Is.True);
            Assert.That(autodoc.TryInsert(batteredPod, battered), Is.True);
            Assert.That(autodoc.TryInsert(humanPod, human), Is.True);

            // The plan: the chassis work on the Mechanical step, no organic tend anywhere on a chassis.
            var plan = autodoc.Plan(pod, ipc);
            var humanPlan = autodoc.Plan(humanPod, human);
            _notes.Add("plan: " + Format(plan) + " || battered plan: " + Format(autodoc.Plan(batteredPod, battered)) +
                       " || human plan: " + Format(humanPlan));
            Assert.Multiple(() =>
            {
                Assert.That(plan.Any(e => e.Surgery == "WFSurgeryWeldChassis" && e.Part == TargetBodyPart.Torso), Is.True);
                Assert.That(plan.Any(e => e.Surgery == "WFSurgeryWeldChassis" && e.Part == TargetBodyPart.LeftArm), Is.True);
                Assert.That(plan.Any(e => e.Surgery == "WFSurgeryWeldChassis" && e.Part == TargetBodyPart.LeftLeg), Is.True);
                Assert.That(plan.Any(e => e.Surgery == "WFSurgeryRewireChassis" && e.Part == TargetBodyPart.RightArm), Is.True);
                Assert.That(plan.Any(e => e.Surgery.Id.Contains("SurgeryTendWounds")), Is.False,
                    "an organic tend was planned on a chassis.");

                Assert.That(humanPlan.Any(e => e.Surgery == "WFSurgeryWeldChassis" || e.Surgery == "WFSurgeryRewireChassis"), Is.False,
                    "machine work was planned on flesh.");
                Assert.That(humanPlan.Any(e => e.Surgery.Id.Contains("SurgeryTendWounds") && e.Part == TargetBodyPart.LeftArm),
                    Is.True, "the human's bruised arm lost its tend.");
            });

            foreach (var ent in new[] { pod, batteredPod })
            {
                Assert.That(SEntMan.System<ItemSlotsSystem>().TryInsert(ent.Owner, AutodocComponent.AutofixSlotId,
                    SEntMan.SpawnEntity("WFAutodocAutofixModule", map.GridCoords), null), Is.True);
                autodoc.SetAuto(ent, true);
            }
        });

        // Tick by tick, so every QUEUE COMPLETE is counted: it is spoken exactly when a run reaches Complete.
        var completions = new Dictionary<EntityUid, int> { [pod.Owner] = 0, [batteredPod.Owner] = 0 };
        var previous = new Dictionary<EntityUid, AutodocState> { [pod.Owner] = AutodocState.Idle, [batteredPod.Owner] = AutodocState.Idle };
        var welderLit = false;
        for (var tick = 0; tick < 1500; tick++)
        {
            await Pair.RunTicksSync(1);
            var done = false;
            await Server.WaitPost(() =>
            {
                foreach (var ent in new[] { pod, batteredPod })
                {
                    if (ent.Comp.State == AutodocState.Complete && previous[ent.Owner] != AutodocState.Complete)
                        completions[ent.Owner]++;

                    previous[ent.Owner] = ent.Comp.State;
                    welderLit |= PodWelderLit(ent);
                }

                done = pod.Comp!.AutoSaidNothing && batteredPod.Comp!.AutoSaidNothing;
            });

            if (done)
                break;
        }

        await Server.WaitAssertion(() =>
        {
            var hud = SEntMan.System<WolfmedSyntheticHudSystem>();
            var readout = SEntMan.EnsureComponent<WolfmedSyntheticHudComponent>(ipc);
            hud.Refresh((ipc, readout));
            var batteredReadout = SEntMan.EnsureComponent<WolfmedSyntheticHudComponent>(battered);
            hud.Refresh((battered, batteredReadout));

            _notes.Add($"after: {Describe(ipc)} || battered: {Describe(battered)}");
            _notes.Add($"completions {completions[pod.Owner]}/{completions[batteredPod.Owner]}, failed " +
                       $"[{string.Join(", ", pod.Comp!.FailedProcedures)}] [{string.Join(", ", batteredPod.Comp!.FailedProcedures)}], " +
                       $"states {pod.Comp.State}/{batteredPod.Comp.State}");

            Assert.Multiple(() =>
            {
                foreach (var body in new[] { ipc, battered })
                {
                    Assert.That(ChassisWoundsLeft(body), Is.Empty, $"the pod left chassis wounds: {Describe(body)}");
                }

                Assert.That(readout.Faults.Where(f => ChassisRows.Contains(f.Line)), Is.Empty,
                    "the readout still carries rows for the welded wounds.");
                Assert.That(batteredReadout.Faults.Where(f => ChassisRows.Contains(f.Line)), Is.Empty);

                Assert.That(completions[pod.Owner], Is.EqualTo(1), "QUEUE COMPLETE was not said exactly once.");
                Assert.That(completions[batteredPod.Owner], Is.EqualTo(1), "QUEUE COMPLETE was not said exactly once.");
                Assert.That(pod.Comp.FailedProcedures, Is.Empty, "the stall guard abandoned a procedure that was working.");
                Assert.That(batteredPod.Comp.FailedProcedures, Is.Empty, "the stall guard abandoned a procedure that was working.");
                Assert.That(welderLit, Is.False, "the pod lit its own welder.");
            });
        });
    }

    private Entity<AutodocComponent> Pod(TestMapData map)
    {
        var uid = SEntMan.SpawnEntity("WolfmedWeldTestAutodoc", map.GridCoords);
        return (uid, SEntMan.GetComponent<AutodocComponent>(uid));
    }

    /// <summary>The pod's own welder as an ignition source: lit, or toggled on.</summary>
    private bool PodWelderLit(Entity<AutodocComponent> pod)
    {
        if (!SEntMan.System<SharedContainerSystem>().TryGetContainer(pod, AutodocComponent.ToolContainerId, out var tools))
            return false;

        foreach (var tool in tools.ContainedEntities)
        {
            if (!SEntMan.HasComponent<WelderComponent>(tool))
                continue;

            if (SEntMan.TryGetComponent<IgnitionSourceComponent>(tool, out var ignition) && ignition.Ignited ||
                SEntMan.System<ItemToggleSystem>().IsActivated(tool))
                return true;
        }

        return false;
    }

    private List<string> ChassisWoundsLeft(EntityUid body)
    {
        var conditions = SEntMan.System<WolfmedSurgeryConditionSystem>();
        var left = new List<string>();
        foreach (var (part, bodyPart) in SEntMan.System<Content.Shared.Body.Systems.SharedBodySystem>().GetBodyChildren(body))
        {
            foreach (var wound in ChassisWounds)
            {
                if (conditions.FindWound(part, wound) != null)
                    left.Add($"{bodyPart.PartType}{bodyPart.Symmetry}:{wound}");
            }
        }

        return left;
    }

    private string Describe(EntityUid body)
    {
        var sb = new StringBuilder();
        foreach (var (part, bodyPart) in SEntMan.System<Content.Shared.Body.Systems.SharedBodySystem>().GetBodyChildren(body))
        {
            var partWounds = SEntMan.System<WoundSystem>().GetWounds(part).ToList();
            var damage = SEntMan.GetComponent<DamageableComponent>(part).TotalDamage;
            if (partWounds.Count == 0 && damage <= FixedPoint2.Zero)
                continue;

            sb.Append($"{bodyPart.PartType}{bodyPart.Symmetry}[{damage}:");
            foreach (var wound in partWounds)
                sb.Append($" {wound.Comp.Prototype.Id}={wound.Comp.Severity}/{wound.Comp.State}");
            sb.Append("] ");
        }

        return sb.ToString();
    }

    private static string Format(List<AutodocProcedureEntry> plan) =>
        string.Join(", ", plan.Select(e => $"{e.Surgery.Id}@{e.Part}"));
}
