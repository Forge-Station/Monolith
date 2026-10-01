#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._HL.Silicons.Synths.Battery;
using Content.Server._Shitmed.Medical.Surgery;
using Content.Server._WF.Wolfmed.Consciousness;
using Content.Server._WF.Wolfmed.Hud;
using Content.Server._WF.Wolfmed.Life;
using Content.Server.Body.Components;
using Content.Server.Medical;
using Content.Server.Power.EntitySystems;
using Content.Shared._HL.Silicons.Synths.Battery;
using Content.Shared._Onyx.Body.Systems;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Body.Organ;
using Content.Shared._Shitmed.Medical.Surgery;
using Content.Shared._WF.Wolfmed.Body;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.Consciousness;
using Content.Shared._WF.Wolfmed.Hud;
using Content.Shared._WF.Wolfmed.Life;
using Content.Shared._WF.Wolfmed.Surgery;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Mind;
using Content.Shared.Mobs.Systems;
using Content.Shared.Power.Components;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;

namespace Content.IntegrationTests.Tests._WF.Wolfmed.Scenarios;

/// <summary>
/// M4 (plan §9, OD16): every round-start species dies, is disabled and comes back the way its row in the matrix says.
/// Hearts that used to carry no data arrest, brains that used to run no clock kill when taken, the heartless species
/// collapse instead of arresting, a slime keeps its core in its torso, and the species that were not wound hosts are.
/// Synth is a machine (OD16): its battery shuts it down and never kills it, its core is its death.
/// </summary>
[TestFixture]
[TestOf(typeof(WolfmedLifeSystem))]
public sealed class WolfmedSpeciesTest : GameTest
{
    private async Task Pin()
    {
        await OverrideCVar(Side.Server, WolfmedCVars.Consciousness, true);
        await OverrideCVar(Side.Server, WolfmedCVars.ArrestBlood, 0.30f);
        await OverrideCVar(Side.Server, WolfmedCVars.ConsciousnessBloodDown, 0.5f);
        await OverrideCVar(Side.Server, WolfmedCVars.ConsciousnessBloodOut, 0.35f);
    }

    private EntityUid Part(EntityUid body, BodyPartType type) =>
        SEntMan.System<SharedBodySystem>().GetBodyChildren(body).First(part => part.Component.PartType == type).Id;

    private EntityUid Organ<T>(EntityUid body) where T : IComponent =>
        SEntMan.System<SharedBodySystem>().GetBodyOrgans(body).First(organ => SEntMan.HasComponent<T>(organ.Id)).Id;

    private WolfmedConsciousnessComponent Consc(EntityUid body) =>
        SEntMan.GetComponent<WolfmedConsciousnessComponent>(body);

