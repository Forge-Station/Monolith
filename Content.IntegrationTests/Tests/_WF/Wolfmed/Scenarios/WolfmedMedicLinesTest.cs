#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._WF.Wolfmed.Life;
using Content.Shared._Onyx.Body.Systems;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.Consciousness;
using Content.Shared._WF.Wolfmed.Life;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.FixedPoint;
using Content.Shared.Mind;
using NUnit.Framework;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Wolfmed.Scenarios;

/// <summary>
/// M1a package D (plan §5.5, §12 M1a): what the medic reads. The analyzer's vitals block names the state and
/// its cause, the breathing and the circulation for every M1a rung, and the defib verdict never promises a shock
/// will work. Human and IPC only (plan §9.1).
/// </summary>
[TestFixture]
[TestOf(typeof(WolfmedVitalsText))]
public sealed class WolfmedMedicLinesTest : GameTest
{
    private async Task PinLines()
    {
        await OverrideCVar(Side.Server, WolfmedCVars.Consciousness, true);
        await OverrideCVar(Side.Server, WolfmedCVars.ConsciousnessPainDown, 0.95f);
        await OverrideCVar(Side.Server, WolfmedCVars.ConsciousnessPainOut, 1.4f);
        await OverrideCVar(Side.Server, WolfmedCVars.ConsciousnessBloodDown, 0.5f);
        await OverrideCVar(Side.Server, WolfmedCVars.ConsciousnessBloodOut, 0.35f);
        await OverrideCVar(Side.Server, WolfmedCVars.ConsciousnessHysteresis, 0.1f);
        await OverrideCVar(Side.Server, WolfmedCVars.BloodBandPale, 0.8f);
        await OverrideCVar(Side.Server, WolfmedCVars.PainFaintSeconds, 20f);
        await OverrideCVar(Side.Server, WolfmedCVars.PainShockThreshold, 130f);
        await OverrideCVar(Side.Server, WolfmedCVars.ArrestShockBlood, 0f);
        await OverrideCVar(Side.Server, WolfmedCVars.ArrestBlood, 0.30f);
        await OverrideCVar(Side.Server, WolfmedCVars.ArrestOxygenation, 0.15f);
        await OverrideCVar(Side.Server, WolfmedCVars.BrainBloodStart, 0.5f);
        await OverrideCVar(Side.Server, WolfmedCVars.BrainPressureStart, 0.75f);
        await OverrideCVar(Side.Server, WolfmedCVars.BrainPressureOut, 0.45f);
        await OverrideCVar(Side.Server, WolfmedCVars.DefibBlood, 0.25f);
        await OverrideCVar(Side.Server, WolfmedCVars.PostShockBloodTarget, 0.35f);
        await OverrideCVar(Side.Server, WolfmedCVars.PostShockGraceSeconds, 45f);
        await OverrideCVar(Side.Server, WolfmedCVars.AnalyzerBloodFast, 1f);
    }

    /// <summary>Pain on one part with its wound floor at the same value, so it does not recover away.</summary>
    private void SetPain(EntityUid body, BodyPartType type, float value)
    {
        var part = SEntMan.System<SharedBodySystem>().GetBodyChildren(body)
            .First(p => p.Component.PartType == type).Id;
        var pain = SEntMan.GetComponent<PainComponent>(part);
        pain.WoundPain = FixedPoint2.New(value);
        SEntMan.System<PainSystem>().SetPain((part, pain), FixedPoint2.New(value));
    }

