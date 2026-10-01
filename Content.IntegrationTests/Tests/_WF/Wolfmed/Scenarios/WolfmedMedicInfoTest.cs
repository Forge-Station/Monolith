#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.Client.Ghost.UI;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._WF.Wolfmed.Consciousness;
using Content.Server._WF.Wolfmed.Life;
using Content.Server.Body.Systems;
using Content.Server.Chat.Systems;
using Content.Server.Medical;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.Consciousness;
using Content.Shared._WF.Wolfmed.Examine;
using Content.Shared._WF.Wolfmed.Life;
using Content.Shared._WF.Wolfmed.Reagents;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.ActionBlocker;
using Content.Shared.Actions;
using Content.Shared.Body.Part;
using Content.Shared.Chat;
using Content.Shared.Chemistry.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Ghost;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Mind;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Events;
using Content.Shared.Players;
using NUnit.Framework;
using Robust.Client.UserInterface;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Wolfmed.Scenarios;

/// <summary>
/// M2 (plan §5.2-5.5, §3.4, OD7 (c), OD8, OD14, OD20, P30): what the patient and the medic are told. The analyzer's
/// full vitals block with the routes and the last arrest; the explanation card on the unconscious screen; the sedation
/// model with its warnings and antagonist; the crawling stage's Play dead and adjacent aid; wait as a ghost.
/// </summary>
[TestFixture]
[TestOf(typeof(WolfmedVitalsText))]
public sealed class WolfmedMedicInfoTest : GameTest
{
    private async Task Pin()
    {
        await OverrideCVar(Side.Server, WolfmedCVars.Consciousness, true);
        await OverrideCVar(Side.Server, WolfmedCVars.ConsciousnessPainDown, 0.95f);
        await OverrideCVar(Side.Server, WolfmedCVars.ConsciousnessPainOut, 1.4f);
        await OverrideCVar(Side.Server, WolfmedCVars.ConsciousnessBloodDown, 0.5f);
        await OverrideCVar(Side.Server, WolfmedCVars.ConsciousnessBloodOut, 0.35f);
        await OverrideCVar(Side.Server, WolfmedCVars.ConsciousnessHysteresis, 0.1f);
        await OverrideCVar(Side.Server, WolfmedCVars.PainFaintSeconds, 20f);
        await OverrideCVar(Side.Server, WolfmedCVars.PainShockThreshold, 130f);
        await OverrideCVar(Side.Server, WolfmedCVars.ArrestShockBlood, 0f);
        await OverrideCVar(Side.Server, WolfmedCVars.ArrestBlood, 0.30f);
        await OverrideCVar(Side.Server, WolfmedCVars.ArrestOxygenation, 0.15f);
        await OverrideCVar(Side.Server, WolfmedCVars.ArrestSepsis, 80f);
        await OverrideCVar(Side.Server, WolfmedCVars.BrainBloodStart, 0.5f);
        await OverrideCVar(Side.Server, WolfmedCVars.BrainPressureStart, 0.75f);
        await OverrideCVar(Side.Server, WolfmedCVars.BrainPressureOut, 0.45f);
        await OverrideCVar(Side.Server, WolfmedCVars.DefibBlood, 0.25f);
        await OverrideCVar(Side.Server, WolfmedCVars.PostShockBloodTarget, 0.35f);
        await OverrideCVar(Side.Server, WolfmedCVars.PostShockGraceSeconds, 45f);
        await OverrideCVar(Side.Server, WolfmedCVars.SedationRise, 0.05f);
        await OverrideCVar(Side.Server, WolfmedCVars.SedationWarn, 0.4f);
        await OverrideCVar(Side.Server, WolfmedCVars.SedationWarnHeavy, 0.8f);
        await OverrideCVar(Side.Server, WolfmedCVars.ArrestCauseMemorySeconds, 300f);
        await OverrideCVar(Side.Server, WolfmedCVars.DormantOfferSeconds, 90f);
        await OverrideCVar(Side.Server, WolfmedCVars.CardExaminedSeconds, 3f);
        await OverrideCVar(Side.Server, WolfmedCVars.DownedReach, 1.5f);
    }

    private WolfmedConsciousnessComponent Consc(EntityUid body) => SEntMan.GetComponent<WolfmedConsciousnessComponent>(body);

    /// <summary>Pain on one part with its wound floor at the same value, so it does not recover away.</summary>
    private void SetPain(EntityUid body, BodyPartType type, float value)
    {
        var part = new WolfmedScenario(SEntMan).Part(body, type);
        var pain = SEntMan.GetComponent<PainComponent>(part);
        pain.WoundPain = FixedPoint2.New(value);
        SEntMan.System<PainSystem>().SetPain((part, pain), FixedPoint2.New(value));
    }

    private List<string> ActionIds(EntityUid body) =>
        SEntMan.System<SharedActionsSystem>().GetActions(body)
            .Select(action => SEntMan.GetComponent<MetaDataComponent>(action.Id).EntityPrototype?.ID ?? string.Empty)
            .ToList();