    /// <summary>
    /// <c>SpeciesArrestTest</c> (plan §12 M4): a moth's heart destroyed arrests it; a skrell at 29% blood arrests; a
    /// diona's brain removed kills it; a slime survives decapitation; a shadekin at 29% blood arrests. A heartless
    /// species' arrest is circulatory collapse, in its words (OD16).
    /// </summary>
    [Test]
    public async Task SpeciesArrestTest()
    {
        await Pin();
        var map = await Pair.CreateTestMap();
        var s = new WolfmedScenario(SEntMan);
        var alerts = SEntMan.System<WolfmedConditionAlertSystem>();
        var mobState = SEntMan.System<MobStateSystem>();
        var graph = SEntMan.System<SharedBodySystem>();
        EntityUid moth = default, skrell = default, diona = default, dionaBled = default, slime = default,
            slimeBled = default, shadekin = default, protoKin = default;

        await Server.WaitPost(() =>
        {
            s.SetAir(map.MapUid, true);
            moth = SEntMan.SpawnEntity("MobMoth", map.GridCoords);
            skrell = SEntMan.SpawnEntity("RMCMobSkrell", map.GridCoords);
            diona = SEntMan.SpawnEntity("MobDiona", map.GridCoords);
            dionaBled = SEntMan.SpawnEntity("MobDiona", map.GridCoords);
            slime = SEntMan.SpawnEntity("MobSlimePerson", map.GridCoords);
            slimeBled = SEntMan.SpawnEntity("MobSlimePerson", map.GridCoords);
            shadekin = SEntMan.SpawnEntity("MobShadekin", map.GridCoords);
            protoKin = SEntMan.SpawnEntity("MobProtoKin", map.GridCoords);
        });
        await RunSeconds(2);

        await Server.WaitAssertion(() =>
        {
            // A moth's heart (group B) now carries Wolfmed data, so destroying it stops the heart.
            SEntMan.System<OrganHealthSystem>().SetHealth(
                (Organ<HeartComponent>(moth), SEntMan.GetComponent<WolfmedOrganComponent>(Organ<HeartComponent>(moth))),
                FixedPoint2.Zero);
            s.Advance(moth, 1);
            Assert.That(s.Life.InArrest(moth), Is.True, "a moth's destroyed heart did not stop.");
            Assert.That(SEntMan.GetComponent<WolfmedCardiacArrestComponent>(moth).Cause, Is.EqualTo("heart"));
            Assert.That(Consc(moth).Heartless, Is.False, "a moth has a heart; its arrest is not a collapse.");
            Assert.That(alerts.GetTitle(moth), Does.StartWith("Cardiac arrest"));

            // A skrell (group C) runs a brain clock now: 29% blood arrests it.
            s.SetBlood(skrell, 0.29f);
            s.Advance(skrell, 1);
            Assert.That(s.Life.InArrest(skrell), Is.True, "a skrell at 29% blood did not arrest.");

            // A diona's nymph brain is its brain: taking it out kills.
            var brain = Organ<BrainComponent>(diona);
            Assert.That(SEntMan.HasComponent<WolfmedBrainComponent>(brain), Is.True);
            Assert.That(graph.RemoveOrgan(brain), Is.True);
            Assert.That(mobState.IsDead(diona), Is.True, "a diona whose brain was taken out is still alive.");

            // A diona has no heart: at 29% blood its circulation collapses (plan §9.3, OD16).
            s.SetBlood(dionaBled, 0.29f);
            s.Advance(dionaBled, 1);
            Assert.Multiple(() =>
            {
                Assert.That(s.Life.InArrest(dionaBled), Is.True, "a diona at 29% blood did not collapse.");
                Assert.That(Consc(dionaBled).Heartless, Is.True);
                Assert.That(Consc(dionaBled).Cause, Is.EqualTo(WolfmedCause.Arrest));
                Assert.That(alerts.GetTitle(dionaBled), Is.EqualTo("Circulatory collapse: blood loss"));
                Assert.That(alerts.GetShownHealthAlert(dionaBled)?.Id, Is.EqualTo("WFWolfmedOutCollapse"));
                Assert.That(alerts.GetConditionText(dionaBled), Does.Not.Contain("heart"),
                    "a heartless patient was told about its heart.");
                Assert.That(s.AnalyzerLines(dionaBled)[0], Is.EqualTo("CIRCULATORY COLLAPSE: blood"));
                // Playtest 3: the collapse is named on the state line; the vitals line says only what it sees.
                Assert.That(s.AnalyzerLines(dionaBled)[1], Does.StartWith("Not breathing").And.Not.Contain("heart")
                    .And.Not.Contain("cardiac"));
                Assert.That(SEntMan.HasComponent<WolfmedDyingActionsComponent>(dionaBled), Is.True,
                    "a collapse is Dying and offers Succumb.");
            });

            // A slime's core is in its torso: the head comes off and the slime lives.
            var head = Part(slime, BodyPartType.Head);
            Assert.That(SEntMan.GetComponent<BodyPartComponent>(head).IsVital, Is.False);
            Assert.That(SEntMan.System<Content.Shared._WF.Wolfmed.Compat.WolfmedBodySystem>().TryDetachPart(head), Is.True);
            Assert.That(mobState.IsDead(slime), Is.False, "a slime died of losing its head.");
            Assert.That(graph.GetBodyChildrenOfType(slime, BodyPartType.Head).Any(), Is.False, "the head is still on.");

            s.SetBlood(slimeBled, 0.29f);
            s.Advance(slimeBled, 1);
            Assert.That(s.Life.InArrest(slimeBled), Is.True, "a slime at 29% blood did not collapse.");
            Assert.That(Consc(slimeBled).Heartless, Is.True);

            // Group D: a shadekin and a proto-kin are wound hosts, and 29% blood arrests them.
            foreach (var kin in new[] { shadekin, protoKin })
            {
                Assert.That(SEntMan.HasComponent<WoundHostComponent>(kin), Is.True, "group D is still not a wound host.");
                Assert.That(s.Life.GetBrain(kin), Is.Not.Null, "group D runs no brain clock.");
                s.SetBlood(kin, 0.29f);
                s.Advance(kin, 1);
                Assert.That(s.Life.InArrest(kin), Is.True, $"{SEntMan.GetComponent<MetaDataComponent>(kin).EntityPrototype?.ID} at 29% blood did not arrest.");
                // No lungs and no respirator by design: the chest reads as breathing, not as failed lungs.
                Assert.That(SEntMan.System<WolfmedBreathingSystem>().Assess(shadekin).Breathing,
                    Is.Not.EqualTo(WolfmedBreathing.Laboured));
            }
        });

        // The slime is still alive a few seconds on, with its head on the floor.
        await RunSeconds(3);
        await Server.WaitAssertion(() =>
            Assert.That(mobState.IsDead(slime), Is.False, "a decapitated slime died a few seconds later."));
    }