    /// <summary>
    /// <c>AnalyzerStateLinesTest</c> (plan §12 M1a): Up, Downed (pain, blood), Faint, Unconscious (blood,
    /// hypoxia), arrest (refused, then indicated), dead with a destroyed brain, and a shut-down IPC. Each reads
    /// the §5.5 state-and-cause line, and <c>BloodBand</c> matches the blood %. Playtest 3: breathing and circulation
    /// are items on the one vitals line, listed only when not normal; the transfusion moved to "Do first".
    /// </summary>
    [Test]
    public async Task AnalyzerStateLinesTest()
    {
        await PinLines();
        var map = await Pair.CreateTestMap();
        var s = new WolfmedScenario(SEntMan);
        EntityUid up = default, pain = default, bled = default, faint = default, pale = default,
            bledOut = default, hypoxic = default, arrest = default, dead = default, ipc = default;

        await Server.WaitPost(() =>
        {
            s.SetAir(map.MapUid, true);
            up = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            pain = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            bled = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            faint = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            pale = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            bledOut = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            hypoxic = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            arrest = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            dead = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            ipc = SEntMan.SpawnEntity("MobIPC", map.GridCoords);

            // The charge loop skips a chassis with no mind.
            var minds = SEntMan.System<SharedMindSystem>();
            minds.TransferTo(minds.CreateMind(null).Owner, ipc);
        });
        await RunSeconds(2);

        await Server.WaitPost(() =>
        {
            // Downed by pain: body pain 129, under the 130 shock and the 189 faint line.
            SetPain(pain, BodyPartType.Torso, 129f);

            // Downed by blood at 45%, Unconscious by blood at 33%, pale at 70%.
            s.SetBlood(bled, 0.45f);
            s.SetBlood(bledOut, 0.33f);
            s.SetBlood(pale, 0.70f);

            // A pain faint: summed 199.
            SetPain(faint, BodyPartType.Torso, 129f);
            SetPain(faint, BodyPartType.Head, 70f);

            // The heart stops at 29%.
            s.SetBlood(arrest, 0.29f);
            s.Advance(arrest, 1);

            // Dead of a destroyed brain.
            var brain = s.Life.GetBrainOrgan(dead)!.Value;
            SEntMan.System<OrganHealthSystem>().SetHealth(brain, FixedPoint2.Zero);
            s.Life.Kill(dead);

            // The IPC's cell out.
            var slots = SEntMan.System<ItemSlotsSystem>();
            Assert.That(slots.TryGetSlot(ipc, "cell_slot", out var slot), Is.True, "the IPC has no cell slot.");
            Assert.That(SEntMan.System<SharedContainerSystem>().Remove(slot!.Item!.Value, slot.ContainerSlot!), Is.True);

            foreach (var body in new[] { pain, bled, bledOut, pale, faint })
                s.Consciousness.Refresh(body);
        });
        await RunSeconds(2);

        await Server.WaitAssertion(() =>
        {
            // Hypoxic: oxygenation under the 0.45 Unconscious line, the pressure rewritten at once.
            s.Life.SetOxygenation(hypoxic, 0.42f);
            s.Life.Tick(hypoxic, 0.0001f);
            s.Consciousness.Refresh(hypoxic);

            foreach (var body in new[] { up, pain, bled, pale, faint, bledOut, hypoxic, arrest, dead, ipc })
                TestContext.Out.WriteLine(s.Analyzer(body));

            var lines = s.AnalyzerLines(up);
            Assert.Multiple(() =>
            {
                Assert.That(lines[0], Is.EqualTo("CONSCIOUS"));
                Assert.That(lines[1], Is.EqualTo("Vitals normal"), "a healthy patient listed a vital.");
                Assert.That(lines, Has.Length.EqualTo(2), "a conscious patient got a defib verdict or a do-first line.");
                Assert.That(s.Report(up).BloodBand, Is.EqualTo(WolfmedBloodBand.Normal));
            });

            lines = s.AnalyzerLines(pain);
            Assert.Multiple(() =>
            {
                Assert.That(s.State(pain), Is.EqualTo(WolfmedConsciousness.Downed));
                Assert.That(lines[0], Is.EqualTo("DOWNED: pain"));
                Assert.That(lines[1], Is.EqualTo("Vitals normal"));
                // M2 (plan §5.5): a patient who is down reads what is getting worse, here nothing.
                Assert.That(lines[2], Is.EqualTo("Do first: nothing; stable"));
                Assert.That(lines, Has.Length.EqualTo(3), "a Downed patient got a defib verdict.");
            });

            lines = s.AnalyzerLines(bled);
            Assert.Multiple(() =>
            {
                Assert.That(lines[0], Is.EqualTo("DOWNED: blood loss"));
                Assert.That(lines[1], Is.EqualTo("Pulse weak, rapid · Blood 45% ↑"), "normal breathing is listed.");
                Assert.That(lines[2], Does.Contain(
                    $"transfuse ≈ {MathF.Ceiling(s.Life.GetTransfusionGuidance(bled).ToBrainSafe)} u"));
                Assert.That(lines[1], Does.Not.Contain("transfuse"), "the units are on the vitals line too.");
                Assert.That(s.Life.GetTransfusionGuidance(bled).ToBrainSafe,
                    Is.EqualTo(0.5f * s.Pool(bled) - s.Blood(bled) * s.Pool(bled)).Within(0.5f));
                Assert.That(s.Report(bled).BloodBand, Is.EqualTo(WolfmedBloodBand.Weak));
            });

            Assert.Multiple(() =>
            {
                Assert.That(s.Report(pale).BloodBand, Is.EqualTo(WolfmedBloodBand.Low));
                Assert.That(s.AnalyzerLines(pale)[1], Does.StartWith("Pale · Blood 70%"));
            });

            lines = s.AnalyzerLines(faint);
            Assert.Multiple(() =>
            {
                // Playtest 2: the faint's seconds left.
                Assert.That(lines[0], Does.Match(@"^FAINTED: pain, \d+ s$"));
                Assert.That(lines[1], Does.Not.Contain("reathing"), "a fainted patient is breathing (plan §4).");
                Assert.That(lines[^1], Is.EqualTo("Defib: refused: pulse present"));
            });

            lines = s.AnalyzerLines(bledOut);
            Assert.Multiple(() =>
            {
                Assert.That(lines[0], Is.EqualTo("UNCONSCIOUS: blood loss"));
                Assert.That(lines[1], Does.StartWith("Pulse barely palpable · Blood 33%"), "normal breathing is listed.");
                Assert.That(lines[^1], Is.EqualTo("Defib: refused: pulse present"));
                Assert.That(s.Report(bledOut).BloodBand, Is.EqualTo(WolfmedBloodBand.Critical));
            });

            lines = s.AnalyzerLines(hypoxic);
            Assert.Multiple(() =>
            {
                Assert.That(lines[0], Does.StartWith("UNCONSCIOUS: no oxygen"));
                Assert.That(lines[1], Does.Not.Contain("reathing"), "the chest is working; the brain is what is short.");
            });

            lines = s.AnalyzerLines(arrest);
            Assert.Multiple(() =>
            {
                Assert.That(s.Life.InArrest(arrest), Is.True, "29% blood did not stop the heart.");
                Assert.That(lines[0], Is.EqualTo("CARDIAC ARREST: blood"),
                    "the blood that stopped the heart was named twice.");
                Assert.That(lines[1], Does.StartWith("Not breathing · No pulse · Blood 29%"));
                Assert.That(lines[^1], Is.EqualTo("Defib: shock indicated"), "29% is over the 25% gate.");
                Assert.That(s.Report(arrest).BloodBand, Is.EqualTo(WolfmedBloodBand.None));
            });

            // Under the gate the paddles refuse, and the verdict names the units.
            s.SetBlood(arrest, 0.24f);
            var (units, safe) = s.Life.GetTransfusionGuidance(arrest);
            Assert.That(s.AnalyzerLines(arrest)[^1], Is.EqualTo(
                $"Defib: refused: blood 24%, transfuse ≈ {MathF.Ceiling(units)} u first (≈ {MathF.Ceiling(safe)} u to 50%)"));
            Assert.That(units, Is.EqualTo((0.35f - 0.24f) * s.Pool(arrest)).Within(1f), "N is the units to 35%.");
            Assert.That(string.Join(" ", s.AnalyzerLines(arrest)), Does.Not.Contain("will work"));

            lines = s.AnalyzerLines(dead);
            Assert.Multiple(() =>
            {
                Assert.That(lines[0], Is.EqualTo("DEAD: catastrophic brain injury"));
                Assert.That(lines[1], Does.StartWith("Not breathing"));
                // Playtest 5: a destroyed brain no longer refuses the paddles; the patient returns with major brain damage.
                Assert.That(lines[^1], Is.EqualTo("Defib: shock indicated"));
            });

            lines = s.AnalyzerLines(ipc);
            Assert.Multiple(() =>
            {
                // Playtest 3: the pump running and full oil are normal, so the chassis's vitals line says so.
                Assert.That(lines[0], Is.EqualTo("SHUTDOWN: no power"));
                Assert.That(lines[1], Is.EqualTo("Vitals normal"));
                Assert.That(lines.Any(line => line.StartsWith("Defib")), Is.False, "a chassis got a defib verdict.");
                Assert.That(lines.Any(line => line.Contains("ulse") || line.Contains("reathing") ||
                                              line.Contains("Blood")), Is.False,
                    "a chassis read in organic words.");
            });
        });
    }