    private static DamageSpecifier Spec(string type, float amount) => new()
    {
        DamageDict = { [new ProtoId<DamageTypePrototype>(type)] = FixedPoint2.New(amount) },
    };

    /// <summary>Puts the test player into a fresh body. Returns the body and its mind.</summary>
    private async Task<(EntityUid Body, EntityUid Mind)> Possess(string prototype, EntityCoordinates coords)
    {
        Assert.That(ServerSession, Is.Not.Null, "This test needs a connected pair.");
        var session = ServerSession!;
        var minds = SEntMan.System<SharedMindSystem>();
        EntityUid body = default, mind = default;
        await Server.WaitPost(() =>
        {
            minds.WipeMind(session.ContentData()?.Mind);
            body = SEntMan.SpawnEntity(prototype, coords);
            mind = minds.CreateMind(session.UserId).Owner;
            minds.TransferTo(mind, body);
        });
        await RunTicksSync(30);
        Assert.That(session.AttachedEntity, Is.EqualTo(body), "the player did not attach to the new body.");
        return (body, mind);
    }

    private async Task<int> ClientWindows<T>() where T : Control
    {
        var count = 0;
        await Client.WaitPost(() => count = Client.ResolveDependency<IUserInterfaceManager>().WindowRoot.Children
            .OfType<T>().Count(window => window.Visible));
        return count;
    }

    /// <summary>
    /// <c>AnalyzerVitalsTest</c> (plan §5.5): the routes line (playtest 3: "Do first") names each running route's first
    /// aid and says "nothing; stable" for a stable patient; after a restart the block says why the heart stopped and
    /// whether that is still there, with the transfusion numbers while blood is, for 300 s and gone after; the defib verdict reads
    /// "shock indicated" and never promises.
    /// </summary>
    [Test]
    public async Task AnalyzerVitalsTest()
    {
        await Pin();
        var map = await Pair.CreateTestMap();
        var s = new WolfmedScenario(SEntMan);
        EntityUid calm = default, bleeder = default, septic = default, overdosed = default, arrest = default;

        await Server.WaitPost(() =>
        {
            s.SetAir(map.MapUid, true);
            calm = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            bleeder = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            septic = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            overdosed = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            arrest = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
        });
        await RunSeconds(2);

        await Server.WaitPost(() =>
        {
            s.SetBlood(bleeder, 0.45f);
            SEntMan.EnsureComponent<WolfmedSepsisComponent>(septic).Progress = 85f;
            SEntMan.System<WolfmedPainReliefSystem>().AddDose(overdosed, "overdose", WolfmedPainReliefTier.Strong, 0f,
                TimeSpan.FromSeconds(120), 1.5f);
        });
        await RunSeconds(16);

        await Server.WaitAssertion(() =>
        {
            // A fresh cut now, so it is still bleeding when the analyzer reads it.
            SEntMan.System<DamageableSystem>().TryChangeDamage(bleeder, Spec("Slash", 25), ignoreResistances: true,
                targetPart: TargetBodyPart.LeftArm);
            // Playtest 3: the routes line is "Do first:", each route's aid once; the transfusion carries its units.
            var calmLine = WolfmedVitalsText.DoFirstLine(s.Report(calm));
            var bleederReport = s.Report(bleeder);
            var bleederLine = WolfmedVitalsText.DoFirstLine(bleederReport);
            var septicLine = WolfmedVitalsText.DoFirstLine(s.Report(septic));
            var overdoseLine = WolfmedVitalsText.DoFirstLine(s.Report(overdosed));
            TestContext.Out.WriteLine($"AnalyzerVitalsTest routes:\n  {calmLine}\n  {bleederLine}\n  {septicLine}\n  {overdoseLine}");
            Assert.Multiple(() =>
            {
                Assert.That(calmLine, Is.Null, "somebody up with nothing running got a routes line.");
                Assert.That(bleederLine, Does.Contain(WolfmedVitalsText.Aid(WolfmedRoutes.Bleeding, false)));
                Assert.That(bleederLine, Does.Contain(Loc.GetString("wolfmed-vitals-aid-transfuse",
                    ("units", WolfmedVitalsText.Units(bleederReport.UnitsToLine)))));
                // INFECTION: 85, still past wolfmed.septic_shock_at (80) after 16 s of recovery, is septic shock.
                Assert.That(septicLine, Does.Contain(WolfmedVitalsText.Aid(WolfmedRoutes.Sepsis, false, shock: true)));
                Assert.That(overdoseLine, Does.Contain(WolfmedVitalsText.Aid(WolfmedRoutes.Sedation, false)));
                Assert.That(s.AnalyzerLines(bleeder), Does.Contain(bleederLine), "the routes line is not in the block.");
            });

            // --- Arrest at 27% blood: the verdict, then a shock and the "After a restart" line. ---
            s.SetBlood(arrest, 0.27f);
            s.Advance(arrest, 1);
            Assert.That(s.Life.InArrest(arrest), Is.True);
            Assert.That(s.AnalyzerLines(arrest), Does.Contain(Loc.GetString("wolfmed-vitals-verdict-indicated")));
            Assert.That(s.Analyzer(arrest), Does.Not.Contain("will work"));

            Assert.That(s.Shock(arrest, out _), Is.True);
            var report = s.Report(arrest);
            var restart = WolfmedVitalsText.RestartLine(report);
            TestContext.Out.WriteLine($"AnalyzerVitalsTest after the shock: {restart}");
            Assert.Multiple(() =>
            {
                Assert.That(report.RestartCause, Is.EqualTo(WolfmedCauseSource.ArrestBlood));
                Assert.That(report.RestartPresent, Is.True);
                Assert.That(restart, Does.StartWith("After a restart: arrest cause blood. Still present: yes. Transfuse ≈"));
                Assert.That(restart, Does.Contain("within 45 s"));
                Assert.That(restart, Does.Contain("to 50% to stop the brain injury"));
            });

            // Transfused to 60%: the cause is gone but the line stays for the memory window.
            s.SetBlood(arrest, 0.6f);
            Assert.That(WolfmedVitalsText.RestartLine(s.Report(arrest)),
                Is.EqualTo(Loc.GetString("wolfmed-vitals-restart", ("cause", "blood"), ("present", Loc.GetString("wolfmed-vitals-no")))));

            var memory = SEntMan.GetComponent<WolfmedArrestMemoryComponent>(arrest);
            memory.RestartedAt -= TimeSpan.FromSeconds(290);
            s.Advance(arrest, 1);
            Assert.That(WolfmedVitalsText.RestartLine(s.Report(arrest)), Is.Not.Null, "the arrest was forgotten inside 300 s.");

            memory.RestartedAt -= TimeSpan.FromSeconds(11);
            s.Advance(arrest, 1);
            Assert.That(WolfmedVitalsText.RestartLine(s.Report(arrest)), Is.Null, "the arrest was still shown after 300 s.");
        });
    }

