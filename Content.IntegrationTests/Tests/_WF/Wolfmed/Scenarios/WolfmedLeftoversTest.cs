#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._WF.Wolfmed.Autodoc;
using Content.Server._WF.Wolfmed.Life;
using Content.Server._WF.Wolfmed.Wounds;
using Content.Shared._Onyx.Body.Systems;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Medical.Surgery;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Wolfmed.Autodoc;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.Consciousness;
using Content.Shared._WF.Wolfmed.Surgery;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Content.Shared.Medical;
using Content.Shared.Mobs.Systems;
using NUnit.Framework;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Wolfmed.Scenarios;

/// <summary>
/// M6 (plan §12 M6, OD18): being hit interrupts treatment, and the leftovers the milestone reports flagged: the pod
/// repairs a machine's core, the pod's progress check sees a clamp closing a bleed, and a synth's Poison runs no toxin
/// route.
/// </summary>
[TestFixture]
[TestOf(typeof(WolfmedDoAfterInterruptSystem))]
public sealed class WolfmedLeftoversTest : GameTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WolfmedLeftoversAutodoc
  parent: WFMachineAutodoc
  suffix: leftovers test
  components:
  - type: Autodoc
    stepTime: 0.02
    bestStepTime: 0.02
    malfunctionChance: 0
    autoPlanInterval: 0.2
  - type: ApcPowerReceiver
    needsPower: false