    /// <summary>
    /// The vitals words are built from enum names, so a new member is a missing string nothing else catches.
    /// Every state, cause, source, breathing, pulse, trend and verdict key resolves.
    /// </summary>
    [Test]
    public async Task VitalsWordsResolveTest()
    {
        var locale = Server.ResolveDependency<Robust.Shared.Localization.ILocalizationManager>();
        await Server.WaitAssertion(() =>
        {
            string Key(string family, Enum member) => $"wolfmed-vitals-{family}-{member.ToString().ToLowerInvariant()}";
            var keys = new System.Collections.Generic.List<string>
            {
                "wolfmed-vitals-state-up", "wolfmed-vitals-state-up-mechanical", "wolfmed-vitals-state-dead",
                "wolfmed-vitals-state-dead-brain", "wolfmed-vitals-state-dead-mechanical", "wolfmed-vitals-blockers",
                "wolfmed-vitals-cause-pain-mechanical", "wolfmed-vitals-cause-with-source",
                "wolfmed-vitals-breathing-depressed", "wolfmed-vitals-breathing-gasping", "wolfmed-vitals-cooling-offline",
                // Playtest 3: the compact block's own words.
                "wolfmed-vitals-normal", "wolfmed-vitals-separator", "wolfmed-vitals-item-blood", "wolfmed-vitals-item-oil",
                "wolfmed-vitals-item-burn-fluid", "wolfmed-vitals-item-toxins", "wolfmed-vitals-item-liver-missing",
                "wolfmed-vitals-do-first", "wolfmed-vitals-do-first-none", "wolfmed-vitals-aid-transfuse",
                "wolfmed-vitals-aid-refill",
            };

            foreach (var state in Enum.GetValues<WolfmedVitalsState>())
            {
                if (state is not (WolfmedVitalsState.Up or WolfmedVitalsState.Dead))
                    keys.Add(Key("state", state));
            }

            keys.AddRange(Enum.GetValues<WolfmedCause>().Select(cause => Key("cause", cause)));
            keys.AddRange(Enum.GetValues<WolfmedCauseSource>().Select(source => Key("source", source)));
            keys.AddRange(Enum.GetValues<WolfmedBreathingSource>().Select(source => Key("breathing-none", source)));
            // A normal pulse is never listed (playtest 3).
            keys.AddRange(Enum.GetValues<WolfmedBloodBand>().Where(band => band != WolfmedBloodBand.Normal)
                .Select(band => Key("pulse", band)));
            keys.AddRange(Enum.GetValues<WolfmedBloodTrend>().Select(trend => Key("trend", trend)));
            keys.AddRange(Enum.GetValues<WolfmedDefibVerdict>()
                .Where(verdict => verdict != WolfmedDefibVerdict.Hidden)
                .Select(verdict => Key("verdict", verdict)));

            Assert.Multiple(() =>
            {
                foreach (var key in keys)
                    Assert.That(locale.HasString(key), Is.True, $"{key} has no string.");
            });
        });
    }
}