    /// <summary>
    /// <c>ExplanationCardTest</c> (plan §5.2, §2.3): the card for a faint, a blood Unconscious and an arrest reads the
    /// cause prototype: title, symptom, what wakes you (the blocked form while something else holds you, so a blocked
    /// faint does not promise coming round). In arrest a coarse bar counts the brain down with no seconds, and the
    /// rescue lines appear with CPR and with an analyzer on the body.
    /// </summary>
    [Test]
    public async Task ExplanationCardTest()
    {
        await Pin();
        var map = await Pair.CreateTestMap();
        var s = new WolfmedScenario(SEntMan);
        var alerts = SEntMan.System<WolfmedConditionAlertSystem>();
        var cards = SEntMan.System<WolfmedCardSystem>();
        EntityUid faint = default, blockedFaint = default, bled = default, arrest = default;

        await Server.WaitPost(() =>
        {
            s.SetAir(map.MapUid, true);
            faint = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            blockedFaint = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            bled = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            arrest = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
        });
        await RunSeconds(2);

        await Server.WaitAssertion(() =>
        {
            SetPain(faint, BodyPartType.Torso, 100);
            SetPain(faint, BodyPartType.Head, 100);
            s.SetBlood(blockedFaint, 0.40f);
            SetPain(blockedFaint, BodyPartType.Torso, 100);
            SetPain(blockedFaint, BodyPartType.Head, 100);
            s.SetBlood(bled, 0.33f);
            s.SetBlood(arrest, 0.29f);
            s.Advance(arrest, 1);

            foreach (var body in new[] { faint, blockedFaint, bled, arrest })
                cards.Refresh(body);

            List<string> Card(EntityUid body) =>
                WolfmedExplanationCard.Lines(SProtoMan, Consc(body), SEntMan.GetComponentOrNull<WolfmedCardComponent>(body));

            var faintCard = Card(faint);
            var blockedCard = Card(blockedFaint);
            var bledCard = Card(bled);
            var arrestCard = Card(arrest);
            TestContext.Out.WriteLine("ExplanationCardTest:\n  " + string.Join("\n  ",
                new[] { faintCard, blockedCard, bledCard, arrestCard }.Select(card => string.Join(" / ", card))));

            Assert.Multiple(() =>
            {
                Assert.That(Consc(faint).Cause, Is.EqualTo(WolfmedCause.PainFaint));
                Assert.That(faintCard[0], Is.EqualTo(alerts.GetTitle(faint)));
                Assert.That(faintCard, Does.Contain(Loc.GetString("wolfmed-cause-pain-faint-symptom")));
                Assert.That(faintCard, Does.Contain(Loc.GetString("wolfmed-cause-pain-faint-help")));

                Assert.That(Consc(blockedFaint).Blockers, Is.Not.EqualTo(WolfmedCauseFlags.None));
                Assert.That(blockedCard, Does.Contain(Loc.GetString("wolfmed-cause-pain-faint-help-blocked")));
                Assert.That(blockedCard, Does.Not.Contain(Loc.GetString("wolfmed-cause-pain-faint-help")),
                    "a faint something else holds still promises coming round shortly.");
                Assert.That(blockedCard.Any(line => line.StartsWith("Also holding you down:")), Is.True);

                Assert.That(Consc(bled).Cause, Is.EqualTo(WolfmedCause.Blood));
                Assert.That(bledCard[0], Is.EqualTo(alerts.GetTitle(bled)));
                Assert.That(bledCard, Does.Contain(Loc.GetString("wolfmed-cause-blood-symptom")));
                Assert.That(bledCard, Does.Contain(Loc.GetString("wolfmed-cause-blood-help-out")));

                Assert.That(Consc(arrest).Cause, Is.EqualTo(WolfmedCause.Arrest));
                Assert.That(arrestCard[0], Is.EqualTo(alerts.GetTitle(arrest)));
                Assert.That(arrestCard, Does.Contain(Loc.GetString("wolfmed-cause-arrest-symptom")));
                Assert.That(arrestCard, Does.Contain(Loc.GetString("wolfmed-cause-arrest-help-out")));
                Assert.That(arrestCard.Any(line => line.Any(char.IsDigit)), Is.False, "the arrest card shows a number.");
            });

            Assert.That(WolfmedExplanationCard.Bar(SProtoMan, Consc(faint), SEntMan.GetComponentOrNull<WolfmedCardComponent>(faint)),
                Is.Null, "a faint has a countdown bar.");
            var full = WolfmedExplanationCard.Bar(SProtoMan, Consc(arrest), SEntMan.GetComponent<WolfmedCardComponent>(arrest));
            Assert.That(full?.Tenths, Is.EqualTo(10), "the bar does not start full.");

            s.Advance(arrest, 90);
            cards.Refresh(arrest);
            var later = WolfmedExplanationCard.Bar(SProtoMan, Consc(arrest), SEntMan.GetComponent<WolfmedCardComponent>(arrest));
            Assert.That(later?.Tenths, Is.LessThan(10).And.GreaterThan(0), "the bar did not count the brain down.");

            // Hands on the chest, then an analyzer: the patient learns help has arrived.
            Assert.That(s.Revival.StartCpr(arrest, TimeSpan.FromSeconds(10)), Is.True);
            cards.Refresh(arrest);
            Assert.That(Card(arrest), Does.Contain(Loc.GetString("wolfmed-card-cpr")));
            Assert.That(Card(arrest), Does.Not.Contain(Loc.GetString("wolfmed-card-examined")));
            s.Report(arrest);
            Assert.That(Card(arrest), Does.Contain(Loc.GetString("wolfmed-card-examined")));
        });
    }