    /// <summary>
    /// <c>SynthBranchTest</c> (plan §12 M4, OD16: the mechanical branch). An empty SynthBattery shuts the synth down with
    /// reason Power, on its own readout too, and nothing kills it over 30 minutes; the battery back brings it up.
    /// Taking the core out is core failure; a destroyed core is repaired on the head and the restart brings the same
    /// mind back.
    /// </summary>
    [Test]
    public async Task SynthBranchTest()
    {
        await Pin();
        var map = await Pair.CreateTestMap();
        var s = new WolfmedScenario(SEntMan);
        var mobState = SEntMan.System<MobStateSystem>();
        var graph = SEntMan.System<SharedBodySystem>();
        var shutdown = SEntMan.System<WolfmedShutdownSystem>();
        EntityUid synth = default, cored = default, broken = default, mind = default;

        await Server.WaitPost(() =>
        {
            s.SetAir(map.MapUid, true);
            s.KeepGrid(map.Grid);
            synth = SEntMan.SpawnEntity("MobSynth", map.GridCoords);
            cored = SEntMan.SpawnEntity("MobSynth", map.GridCoords);
            broken = SEntMan.SpawnEntity("MobSynth", map.GridCoords);
            var minds = SEntMan.System<SharedMindSystem>();
            mind = minds.CreateMind(null).Owner;
            minds.TransferTo(mind, broken);
        });
        await RunSeconds(3);

        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.HasComponent<WoundHostComponent>(synth), Is.True);
                Assert.That(shutdown.IsMechanical(synth), Is.True, "a synth is not a machine.");
                Assert.That(s.Life.GetBrain(synth), Is.Null, "a synth runs an oxygen clock.");
                Assert.That(shutdown.HasPower(synth), Is.True, "a fresh synth has no power.");
                Assert.That(SEntMan.GetComponent<WoundableComponent>(Part(synth, BodyPartType.Torso)).Profile.Id,
                    Is.EqualTo("IpcBodyPartProfile"), "a synth chassis is not mechanical.");
                Assert.That(SEntMan.HasComponent<WolfmedSyntheticHudComponent>(synth), Is.True, "no synthetic readout.");
                Assert.That(SEntMan.GetComponent<WolfmedSyntheticHudComponent>(synth).Power, Is.GreaterThan(0f),
                    "the readout does not show the synth's battery.");
            });

            // Empty the battery.
            Assert.That(SEntMan.System<SynthBatterySystem>().TryGetBattery(synth, out var cell), Is.True);
            SEntMan.System<BatterySystem>().SetCharge(cell!.Value.Owner, 0f, cell.Value.Comp);
        });
        await RunSeconds(3);

        await Server.WaitAssertion(() =>
        {
            var comp = Consc(synth);
            Assert.Multiple(() =>
            {
                Assert.That(comp.State, Is.EqualTo(WolfmedConsciousness.Unconscious), "an empty battery left the synth up.");
                Assert.That(comp.Cause, Is.EqualTo(WolfmedCause.Shutdown));
                Assert.That(comp.CauseSource, Is.EqualTo(WolfmedCauseSource.Power));
                Assert.That(s.AnalyzerLines(synth)[0], Is.EqualTo("SHUTDOWN: no power"));
                Assert.That(SEntMan.GetComponent<WolfmedSyntheticHudComponent>(synth).CauseLine,
                    Is.EqualTo("wolfmed-synthetic-cause-shutdown"));
            });

            // Thirty minutes on the life and core-heat ticks: nothing runs out on a shut-down synth.
            s.Advance(synth, 1800);
            for (var second = 0; second < 1800; second += 30)
                SEntMan.System<WolfmedOverheatSystem>().Tick(synth, 30f);
            Assert.That(mobState.IsDead(synth), Is.False, "a shutdown killed the synth.");
        });

        await RunSeconds(5);
        await Server.WaitAssertion(() =>
        {
            Assert.That(mobState.IsDead(synth), Is.False, "a shutdown killed the synth.");
            Assert.That(SEntMan.System<SynthBatterySystem>().TryGetBattery(synth, out var cell), Is.True);
            SEntMan.System<BatterySystem>().SetCharge(cell!.Value.Owner, cell.Value.Comp.MaxCharge, cell.Value.Comp);
        });
        await RunSeconds(3);
        await Server.WaitAssertion(() =>
            Assert.That(Consc(synth).State, Is.EqualTo(WolfmedConsciousness.Up), "a charged battery did not bring the synth up."));

        await Server.WaitAssertion(() =>
        {
            // The core out: core failure.
            var core = Organ<BrainComponent>(cored);
            Assert.That(SEntMan.HasComponent<WolfmedOrganComponent>(core), Is.True, "the synth core carries no organ health.");
            Assert.That(graph.RemoveOrgan(core), Is.True);
            Assert.That(mobState.IsDead(cored), Is.True, "taking the core out did not kill the synth.");

            // A destroyed core: dead, repaired on the head, restarted.
            SEntMan.System<OrganHealthSystem>().SetHealth(s.Life.GetBrainOrgan(broken)!.Value, FixedPoint2.Zero);
        });
        await RunSeconds(1);

        await Server.WaitAssertion(() =>
        {
            Assert.That(mobState.IsDead(broken), Is.True, "a destroyed synth core did not kill it.");
            var surgery = SEntMan.System<SurgerySystem>();
            var head = Part(broken, BodyPartType.Head);
            Assert.Multiple(() =>
            {
                Assert.That(surgery.WolfmedSurgeryValid(broken, head, "WFSurgeryRepairSynthCore"), Is.True,
                    "no core repair on a synth's head.");
                Assert.That(surgery.WolfmedSurgeryValid(broken, head, "WFSurgeryRepairBrain"), Is.False,
                    "organic brain repair is offered on a synth's core.");
                Assert.That(SEntMan.System<WolfmedRevivalSystem>().GetRestartRefusal(broken),
                    Is.EqualTo(WolfmedRevivalSystem.RestartCoreDestroyed));
            });

            var tools = new List<EntityUid>
            {
                SEntMan.SpawnEntity("Wrench", map.GridCoords),
                SEntMan.SpawnEntity("Multitool", map.GridCoords),
                SEntMan.SpawnEntity("Welder", map.GridCoords),
            };
            foreach (var step in new[] { "WFSurgeryStepUnseatCore", "WFSurgeryStepRepairSynthCore", "WFSurgeryStepReseatCore" })
            {
                var singleton = surgery.GetSingleton(step);
                Assert.That(singleton, Is.Not.Null, $"{step} has no singleton.");
                var ev = new SurgeryStepEvent(broken, broken, head, tools, singleton!.Value);
                SEntMan.EventBus.RaiseLocalEvent(singleton.Value, ref ev);
            }

            var repaired = s.Life.GetBrainOrgan(broken)!.Value;
            Assert.That(repaired.Comp.Health, Is.EqualTo(repaired.Comp.MaxHealth), "the synth core was not repaired.");
            Assert.That(SEntMan.HasComponent<WolfmedBrainTraumaComponent>(broken), Is.False, "a repaired synth core carries trauma.");
            Assert.That(SEntMan.HasComponent<Content.Shared._EinsteinEngines.Silicon.DeadStartupButton.DeadStartupButtonComponent>(broken),
                Is.True, "a synth has no restart button.");
            Assert.That(SEntMan.System<WolfmedRevivalSystem>().TryRestart(broken), Is.True);
            Assert.That(mobState.IsDead(broken), Is.False, "the restart did not bring the synth back.");
            Assert.That(SEntMan.System<SharedMindSystem>().TryGetMind(broken, out var after, out _), Is.True);
            Assert.That(after, Is.EqualTo(mind), "the synth came back as somebody else.");
        });
    }
}
