#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server.Body.Components;
using Content.Server._WF.Wolfmed.Life;
using Content.Server._WF.Wolfmed.Wounds;
using Content.Shared._Onyx.Body.Systems;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Body.Organ;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Wolfmed.Body;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.Consciousness;
using Content.Shared._WF.Wolfmed.Life;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Wolfmed.Scenarios;

/// <summary>
/// M5 (plan §3.8, §3.9, P21, OD13): toxins and radiation get lethal routes of their own, infection no longer feeds
/// the toxin load, and acid residue deepens only its own burn. Times are asserted as order plus a ±20% band around
/// the figures derived from the pinned CVars.
/// </summary>
[TestFixture]
[TestOf(typeof(WolfmedToxinSystem))]
public sealed class WolfmedRemainingCausesTest : GameTest
{
    private const float Band = 0.2f;

    /// <summary>
    /// What the test measured, written out on the test's own thread when it ends: a line written from a server
    /// callback can land in another test's output.
    /// </summary>
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
        // The sepsis fixtures infect a 20-point cut, a minor wound, which playtest 5 spares the air.
        await OverrideCVar(Side.Server, WolfmedCVars.InfectionSparesMinor, false);
        await OverrideCVar(Side.Server, WolfmedCVars.ConsciousnessHysteresis, 0.1f);
        await OverrideCVar(Side.Server, WolfmedCVars.ConsciousnessToxinDown, 60f);
        await OverrideCVar(Side.Server, WolfmedCVars.ConsciousnessToxinOut, 120f);
        await OverrideCVar(Side.Server, WolfmedCVars.BrainToxinSeconds, 600f);
        await OverrideCVar(Side.Server, WolfmedCVars.ToxinClearance, 0.1f);
        await OverrideCVar(Side.Server, WolfmedCVars.RadiationMarrowStop, 40f);
        await OverrideCVar(Side.Server, WolfmedCVars.RadiationMarrowBleed, 100f);
        await OverrideCVar(Side.Server, WolfmedCVars.RadiationMarrowRate, 0.1f);
        await OverrideCVar(Side.Server, WolfmedCVars.ConsciousnessRadiationDown, 80f);
        await OverrideCVar(Side.Server, WolfmedCVars.ArrestOxygenation, 0.15f);
        await OverrideCVar(Side.Server, WolfmedCVars.ArrestBlood, 0.30f);
        await OverrideCVar(Side.Server, WolfmedCVars.BrainArrestSeconds, 120f);
        await OverrideCVar(Side.Server, WolfmedCVars.BrainRefillFactor, 0.5f);
        await OverrideCVar(Side.Server, WolfmedCVars.BrainPressureStart, 0.75f);
        await OverrideCVar(Side.Server, WolfmedCVars.BrainPressureOut, 0.45f);
        await OverrideCVar(Side.Server, WolfmedCVars.BrainColdFactor, 1f);
    }

    private WolfmedToxinSystem Toxin => SEntMan.System<WolfmedToxinSystem>();
    private WolfmedRadiationSystem Radiation => SEntMan.System<WolfmedRadiationSystem>();

    private void Deal(EntityUid body, string type, float amount) =>
        SEntMan.System<DamageableSystem>().TryChangeDamage(body, WolfmedScenario.Spec(type, amount), ignoreResistances: true);

    private EntityUid Liver(EntityUid body) =>
        SEntMan.System<SharedBodySystem>().GetBodyOrgans(body).First(organ => SEntMan.HasComponent<LiverComponent>(organ.Id)).Id;

    private void Inject(EntityUid body, string reagent, float units)
    {
        var solutions = SEntMan.System<SharedSolutionContainerSystem>();
        var bloodstream = SEntMan.GetComponent<BloodstreamComponent>(body);
        Assert.That(solutions.TryGetSolution(body, bloodstream.ChemicalSolutionName, out var chemicals, out _), Is.True);
        Assert.That(solutions.TryAddReagent(chemicals!.Value, reagent, FixedPoint2.New(units)), Is.True);
    }

    private static void InBand(float actual, float derived, string what) =>
        Assert.That(actual, Is.InRange(derived * (1f - Band), derived * (1f + Band)),
            $"{what}: {actual} against {derived} derived.");

    /// <summary>
    /// <c>ToxinScenarioTest</c> (plan §3.8): Poison 60 Downs with cause Toxin; 130 is a toxic coma that breathes and
    /// drains the brain at 1/600 per s, and with nothing clearing it the heart stops through the oxygen trigger, named
    /// "toxin", at the derived 510 s. The liver clears 0.1 a second, an impaired one half that; a coma it can clear
    /// wakes under 108 (the 0.9 leave line). Dylovene at coma: the load falls, the patient wakes under 108 and stands
    /// under 54.
    /// </summary>
    [Test]
    public async Task ToxinScenarioTest()
    {
        // Playtest 5: a patient at the coma band retches and can vomit (the condition emotes), and a vomit purges the
        // bloodstream, dylovene included, on a random roll. This test measures the clearance, so the roll is off.
        await OverrideCVar(Side.Server, WolfmedCVars.ConditionEmoteChance, 0f);

        await Pin();
        var map = await Pair.CreateTestMap();
        var s = new WolfmedScenario(SEntMan);
        EntityUid downed = default, liverless = default, cleared = default, impaired = default, coma = default, treated = default;

        await Server.WaitPost(() =>
        {
            s.SetAir(map.MapUid, true);
            s.KeepGrid(map.Grid);
            downed = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            liverless = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            cleared = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            impaired = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            coma = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            treated = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
        });
        await RunSeconds(3);

        await Server.WaitAssertion(() =>
        {
            // Poison 60: Downed, poisoned.
            Deal(downed, "Poison", 60);
            s.Consciousness.Refresh(downed);
            Assert.Multiple(() =>
            {
                Assert.That(Toxin.GetLoad(downed), Is.EqualTo(60f).Within(0.01f), "the toxin load is not the systemic Poison.");
                Assert.That(s.State(downed), Is.EqualTo(WolfmedConsciousness.Downed));
                Assert.That(s.Vitals(downed).Cause, Is.EqualTo(WolfmedCause.Toxin));
                Assert.That(s.Analyzer(downed), Does.Contain("DOWNED: poisoning").And.Contain("Toxins 60, high"));
            });

            // Poison 130 with nothing to clear it: a toxic coma that breathes and drains the brain.
            Assert.That(SEntMan.System<SharedBodySystem>().RemoveOrgan(Liver(liverless)), Is.True);
            Deal(liverless, "Poison", 130);
            s.Life.Tick(liverless, 0.0001f);
            Assert.Multiple(() =>
            {
                Assert.That(s.State(liverless), Is.EqualTo(WolfmedConsciousness.Unconscious));
                Assert.That(s.Vitals(liverless).Cause, Is.EqualTo(WolfmedCause.Toxin));
                Assert.That(s.Vitals(liverless).Breathing, Is.EqualTo(WolfmedBreathing.Normal), "a toxic coma stopped the chest.");
                Assert.That(s.Breathing.BreathingSuppressed(liverless), Is.False);
                Assert.That(s.Analyzer(liverless), Does.Contain("UNCONSCIOUS: poisoning").And.Contain("No liver"));
            });

            var before = s.Life.GetOxygenation(liverless);
            s.Advance(liverless, 60);
            InBand(before - s.Life.GetOxygenation(liverless), 60f / 600f, "brain drain over 60 s of toxic coma");
            Assert.That(s.Life.GetActiveRoutes(liverless) & WolfmedRoutes.Toxin, Is.Not.EqualTo(WolfmedRoutes.None));

            var arrestAt = 60 + s.Advance(liverless, 700, _ => s.Life.InArrest(liverless));
            Note($"ToxinScenarioTest: Poison 130 with no liver arrests at {arrestAt} s (derived 510 s).");
            Assert.Multiple(() =>
            {
                Assert.That(s.Life.InArrest(liverless), Is.True, "a toxic coma nothing cleared never stopped the heart.");
                Assert.That(SEntMan.GetComponent<WolfmedCardiacArrestComponent>(liverless).Cause, Is.EqualTo("toxin"));
                InBand(arrestAt, (1f - 0.15f) * 600f, "toxic coma arrest");
                Assert.That(s.Analyzer(liverless), Does.Contain("CARDIAC ARREST: poisoning"));
            });

            // Clearance: a working liver 0.1 a second, an impaired one half that.
            Deal(cleared, "Poison", 30);
            s.Advance(cleared, 60);
            var impairedLiver = Liver(impaired);
            var organ = SEntMan.GetComponent<WolfmedOrganComponent>(impairedLiver);
            SEntMan.System<OrganHealthSystem>().SetHealth((impairedLiver, organ), organ.MaxHealth * 0.4f);
            Deal(impaired, "Poison", 30);
            s.Advance(impaired, 60);
            Note($"ToxinScenarioTest: 60 s cleared {30f - Toxin.GetLoad(cleared):0.00} (working) and " +
                                      $"{30f - Toxin.GetLoad(impaired):0.00} (impaired).");
            InBand(30f - Toxin.GetLoad(cleared), 6f, "a working liver's clearance over 60 s");
            InBand(30f - Toxin.GetLoad(impaired), 3f, "an impaired liver's clearance over 60 s");
            Assert.That(s.Analyzer(impaired), Does.Contain("Liver impaired")); // playtest 3: the organ item

            // A coma the liver can clear: wakes under the 0.9 leave line, 108, then the Downed line clears under 54.
            Deal(coma, "Poison", 130);
            Assert.That(s.State(coma), Is.EqualTo(WolfmedConsciousness.Unconscious));
            var wake = s.Advance(coma, 400, _ => s.State(coma) != WolfmedConsciousness.Unconscious);
            var wakeLoad = Toxin.GetLoad(coma);
            var stand = wake + s.Advance(coma, 900, _ => Toxin.GetLoad(coma) < 54f);
            Note($"ToxinScenarioTest: Poison 130, liver only: wakes at {wake} s (load {wakeLoad:0.0}, " +
                                      $"derived 220 s), under the Downed line at {stand} s (derived 760 s).");
            Assert.Multiple(() =>
            {
                Assert.That(wakeLoad, Is.LessThan(108f).And.GreaterThan(106f), "the coma did not end at its 0.9 leave line.");
                InBand(wake, (130f - 108f) / 0.1f, "waking from a toxic coma on the liver alone");
                InBand(stand, (130f - 54f) / 0.1f, "clearing the Downed line on the liver alone");
                Assert.That(s.State(coma), Is.EqualTo(WolfmedConsciousness.Downed));
            });

            // Dylovene at coma, in real time below.
            Deal(treated, "Poison", 130);
            Assert.That(s.State(treated), Is.EqualTo(WolfmedConsciousness.Unconscious));
            Inject(treated, "Dylovene", 15);
        });

        int? woke = null, stood = null;
        float wokeLoad = 0f, stoodLoad = 0f;
        for (var second = 1; second <= 120 && stood == null; second++)
        {
            await RunSeconds(1);
            var now = second;
            await Server.WaitPost(() =>
            {
                if (woke == null && s.State(treated) != WolfmedConsciousness.Unconscious)
                {
                    woke = now;
                    wokeLoad = Toxin.GetLoad(treated);
                }

                if (stood == null && s.State(treated) == WolfmedConsciousness.Up)
                {
                    stood = now;
                    stoodLoad = Toxin.GetLoad(treated);
                }
            });
        }

        var endLoad = 0f;
        var endState = WolfmedConsciousness.Up;
        await Server.WaitPost(() =>
        {
            endLoad = Toxin.GetLoad(treated);
            endState = s.State(treated);
        });
        var liver = "";
        var dyloveneLeft = 0f;
        await Server.WaitPost(() =>
        {
            liver = Toxin.GetLiver(treated).ToString();
            var solutions = SEntMan.System<SharedSolutionContainerSystem>();
            var bloodstream = SEntMan.GetComponent<BloodstreamComponent>(treated);
            if (solutions.TryGetSolution(treated, bloodstream.ChemicalSolutionName, out _, out var chemicals))
                dyloveneLeft = chemicals.GetTotalPrototypeQuantity("Dylovene").Float();
        });
        Note($"ToxinScenarioTest: 15 u dylovene at Poison 130: wakes at {woke} s (load {wokeLoad:0.0}), " +
                                  $"stands at {stood} s (load {stoodLoad:0.0}); at the end load {endLoad:0.0}, {endState}, " +
                                  $"vitals {s.Vitals(treated).Cause}, liver {liver}, dylovene left {dyloveneLeft:0.0} u.");
        Assert.Multiple(() =>
        {
            Assert.That(woke, Is.Not.Null, "dylovene never woke a toxic coma.");
            Assert.That(wokeLoad, Is.LessThan(108f));
            Assert.That(stood, Is.Not.Null, "dylovene never stood the patient up.");
            Assert.That(stoodLoad, Is.LessThan(54f));
            Assert.That(stood, Is.GreaterThan(woke!));
        });
    }

    /// <summary>
    /// <c>RadiationScenarioTest</c> (plan §3.9): at 40 the blood stays flat at 90% for a minute (no regeneration);
    /// at 100 it falls 0.1 u/s; at 80 the patient is Downed, cause Radiation. Hyronalin takes the dose under 40 and
    /// the blood comes back. A chassis at 100 loses no oil and is not Downed by it.
    /// </summary>
    [Test]
    public async Task RadiationScenarioTest()
    {
        await Pin();
        var map = await Pair.CreateTestMap();
        var s = new WolfmedScenario(SEntMan);
        EntityUid control = default, stopped = default, failing = default, sick = default, ipc = default;

        await Server.WaitPost(() =>
        {
            s.SetAir(map.MapUid, true);
            s.KeepGrid(map.Grid);
            control = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            stopped = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            failing = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            sick = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            ipc = SEntMan.SpawnEntity("MobIPC", map.GridCoords);
        });
        await RunSeconds(3);

        float Volume(EntityUid body) => s.Blood(body) * s.Pool(body);
        float controlStart = 0f, stoppedStart = 0f, failingStart = 0f, oilStart = 0f;

        await Server.WaitAssertion(() =>
        {
            foreach (var body in new[] { control, stopped, failing })
                s.SetBlood(body, 0.9f);

            Deal(stopped, "Radiation", 40);
            Deal(failing, "Radiation", 100);
            Deal(sick, "Radiation", 80);
            Deal(ipc, "Radiation", 100);
            foreach (var body in new[] { stopped, failing, sick, ipc })
                s.Consciousness.Refresh(body);

            controlStart = Volume(control);
            stoppedStart = Volume(stopped);
            failingStart = Volume(failing);
            oilStart = Volume(ipc);

            Assert.Multiple(() =>
            {
                Assert.That(Radiation.GetRadiation(failing), Is.EqualTo(100f).Within(0.01f));
                Assert.That(s.State(stopped), Is.EqualTo(WolfmedConsciousness.Up), "radiation 40 put somebody down.");
                Assert.That(s.State(sick), Is.EqualTo(WolfmedConsciousness.Downed));
                Assert.That(s.Vitals(sick).Cause, Is.EqualTo(WolfmedCause.Radiation));
                Assert.That(s.Analyzer(sick), Does.Contain("DOWNED: radiation sickness"));
                Assert.That(s.Vitals(ipc).Cause, Is.Not.EqualTo(WolfmedCause.Radiation), "a chassis has radiation sickness.");
                Assert.That(Radiation.GetMarrowLossRate(ipc), Is.Zero, "a chassis has a marrow.");
            });
            Note($"RadiationScenarioTest: the chassis carries radiation {Radiation.GetRadiation(ipc):0}.");
        });

        await RunSeconds(60);

        await Server.WaitAssertion(() =>
        {
            var controlGain = Volume(control) - controlStart;
            var stoppedChange = Volume(stopped) - stoppedStart;
            var failingLoss = failingStart - Volume(failing);
            Note($"RadiationScenarioTest: 60 s at 90% blood: no radiation {controlGain:+0.0;-0.0} u, " +
                                      $"radiation 40 {stoppedChange:+0.0;-0.0} u, radiation 100 {-failingLoss:+0.0;-0.0} u " +
                                      $"(derived -6.0 u); chassis oil {Volume(ipc) - oilStart:+0.0;-0.0} u.");
            Assert.Multiple(() =>
            {
                Assert.That(controlGain, Is.GreaterThan(5f), "the control body did not regenerate, so the test shows nothing.");
                Assert.That(stoppedChange, Is.InRange(-0.5f, 0.5f), "radiation 40 did not stop the blood coming back.");
                InBand(failingLoss, 6f, "the failing marrow's loss over 60 s");
                Assert.That(Volume(ipc), Is.GreaterThanOrEqualTo(oilStart - 0.5f), "a chassis lost oil to radiation.");
                // Playtest 3: the marrow's stage on the vitals line, its first aid on "Do first".
                Assert.That(s.Analyzer(failing), Does.Contain("marrow failing")
                    .And.Contain(WolfmedVitalsText.Aid(WolfmedRoutes.Marrow, false)));
                Assert.That(s.Analyzer(stopped), Does.Contain("marrow suppressed"));
            });

            Inject(failing, "Hyronalin", 15);
        });

        int? under = null;
        for (var second = 1; second <= 120 && under == null; second++)
        {
            await RunSeconds(1);
            var now = second;
            await Server.WaitPost(() =>
            {
                if (Radiation.GetRadiation(failing) < 40f)
                    under = now;
            });
        }

        var afterDrug = 0f;
        await Server.WaitPost(() => afterDrug = Volume(failing));
        await RunSeconds(20);
        await Server.WaitAssertion(() =>
        {
            Note($"RadiationScenarioTest: hyronalin took the dose under 40 in {under} s; blood then " +
                                      $"{Volume(failing) - afterDrug:+0.0;-0.0} u in 20 s.");
            Assert.That(under, Is.Not.Null, "hyronalin never took the dose under the marrow line.");
            Assert.That(Volume(failing) - afterDrug, Is.GreaterThan(2f), "the blood did not come back once the dose fell.");
        });
    }

    /// <summary>
    /// <c>SepsisNotToxinTest</c> (OD13): a wound left to go septic, and a body held septic, never gain a toxin load.
    /// Sepsis has its own brain drain; the pools are separate.
    /// </summary>
    [Test]
    public async Task SepsisNotToxinTest()
    {
        await Pin();
        var map = await Pair.CreateTestMap();
        EntityUid wounded = default, septic = default;

        await Server.WaitPost(() =>
        {
            wounded = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            septic = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
        });
        await RunSeconds(2);

        await Server.WaitAssertion(() =>
        {
            var infection = SEntMan.System<WolfmedInfectionSystem>();
            SEntMan.System<DamageableSystem>().TryChangeDamage(wounded, WolfmedScenario.Spec("Slash", 20),
                ignoreResistances: true, targetPart: TargetBodyPart.Torso);
            SEntMan.EnsureComponent<WolfmedSepsisComponent>(septic).Progress = 100f;

            for (var minute = 0; minute < 30; minute++)
                infection.Update(60f);

            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.HasComponent<WolfmedSepsisComponent>(wounded), Is.True, "the wound never went septic.");
                Assert.That(Toxin.GetLoad(wounded), Is.Zero, "infection fed the toxin load.");
                Assert.That(Toxin.GetLoad(septic), Is.Zero, "sepsis fed the toxin load.");
            });
        });
    }

    /// <summary>
    /// <c>AcidResidueTest</c> (P21): acid residue deepens its own chemical burn and nothing else. A plain burn on the
    /// same part keeps its severity, and a part with no plain burn never gets one.
    /// </summary>
    [Test]
    public async Task AcidResidueTest()
    {
        await Pin();
        var map = await Pair.CreateTestMap();
        EntityUid scalded = default, splashed = default;

        await Server.WaitPost(() =>
        {
            // M6: station air. In the default map's vacuum the surface passes the cold damage line about two seconds
            // in, and a cold tick landing on a torso left a plain burn the assertions below read as the residue's.
            var scenario = new WolfmedScenario(SEntMan);
            scenario.SetAir(map.MapUid, true);
            scenario.KeepGrid(map.Grid);
            scalded = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            splashed = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
        });
        await RunSeconds(2);

        await Server.WaitAssertion(() =>
        {
            var damageable = SEntMan.System<DamageableSystem>();
            var residue = SEntMan.System<WolfmedChemicalBurnSystem>();
            var s = new WolfmedScenario(SEntMan);

            damageable.TryChangeDamage(scalded, WolfmedScenario.Spec("Heat", 30), ignoreResistances: true,
                targetPart: TargetBodyPart.Torso);
            foreach (var body in new[] { scalded, splashed })
                damageable.TryChangeDamage(body, WolfmedScenario.Spec("Caustic", 20), ignoreResistances: true,
                    targetPart: TargetBodyPart.Torso);

            var scaldedTorso = s.Part(scalded, BodyPartType.Torso);
            var splashedTorso = s.Part(splashed, BodyPartType.Torso);
            var plainBefore = Severity(scaldedTorso, "BurnWound");
            var acidBefore = Severity(scaldedTorso, "WFWolfmedChemicalBurnWound");
            Assert.Multiple(() =>
            {
                Assert.That(plainBefore, Is.GreaterThan(0f), "the heat left no plain burn to watch.");
                Assert.That(acidBefore, Is.GreaterThan(0f), "the acid left no chemical burn.");
                Assert.That(Severity(splashedTorso, "BurnWound"), Is.Zero);
            });

            for (var tick = 0; tick < 10; tick++)
                residue.Update(5f);

            var acidAfter = Severity(scaldedTorso, "WFWolfmedChemicalBurnWound");
            Note($"AcidResidueTest: ten residue ticks: chemical burn {acidBefore:0.0} -> {acidAfter:0.0}, " +
                                      $"plain burn {plainBefore:0.0} -> {Severity(scaldedTorso, "BurnWound"):0.0}.");
            Assert.Multiple(() =>
            {
                Assert.That(acidAfter, Is.GreaterThan(acidBefore), "the residue did not deepen its own burn.");
                Assert.That(Severity(scaldedTorso, "BurnWound"), Is.EqualTo(plainBefore), "the residue grew the plain burn.");
                Assert.That(Severity(splashedTorso, "BurnWound"), Is.Zero, "the residue made a plain burn.");
                Assert.That(Severity(splashedTorso, "WFWolfmedChemicalBurnWound"), Is.GreaterThan(20f));
            });
        });
    }

    private float Severity(EntityUid part, string prototype) =>
        SEntMan.System<WoundSystem>().GetWounds(part)
            .Where(wound => wound.Comp.Prototype == new ProtoId<WoundPrototype>(prototype))
            .Sum(wound => wound.Comp.Severity.Float());
}