    /// <summary>
    /// <c>SedationModelTest</c> (plan §3.4, OD14): one standard dose of each strong painkiller (the 3 u opiate pen, 5 u
    /// of tramadol or oxycodone) levels off under the 0.6 breathing line; two put the patient on the floor. The three
    /// warnings fire once each as sedation climbs through 0.4, the breathing line and 0.8. Naloxone wakes an overdose.
    /// No Asphyxiation anywhere.
    /// </summary>
    [Test]
    public async Task SedationModelTest()
    {
        await Pin();
        var map = await Pair.CreateTestMap();
        var s = new WolfmedScenario(SEntMan);
        var relief = SEntMan.System<WolfmedPainReliefSystem>();
        var doses = new (string Reagent, float Units)[] { ("WFWolfmedOpiate", 3f), ("Tramadol", 5f), ("Oxycodone", 5f) };
        var single = new EntityUid[doses.Length];
        var twice = new EntityUid[doses.Length];
        EntityUid climb = default, reversed = default;

        await Server.WaitPost(() =>
        {
            s.SetAir(map.MapUid, true);
            for (var i = 0; i < doses.Length; i++)
            {
                single[i] = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
                twice[i] = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            }

            climb = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            reversed = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
        });
        await RunSeconds(2);

        await Server.WaitPost(() =>
        {
            var bloodstream = SEntMan.System<BloodstreamSystem>();
            for (var i = 0; i < doses.Length; i++)
            {
                bloodstream.TryAddToChemicals(single[i], new Solution(doses[i].Reagent, FixedPoint2.New(doses[i].Units)));
                bloodstream.TryAddToChemicals(twice[i], new Solution(doses[i].Reagent, FixedPoint2.New(doses[i].Units * 2f)));
            }

            // A dose held at full sedation: the warnings in order, then naloxone on a second body.
            relief.AddDose(climb, "overdose", WolfmedPainReliefTier.Strong, 0f, TimeSpan.FromSeconds(120), 1.5f);
            relief.AddDose(reversed, "overdose", WolfmedPainReliefTier.Strong, 0f, TimeSpan.FromSeconds(120), 1.5f);
        });

        var singlePeak = new float[doses.Length];
        var twicePeak = new float[doses.Length];
        var twiceDowned = new bool[doses.Length];
        var warnings = new List<(float Sedation, string Line)>();
        var lastLine = string.Empty;
        for (var tick = 0; tick < 120; tick++)
        {
            await RunSeconds(0.5f);
            await Server.WaitPost(() =>
            {
                for (var i = 0; i < doses.Length; i++)
                {
                    singlePeak[i] = MathF.Max(singlePeak[i], relief.GetSedation(single[i]));
                    twicePeak[i] = MathF.Max(twicePeak[i], relief.GetSedation(twice[i]));
                    twiceDowned[i] |= Consc(twice[i]).State == WolfmedConsciousness.Downed &&
                                      Consc(twice[i]).Cause == WolfmedCause.Sedation;
                }

                var line = Consc(climb).LastConditionLine;
                if (line != lastLine)
                {
                    warnings.Add((relief.GetSedation(climb), line));
                    lastLine = line;
                }
            });
        }

        for (var i = 0; i < doses.Length; i++)
            TestContext.Out.WriteLine($"SedationModelTest: {doses[i].Units} u {doses[i].Reagent}: peak {singlePeak[i]:0.00}; " +
                                      $"doubled {twicePeak[i]:0.00}, Downed by sedation {twiceDowned[i]}.");
        TestContext.Out.WriteLine("SedationModelTest lines: " + string.Join(" | ", warnings.Select(w => $"{w.Sedation:0.00} {w.Line}")));

        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                for (var i = 0; i < doses.Length; i++)
                {
                    Assert.That(singlePeak[i], Is.GreaterThan(0.2f), $"{doses[i].Reagent} barely sedates.");
                    Assert.That(singlePeak[i], Is.LessThan(0.6f), $"one dose of {doses[i].Reagent} depresses breathing.");
                    Assert.That(twiceDowned[i], Is.True, $"two doses of {doses[i].Reagent} did not put the patient down.");
                }

                for (var level = 1; level <= 3; level++)
                {
                    var text = Loc.GetString($"wolfmed-sedation-warn-{level}");
                    var seen = warnings.Where(w => w.Line == text).ToList();
                    Assert.That(seen, Has.Count.EqualTo(1), $"warning {level} fired {seen.Count} times.");
                }

                var at = warnings.ToDictionary(w => w.Line, w => w.Sedation);
                Assert.That(at[Loc.GetString("wolfmed-sedation-warn-1")], Is.InRange(0.4f, 0.5f));
                Assert.That(at[Loc.GetString("wolfmed-sedation-warn-2")], Is.InRange(0.6f, 0.7f));
                Assert.That(at[Loc.GetString("wolfmed-sedation-warn-3")], Is.InRange(0.8f, 0.9f));
            });

            // Full overdose: out. Naloxone brings the sedation down and holds it there.
            Assert.That(Consc(reversed).State, Is.EqualTo(WolfmedConsciousness.Unconscious), "the overdose did not knock out.");
            SEntMan.System<BloodstreamSystem>().TryAddToChemicals(reversed, new Solution("WFWolfmedNaloxone", FixedPoint2.New(5)));
        });

        // The 5 u pen metabolises over about ten seconds, 0.3 off the sedation per unit.
        float? woke = null;
        var lowest = 1f;
        for (var waited = 0.5f; waited <= 12f; waited += 0.5f)
        {
            await RunSeconds(0.5f);
            var now = waited;
            await Server.WaitPost(() =>
            {
                if (woke == null && Consc(reversed).State != WolfmedConsciousness.Unconscious)
                    woke = now;
                lowest = MathF.Min(lowest, relief.GetSedation(reversed));
            });
        }

        await Server.WaitAssertion(() =>
        {
            TestContext.Out.WriteLine($"SedationModelTest: naloxone woke the overdose after {woke} s; lowest sedation {lowest:0.00}, " +
                                      $"now {relief.GetSedation(reversed):0.00}.");
            Assert.That(woke, Is.Not.Null, "naloxone did not wake an overdose.");
            Assert.That(lowest, Is.LessThan(0.6f), "naloxone did not bring the sedation under the breathing line.");
            Assert.That(Consc(reversed).State, Is.Not.EqualTo(WolfmedConsciousness.Unconscious));

            foreach (var body in single.Concat(twice).Append(climb).Append(reversed))
                Assert.That(s.Damage(body, "Asphyxiation"), Is.EqualTo(FixedPoint2.Zero), "sedation dealt Asphyxiation.");
        });
    }

    /// <summary>
    /// <c>StimOnStrongTest</c> (P30): a stimulant on top of a strong painkiller keeps the strong painkiller's masking of
    /// the wound slowdowns, as the guidebook says, instead of bringing them back because the stimulant's tier is higher.
    /// </summary>
    [Test]
    public async Task StimOnStrongTest()
    {
        await Pin();
        var map = await Pair.CreateTestMap();
        EntityUid both = default, stimOnly = default;

        await Server.WaitPost(() =>
        {
            both = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            stimOnly = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
        });
        await RunSeconds(1);

        await Server.WaitAssertion(() =>
        {
            var relief = SEntMan.System<WolfmedPainReliefSystem>();
            var window = TimeSpan.FromSeconds(60);
            relief.AddDose(both, "opiate", WolfmedPainReliefTier.Strong, 70f, window, 0f);
            Assert.That(relief.MasksSlowdown(both), Is.True, "a strong painkiller does not mask the slowdowns.");

            relief.AddDose(both, "ephedrine", WolfmedPainReliefTier.Stimulant, 6f, window, 0f);
            relief.AddDose(stimOnly, "ephedrine", WolfmedPainReliefTier.Stimulant, 6f, window, 0f);
            Assert.Multiple(() =>
            {
                Assert.That(relief.GetTier(both), Is.EqualTo(WolfmedPainReliefTier.Stimulant));
                Assert.That(relief.MasksSlowdown(both), Is.True, "a stimulant on top of a strong painkiller brought the slowdowns back.");
                Assert.That(relief.MasksSlowdown(stimOnly), Is.False, "a stimulant alone masks the slowdowns.");
            });
        });
    }

    /// <summary>
    /// <c>WaitAsGhostTest</c> (OD8 (b)): a shut-down IPC is offered wait as a ghost after 90 s, and is flagged in
    /// distress; the ghost can return and the body is unchanged. Nothing is offered in a faint, at 33% blood or while
    /// hypoxic. When a route starts after the offer it is withdrawn and the waiting ghost is told; when the body
    /// wakes the ghost is offered the way back.
    /// </summary>
    [Test]
    public async Task WaitAsGhostTest()
    {
        await Pin();
        var map = await Pair.CreateTestMap();
        var s = new WolfmedScenario(SEntMan);
        var dormant = SEntMan.System<WolfmedDormantSystem>();
        var minds = SEntMan.System<SharedMindSystem>();
        await Server.WaitPost(() =>
        {
            s.SetAir(map.MapUid, true);
            s.KeepGrid(map.Grid);
        });

        var (ipc, mindId) = await Possess("MobIPC", map.GridCoords);
        EntityUid faint = default, bled = default;
        FixedPoint2 damage = default;

        await Server.WaitPost(() =>
        {
            faint = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            bled = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var slots = SEntMan.System<ItemSlotsSystem>();
            Assert.That(slots.TryGetSlot(ipc, "cell_slot", out var slot), Is.True);
            SEntMan.System<SharedContainerSystem>().Remove(slot!.Item!.Value, slot.ContainerSlot!);
        });
        await RunSeconds(2);

        await Server.WaitAssertion(() =>
        {
            Assert.That(Consc(ipc).Cause, Is.EqualTo(WolfmedCause.Shutdown), "the fixture did not shut down.");
            damage = SEntMan.GetComponent<DamageableComponent>(ipc).TotalDamage;

            SEntMan.GetComponent<WolfmedDormantComponent>(ipc).StableSeconds = 0f;
            dormant.Tick(ipc, 85f);
            Assert.That(dormant.IsOffered(ipc), Is.False, "offered before 90 s.");
            Assert.That(dormant.IsDistressed(ipc), Is.False);
            dormant.Tick(ipc, 6f);
            Assert.That(dormant.IsOffered(ipc), Is.True, "not offered after 90 s of shutdown.");
            Assert.That(dormant.IsDistressed(ipc), Is.True, "no distress flag after 90 s.");
            Assert.That(ActionIds(ipc), Does.Contain(WolfmedDormantSystem.WaitAction.Id));

            // A faint, and 33% blood: never offered.
            SetPain(faint, BodyPartType.Torso, 100);
            SetPain(faint, BodyPartType.Head, 100);
            s.SetBlood(bled, 0.33f);
            Assert.That(Consc(faint).Cause, Is.EqualTo(WolfmedCause.PainFaint));
            Assert.That(Consc(bled).Cause, Is.EqualTo(WolfmedCause.Blood));
            dormant.Tick(faint, 200f);
            dormant.Tick(bled, 200f);
            Assert.That(dormant.IsOffered(faint), Is.False, "offered during a faint.");
            Assert.That(dormant.IsOffered(bled), Is.False, "offered while the brain drains on 33% blood.");

            // Taken: a ghost that can come back, the body untouched.
            Assert.That(dormant.WaitAsGhost(ipc), Is.True);
            var ghost = ServerSession!.AttachedEntity;
            Assert.That(SEntMan.TryGetComponent(ghost, out GhostComponent? ghostComp), Is.True, "no ghost.");
            Assert.That(ghostComp!.CanReturnToBody, Is.True, "the waiting ghost cannot return.");
            Assert.That(SEntMan.GetComponent<MindComponent>(mindId).OwnedEntity, Is.EqualTo(ipc));
            Assert.That(SEntMan.GetComponent<DamageableComponent>(ipc).TotalDamage, Is.EqualTo(damage), "waiting changed the body.");
            Assert.That(SEntMan.System<MobStateSystem>().IsDead(ipc), Is.False);

            // Returning is always allowed.
            minds.UnVisit(mindId);
            Assert.That(ServerSession!.AttachedEntity, Is.EqualTo(ipc), "the ghost could not return.");

            // Taken again, then the cell back: the chassis wakes and the ghost is offered the way back.
            Assert.That(dormant.WaitAsGhost(ipc), Is.False, "the offer outlived being taken once.");
            SEntMan.GetComponent<WolfmedDormantComponent>(ipc).StableSeconds = 89f;
            dormant.Tick(ipc, 2f);
            Assert.That(dormant.WaitAsGhost(ipc), Is.True, "the offer did not come back once stable again.");
            Assert.That(SEntMan.System<WolfmedShutdownSystem>().RestoreCell(ipc), Is.True);
        });
        await RunSeconds(3);
        Assert.That(await ClientWindows<ReturnToBodyMenu>(), Is.GreaterThan(0), "the waiting ghost was not offered the way back.");

        // --- A stable unconscious human; a route starts after the offer: withdrawn, and the ghost is told. ---
        var (human, humanMind) = await Possess("MobHuman", map.GridCoords);
        await Server.WaitAssertion(() =>
        {
            s.Consciousness.SetExternalPressure(human, "test", 1f);
            Assert.That(Consc(human).State, Is.EqualTo(WolfmedConsciousness.Unconscious));
            dormant.Tick(human, 91f);
            Assert.That(dormant.IsOffered(human), Is.True, "a stable unconscious body was not offered.");
            Assert.That(dormant.WaitAsGhost(human), Is.True);

            SEntMan.System<DamageableSystem>().TryChangeDamage(human, Spec("Slash", 25), ignoreResistances: true,
                targetPart: TargetBodyPart.LeftArm);
            dormant.Tick(human, 1f);
            var comp = SEntMan.GetComponent<WolfmedDormantComponent>(human);
            TestContext.Out.WriteLine($"WaitAsGhostTest: the waiting ghost was told: {comp.LastLine}");
            Assert.Multiple(() =>
            {
                Assert.That(dormant.IsDistressed(human), Is.False, "the distress flag outlived the route starting.");
                Assert.That(dormant.IsOffered(human), Is.False);
                Assert.That(comp.LastLine, Does.Contain("getting worse"));
                Assert.That(comp.LastLine, Does.Contain(Loc.GetString("wolfmed-dormant-route-bleeding")));
            });

            minds.UnVisit(humanMind);
            Assert.That(ServerSession!.AttachedEntity, Is.EqualTo(human), "the told ghost could not return.");
        });

        // --- Hypoxic: never offered. Last, because it takes the map's air away. ---
        EntityUid hypoxic = default;
        await Server.WaitPost(() =>
        {
            s.SetAir(map.MapUid, false);
            hypoxic = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
        });
        await RunSeconds(15);
        await Server.WaitAssertion(() =>
        {
            s.Life.SetOxygenation(hypoxic, 0.42f);
            s.Life.Tick(hypoxic, 0.0001f);
            s.Consciousness.Refresh(hypoxic);
            Assert.That(Consc(hypoxic).State, Is.EqualTo(WolfmedConsciousness.Unconscious));
            Assert.That(s.Breathing.IsSuffocating(hypoxic), Is.True, "the airless map is not suffocating the patient.");
            dormant.Tick(hypoxic, 200f);
            Assert.That(dormant.IsOffered(hypoxic), Is.False, "offered while hypoxic.");
        });
    }

    /// <summary>
    /// <c>PlayDeadTest</c> (OD20, plan §5.3): Downed and still, a body playing dead reads "appears lifeless" from a
    /// distance (close up it is plainly awake); moving or speaking ends it; it is a Downed action only.
    /// </summary>
    [Test]
    public async Task PlayDeadTest()
    {
        await Pin();
        var map = await Pair.CreateTestMap();
        var crawl = SEntMan.System<WolfmedCrawlActionsSystem>();
        var inspection = SEntMan.System<WolfmedVisualInspectionSystem>();
        EntityUid body = default, medic = default;

        await Server.WaitPost(() =>
        {
            body = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            medic = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
        });
        await RunSeconds(2);

        await Server.WaitAssertion(() =>
        {
            Assert.That(ActionIds(body), Does.Not.Contain(WolfmedCrawlActionsSystem.PlayDeadAction.Id), "Play dead while Up.");
            Assert.That(ActionIds(body), Does.Contain(WolfmedCrawlActionsSystem.CheckYourselfAction.Id), "no Check yourself while Up.");
            Assert.That(crawl.StartPlayingDead(body), Is.False, "played dead standing up.");
            SetPain(body, BodyPartType.Torso, 129);
            Assert.That(Consc(body).State, Is.EqualTo(WolfmedConsciousness.Downed));
            Assert.That(ActionIds(body), Does.Contain(WolfmedCrawlActionsSystem.PlayDeadAction.Id), "no Play dead while Downed.");
        });
        await RunSeconds(3);

        string Distant() => string.Join(" ", inspection.GetLook(body, medic, false)!.Notes);
        string Close() => string.Join(" ", inspection.GetLook(body, medic, true)!.Notes);

        await Server.WaitAssertion(() =>
        {
            var lifeless = "appears lifeless";
            Assert.That(Distant(), Does.Not.Contain(lifeless));
            Assert.That(crawl.StartPlayingDead(body), Is.True);
            Assert.That(Distant(), Does.Contain(lifeless), "playing dead does not read as lifeless at range.");
            Assert.That(Close(), Does.Not.Contain(lifeless), "close up, playing dead fooled the medic.");

            var mover = SEntMan.GetComponent<InputMoverComponent>(body);
            var move = new MoveInputEvent((body, mover), mover.HeldMoveButtons);
            SEntMan.EventBus.RaiseLocalEvent(body, ref move);
            Assert.That(Distant(), Does.Not.Contain(lifeless), "moving did not end playing dead.");

            Assert.That(crawl.StartPlayingDead(body), Is.True);
            SEntMan.System<ChatSystem>().TrySendInGameICMessage(body, "psst", InGameICChatType.Speak, false);
            Assert.That(crawl.IsPlayingDead(body), Is.False, "speaking did not end playing dead.");

            // Check yourself: the condition and the self look, in words.
            var text = crawl.CheckYourself(body);
            TestContext.Out.WriteLine($"PlayDeadTest, check yourself: {text}");
            Assert.That(text, Does.Contain(Loc.GetString("wolfmed-cause-pain-symptom")));

            // Standing up takes Play dead away.
            Assert.That(crawl.StartPlayingDead(body), Is.True);
            SetPain(body, BodyPartType.Torso, 0);
            Consc(body).DownedUntil = TimeSpan.Zero;
            SEntMan.System<WolfmedConsciousnessSystem>().Refresh(body);
            Assert.That(Consc(body).State, Is.EqualTo(WolfmedConsciousness.Up));
            Assert.That(crawl.IsPlayingDead(body), Is.False, "standing up left the body playing dead.");
            Assert.That(ActionIds(body), Does.Not.Contain(WolfmedCrawlActionsSystem.PlayDeadAction.Id));
        });

    }

    /// <summary>
    /// <c>AdjacentDownedAidTest</c> (OD7 (c), plan §5.3): a Downed body may press gauze on a Downed neighbour within
    /// reach, and nothing else: not with nothing in hand, not on somebody standing, not from across the room. The
    /// gauze goes on at the Downed do-after pace and the bleed is dressed.
    /// </summary>
    [Test]
    public async Task AdjacentDownedAidTest()
    {
        await Pin();
        var map = await Pair.CreateTestMap();
        var downed = SEntMan.System<WolfmedDownedSystem>();
        var blocker = SEntMan.System<ActionBlockerSystem>();
        EntityUid helper = default, patient = default, standing = default, far = default, gauze = default;

        await Server.WaitPost(() =>
        {
            helper = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            patient = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            standing = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            far = SEntMan.SpawnEntity("MobHuman", map.GridCoords.Offset(new System.Numerics.Vector2(3f, 0f)));
            gauze = SEntMan.SpawnEntity("Gauze1", map.GridCoords);
            foreach (var body in new[] { helper, patient, far })
                SetPain(body, BodyPartType.Torso, 129);
            SEntMan.System<DamageableSystem>().TryChangeDamage(patient, Spec("Slash", 20), ignoreResistances: true,
                targetPart: TargetBodyPart.Torso);
        });
        await RunSeconds(4);

        await Server.WaitAssertion(() =>
        {
            Assert.That(Consc(helper).State, Is.EqualTo(WolfmedConsciousness.Downed));
            Assert.That(Consc(patient).State, Is.EqualTo(WolfmedConsciousness.Downed));
            Assert.That(blocker.CanInteract(helper, patient), Is.False, "a Downed body reached a neighbour empty-handed.");

            Assert.That(SEntMan.System<SharedHandsSystem>().TryPickupAnyHand(helper, gauze), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(downed.CanAidAdjacent(helper, patient), Is.True);
                Assert.That(blocker.CanInteract(helper, patient), Is.True, "gauze in hand did not reach a Downed neighbour.");
                Assert.That(blocker.CanInteract(helper, standing), Is.False, "a Downed body reached somebody standing.");
                Assert.That(blocker.CanInteract(helper, far), Is.False, "a Downed body reached across the room.");
            });

            Assert.That(SEntMan.System<SharedInteractionSystem>().InteractUsing(helper, gauze, patient,
                SEntMan.GetComponent<TransformComponent>(patient).Coordinates), Is.True, "the gauze was not used on the neighbour.");
        });
        await RunSeconds(6);

        await Server.WaitAssertion(() =>
        {
            var torso = new WolfmedScenario(SEntMan).Part(patient, BodyPartType.Torso);
            var treated = SEntMan.System<WoundSystem>().GetWounds((torso, SEntMan.GetComponent<WoundableComponent>(torso)))
                .Any(wound => SEntMan.TryGetComponent(wound, out WoundBleedingComponent? bleeding) &&
                              bleeding.Treatment == BleedingTreatment.Bandaged);
            Assert.That(treated, Is.True, "the neighbour's bleed was not dressed.");
        });
    }
}