";

    private readonly List<string> _notes = new();

    private void Note(string line) => _notes.Add(line);

    [TearDown]
    public void WriteNotes()
    {
        foreach (var line in _notes)
            TestContext.Out.WriteLine(line);
    }

    private async Task Pin()
    {
        await OverrideCVar(Side.Server, WolfmedCVars.Consciousness, true);
        await OverrideCVar(Side.Server, WolfmedCVars.ConsciousnessHysteresis, 0.1f);
        await OverrideCVar(Side.Server, WolfmedCVars.DoAfterInterruptDamage, 10f);
        await OverrideCVar(Side.Server, WolfmedCVars.ConsciousnessToxinDown, 60f);
        await OverrideCVar(Side.Server, WolfmedCVars.ConsciousnessToxinOut, 120f);
        await OverrideCVar(Side.Server, WolfmedCVars.ToxinClearance, 0.1f);
    }

    private EntityUid Part(EntityUid body, BodyPartType type, BodyPartSymmetry symmetry = BodyPartSymmetry.None) =>
        SEntMan.System<SharedBodySystem>().GetBodyChildren(body)
            .First(part => part.Component.PartType == type && part.Component.Symmetry == symmetry).Id;

    /// <summary>A long do-after of the given kind. Its event goes to a bare entity, so ending it runs no handler.</summary>
    private DoAfterId Start(EntityUid user, EntityUid target, DoAfterEvent ev)
    {
        var sink = SEntMan.SpawnEntity(null, SEntMan.GetComponent<TransformComponent>(user).Coordinates);
        var args = new DoAfterArgs(SEntMan, user, TimeSpan.FromSeconds(60), ev, sink, target: target);
        Assert.That(SEntMan.System<SharedDoAfterSystem>().TryStartDoAfter(args, out var id), Is.True, "the do-after did not start.");
        return id!.Value;
    }

    private DoAfterStatus Status(DoAfterId id) => SEntMan.System<SharedDoAfterSystem>().GetStatus(id);

    private void Hit(EntityUid body, EntityUid attacker, string type, float amount) =>
        SEntMan.System<DamageableSystem>().TryChangeDamage(body, WolfmedScenario.Spec(type, amount), origin: attacker,
            targetPart: TargetBodyPart.LeftArm);

    /// <summary>
    /// <c>HitInterruptsSelfTreatmentTest</c> (plan §12 M6, OD18, P17): a part hit of 10 cancels a self-bandage and a
    /// surgery step the hit body is performing; a hit of 9, a bleed tick, a fire tick and a systemic tick do not.
    /// </summary>
    [Test]
    public async Task HitInterruptsSelfTreatmentTest()
    {
        await Pin();
        var map = await Pair.CreateTestMap();
        EntityUid patient = default, surgeon = default, attacker = default;

        await Server.WaitPost(() =>
        {
            patient = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            surgeon = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            attacker = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
        });
        await RunSeconds(2);

        await Server.WaitAssertion(() =>
        {
            var damage = SEntMan.System<DamageableSystem>();
            var bandage = Start(patient, patient, new HealingDoAfterEvent());
            var surgery = Start(surgeon, patient, new SurgeryDoAfterEvent("WFSurgeryStopBleeding", "WFSurgeryStepSutureBleeding"));

            // A bleed tick, the bloodstream's shape: systemic, never a part hit.
            damage.TryChangeDamage(patient, WolfmedScenario.Spec("Bloodloss", 15), ignoreResistances: true,
                interruptsDoAfters: false);
            // A fire tick, the flammable system's shape: a part hit that does not interrupt.
            damage.TryChangeDamage(patient, WolfmedScenario.Spec("Heat", 15), interruptsDoAfters: false);
            damage.TryChangeDamage(surgeon, WolfmedScenario.Spec("Heat", 15), interruptsDoAfters: false);
            // A systemic tick: Poison lands on no part, however it is dealt.
            damage.TryChangeDamage(patient, WolfmedScenario.Spec("Poison", 15));
            // A hit of 9.
            Hit(patient, attacker, "Blunt", 9);
            Hit(surgeon, attacker, "Blunt", 9);

            Assert.Multiple(() =>
            {
                Assert.That(Status(bandage), Is.EqualTo(DoAfterStatus.Running), "a tick or a hit of 9 stopped the bandage.");
                Assert.That(Status(surgery), Is.EqualTo(DoAfterStatus.Running), "a tick or a hit of 9 stopped the surgery.");
            });

            // A hit of 10 on each.
            Hit(patient, attacker, "Blunt", 10);
            Assert.Multiple(() =>
            {
                Assert.That(Status(bandage), Is.EqualTo(DoAfterStatus.Cancelled), "a hit of 10 did not stop the bandage.");
                Assert.That(Status(surgery), Is.EqualTo(DoAfterStatus.Running),
                    "a hit on the patient stopped the surgeon's step: only the hit body's own do-afters stop.");
            });

            Hit(surgeon, attacker, "Blunt", 10);
            Assert.That(Status(surgery), Is.EqualTo(DoAfterStatus.Cancelled), "a hit of 10 did not stop the surgery step.");
        });
    }

    /// <summary>
    /// M4's leftover: the pod could not repair a machine's core. With the neuro disk it now runs WFSurgeryRepairCore on an
    /// IPC's chassis and WFSurgeryRepairSynthCore on a synth's head, with its own wrench, multitool and welder, and the
    /// restart button brings each back.
    /// </summary>
    [Test]
    public async Task PodRepairsACoreTest()
    {
        await Pin();
        var map = await Pair.CreateTestMap();
        var life = SEntMan.System<WolfmedLifeSystem>();
        var mobState = SEntMan.System<MobStateSystem>();
        EntityUid ipc = default, synth = default;
        Entity<AutodocComponent> ipcPod = default, synthPod = default;

        await Server.WaitPost(() =>
        {
            var scenario = new WolfmedScenario(SEntMan);
            scenario.SetAir(map.MapUid, true);
            scenario.KeepGrid(map.Grid);
            ipc = SEntMan.SpawnEntity("MobIPC", map.GridCoords);
            synth = SEntMan.SpawnEntity("MobSynth", map.GridCoords);
        });
        await RunSeconds(2);

        await Server.WaitPost(() =>
        {
            SEntMan.System<OrganHealthSystem>().SetHealth(life.GetBrainOrgan(ipc)!.Value, FixedPoint2.Zero);
            SEntMan.System<OrganHealthSystem>().SetHealth(life.GetBrainOrgan(synth)!.Value, FixedPoint2.Zero);
        });
        await RunSeconds(1);

        await Server.WaitAssertion(() =>
        {
            Assert.That(mobState.IsDead(ipc), Is.True, "a destroyed core did not kill the IPC.");
            Assert.That(mobState.IsDead(synth), Is.True, "a destroyed core did not kill the synth.");

            var autodoc = SEntMan.System<AutodocSystem>();
            var slots = SEntMan.System<ItemSlotsSystem>();
            Entity<AutodocComponent> Pod(EntityUid occupant)
            {
                var uid = SEntMan.SpawnEntity("WolfmedLeftoversAutodoc", map.GridCoords);
                var pod = (uid, SEntMan.GetComponent<AutodocComponent>(uid));
                Assert.That(slots.TryInsert(uid, AutodocComponent.DiskSlotId,
                    SEntMan.SpawnEntity("WFAutodocProgramDiskNeuro", map.GridCoords), null), Is.True);
                Assert.That(autodoc.TryInsert(pod, occupant), Is.True);
                return pod;
            }

            ipcPod = Pod(ipc);
            synthPod = Pod(synth);
        });
        await Pair.RunTicksSync(10);

        await Server.WaitAssertion(() =>
        {
            var autodoc = SEntMan.System<AutodocSystem>();
            Assert.That(autodoc.TryQueue(ipcPod, "WFSurgeryRepairCore", TargetBodyPart.Torso), Is.True,
                "the neuro disk does not unlock the IPC core repair.");
            Assert.That(autodoc.TryQueue(synthPod, "WFSurgeryRepairSynthCore", TargetBodyPart.Head), Is.True,
                "the neuro disk does not unlock the synth core repair.");
            Assert.That(autodoc.TryStart(ipcPod, null), Is.True);
            Assert.That(autodoc.TryStart(synthPod, null), Is.True);
        });
        await Pair.RunTicksSync(600);

        await Server.WaitAssertion(() =>
        {
            var revival = SEntMan.System<WolfmedRevivalSystem>();
            var ipcCore = life.GetBrainOrgan(ipc)!.Value;
            var synthCore = life.GetBrainOrgan(synth)!.Value;
            Note($"PodRepairsACoreTest: IPC core {ipcCore.Comp.Health}/{ipcCore.Comp.MaxHealth} (pod {ipcPod.Comp!.State}), " +
                 $"synth core {synthCore.Comp.Health}/{synthCore.Comp.MaxHealth} (pod {synthPod.Comp!.State})");
            Assert.Multiple(() =>
            {
                Assert.That(ipcCore.Comp.Health, Is.EqualTo(ipcCore.Comp.MaxHealth),
                    $"the pod did not repair the IPC core (state {ipcPod.Comp.State}, step {ipcPod.Comp.CurrentStep}).");
                Assert.That(synthCore.Comp.Health, Is.EqualTo(synthCore.Comp.MaxHealth),
                    $"the pod did not repair the synth core (state {synthPod.Comp.State}, step {synthPod.Comp.CurrentStep}).");
                Assert.That(ipcPod.Comp.FailedProcedures, Is.Empty);
                Assert.That(synthPod.Comp.FailedProcedures, Is.Empty);
                Assert.That(SEntMan.HasComponent<WolfmedCoreHousingOpenComponent>(Part(ipc, BodyPartType.Torso)), Is.False,
                    "the pod left the IPC's housing open.");
                Assert.That(SEntMan.HasComponent<WolfmedCoreHousingOpenComponent>(Part(synth, BodyPartType.Head)), Is.False,
                    "the pod left the synth's housing open.");
            });

            Assert.That(revival.TryRestart(ipc), Is.True, "the repaired IPC does not restart.");
            Assert.That(revival.TryRestart(synth), Is.True, "the repaired synth does not restart.");
        });
    }

    /// <summary>
    /// M3's leftover: the pod's progress check compared severities, so a clamp that was closing a bleed read as no
    /// progress and WFSurgeryStopBleeding was abandoned after three clamps. The part's signature now carries each wound's
    /// bleeding severity, and a bleed that needs five clamps is closed in one procedure.
    /// </summary>
    [Test]
    public async Task PodClampProgressTest()
    {
        await Pin();
        var map = await Pair.CreateTestMap();
        EntityUid body = default, arm = default, wound = default;
        Entity<AutodocComponent> pod = default;

        await Server.WaitAssertion(() =>
        {
            var autodoc = SEntMan.System<AutodocSystem>();
            var wounds = SEntMan.System<WoundSystem>();
            var bleeding = SEntMan.System<WoundBleedingSystem>();
            body = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            arm = Part(body, BodyPartType.Arm, BodyPartSymmetry.Left);

            wound = wounds.CreateOrMergeWound(arm, "SlashWound", FixedPoint2.New(30))!.Value;
            var bleed = SEntMan.EnsureComponent<WoundBleedingComponent>(wound);
            bleed.BleedingSeverity = FixedPoint2.New(50);
            Assert.That(bleeding.SetTreatment(wound, BleedingTreatment.None), Is.True);
            Assert.That(bleed.CurrentRate, Is.GreaterThan(0f), "the fixture wound does not bleed.");

            // One clamp's worth off the bleed, nothing off the wound: the signature moves.
            var severity = SEntMan.GetComponent<WoundComponent>(wound).Severity;
            var before = autodoc.PartSignature(arm);
            Assert.That(bleeding.ReduceBleeding(wound, FixedPoint2.New(10)), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.GetComponent<WoundComponent>(wound).Severity, Is.EqualTo(severity));
                Assert.That(autodoc.PartSignature(arm), Is.Not.EqualTo(before), "a clamp that closed a bleed reads as no progress.");
            });

            // Back to 50 for the pod: five clamps of 10.
            bleed.BleedingSeverity = FixedPoint2.New(50);
            bleeding.SetTreatment(wound, BleedingTreatment.None);

            var uid = SEntMan.SpawnEntity("WolfmedLeftoversAutodoc", map.GridCoords);
            pod = (uid, SEntMan.GetComponent<AutodocComponent>(uid));
            Assert.That(autodoc.TryInsert(pod, body), Is.True);
        });
        await Pair.RunTicksSync(10);

        await Server.WaitAssertion(() =>
        {
            var autodoc = SEntMan.System<AutodocSystem>();
            Assert.That(autodoc.TryQueue(pod, "WFSurgeryStopBleeding", TargetBodyPart.LeftArm), Is.True);
            Assert.That(autodoc.TryStart(pod, null), Is.True);
        });
        await Pair.RunTicksSync(300);

        await Server.WaitAssertion(() =>
        {
            var rate = SEntMan.TryGetComponent<WoundBleedingComponent>(wound, out var bleed) ? bleed.CurrentRate : 0f;
            Note($"PodClampProgressTest: pod {pod.Comp!.State}, failed [{string.Join(", ", pod.Comp.FailedProcedures)}], bleed rate {rate}");
            Assert.Multiple(() =>
            {
                Assert.That(pod.Comp.FailedProcedures.Contains(("WFSurgeryStopBleeding", TargetBodyPart.LeftArm)), Is.False,
                    "the pod abandoned a clamp that was working.");
                Assert.That(rate, Is.Zero, "the bleed was not closed.");
            });
        });
    }

    /// <summary>
    /// A Synth is mechanical (OD16) and its modifier set now zeroes Poison, so a poison dose that goes through
    /// resistances never lands: no load, no toxin cause, no coma, no analyzer line. A human with the same dose is in a
    /// toxic coma. The owner's call on 2026-09-24: synthetic with organic parts, and no organic weakness left dangling.
    /// </summary>
    [Test]
    public async Task SynthTakesNoPoisonTest()
    {
        await Pin();
        var map = await Pair.CreateTestMap();
        var s = new WolfmedScenario(SEntMan);
        EntityUid synth = default, human = default;

        await Server.WaitPost(() =>
        {
            s.SetAir(map.MapUid, true);
            s.KeepGrid(map.Grid);
            synth = SEntMan.SpawnEntity("MobSynth", map.GridCoords);
            human = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
        });
        await RunSeconds(2);

        await Server.WaitAssertion(() =>
        {
            var toxin = SEntMan.System<WolfmedToxinSystem>();
            var damage = SEntMan.System<DamageableSystem>();
            damage.TryChangeDamage(synth, WolfmedScenario.Spec("Poison", 130));
            damage.TryChangeDamage(human, WolfmedScenario.Spec("Poison", 130));
            s.Consciousness.Refresh(synth);
            s.Consciousness.Refresh(human);

            Note($"SynthTakesNoPoisonTest: synth load {toxin.GetLoad(synth)}, state {s.State(synth)}, " +
                 $"cause {s.Vitals(synth).Cause}; analyzer {s.Analyzer(synth)}");
            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.System<WolfmedShutdownSystem>().IsMechanical(synth), Is.True, "a synth is not a machine.");
                Assert.That(toxin.GetLoad(synth), Is.Zero, "the synth took Poison through its modifier set.");
                Assert.That(s.Vitals(synth).Cause, Is.Not.EqualTo(WolfmedCause.Toxin), "the synth is poisoned.");
                Assert.That(s.State(synth), Is.EqualTo(WolfmedConsciousness.Up), "the poison did something to the synth.");
                Assert.That(s.Analyzer(synth), Does.Not.Contain("Toxins"), "the analyzer shows a toxin load on a synth.");
                Assert.That(s.Analyzer(human), Does.Contain("Toxins "), "the control's toxin item is missing (playtest 3).");
                Assert.That(s.State(human), Is.EqualTo(WolfmedConsciousness.Unconscious), "the human control is not in a coma.");
                Assert.That(s.Vitals(human).Cause, Is.EqualTo(WolfmedCause.Toxin));
            });
        });
    }
}
