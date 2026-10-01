#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Destructible;
using Content.Server.Destructible.Thresholds.Triggers;
using Content.Server._WF.Wolfmed.Body;
using Content.Server._WF.Wolfmed.Consciousness;
using Content.Server._WF.Wolfmed.Explosion;
using Content.Server._WF.Wolfmed.Wounds;
using Content.Shared._Onyx.Body.Systems;
using Content.Shared._Onyx.Targeting;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Body.Organ;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Wolfmed.Body;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.Consciousness;
using Content.Shared._WF.Wolfmed.Hud;
using Content.Shared._WF.Wolfmed.Life;
using Content.Shared._WF.Wolfmed.Reagents;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs.Systems;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Random;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Wolfmed.Scenarios;

/// <summary>
/// M3 (plan §8, §3.6, P19, P23, P27, OD15): organ damage follows the size and place of a hit, not a roll; damaged
/// lungs, brain and core have named consequences; a heavy head blow knocks out for seconds; a beaten or burned limb
/// comes off with a stump before it can be destroyed; pressure, blasts and shocks land where the plan says.
/// </summary>
/// <remarks>Times are asserted as order plus a ±20% band, with every CVar the arithmetic reads pinned.</remarks>
[TestFixture]
[TestOf(typeof(WolfmedOrganThresholdSystem))]
public sealed class WolfmedConsequencesTest : GameTest
{
    private const float Band = 0.2f;
    private const float Scale = 3.4f;
    private const float KnockoutSeconds = 5f;

    /// <summary>Measurements taken on the server thread, written out on the test thread where NUnit can see them.</summary>
    private readonly List<string> _log = new();

    [TearDown]
    public void WriteMeasurements()
    {
        foreach (var line in _log)
            TestContext.Out.WriteLine(line);

        _log.Clear();
    }

    private async Task Pin()
    {
        await OverrideCVar(Side.Server, WolfmedCVars.Consciousness, true);
        await OverrideCVar(Side.Server, WolfmedCVars.ConsciousnessPainDown, 0.95f);
        await OverrideCVar(Side.Server, WolfmedCVars.ConsciousnessPainOut, 1.4f);
        await OverrideCVar(Side.Server, WolfmedCVars.ConsciousnessBloodDown, 0.5f);
        await OverrideCVar(Side.Server, WolfmedCVars.ConsciousnessBloodOut, 0.35f);
        await OverrideCVar(Side.Server, WolfmedCVars.ConsciousnessHysteresis, 0.1f);
        await OverrideCVar(Side.Server, WolfmedCVars.BrainAirlossSeconds, 180f);
        await OverrideCVar(Side.Server, WolfmedCVars.BrainPressureStart, 0.75f);
        await OverrideCVar(Side.Server, WolfmedCVars.BrainPressureOut, 0.45f);
        await OverrideCVar(Side.Server, WolfmedCVars.OrganDamageScale, Scale);
        await OverrideCVar(Side.Server, WolfmedCVars.OrganHitCap, 3f); // playtest 4: the shipped cap
        await OverrideCVar(Side.Server, WolfmedCVars.LungDamageFactor, 1f);
        await OverrideCVar(Side.Server, WolfmedCVars.ConsciousnessBrainDown, 0.25f);
        await OverrideCVar(Side.Server, WolfmedCVars.ConsciousnessCoreDown, 0.25f);
        await OverrideCVar(Side.Server, WolfmedCVars.InjuryDownPressure, 0.75f);
        await OverrideCVar(Side.Server, WolfmedCVars.HeadKnockoutSeconds, KnockoutSeconds);
        await OverrideCVar(Side.Server, WolfmedCVars.HeadKnockoutBlunt, 30f);
        await OverrideCVar(Side.Server, WolfmedCVars.ElectricHeartFactor, 0.2f);
        await OverrideCVar(Side.Server, WolfmedCVars.ElectricHeartFrom, 15f);
        await OverrideCVar(Side.Server, WolfmedCVars.BlastDismemberHead, false);
    }

    private WoundDamageRoutingSystem Routing => SEntMan.System<WoundDamageRoutingSystem>();
    private OrganHealthSystem OrganHealth => SEntMan.System<OrganHealthSystem>();

    private static DamageSpecifier Spec(string type, float amount) => WolfmedScenario.Spec(type, amount);

    private WolfmedConsciousnessComponent Consc(EntityUid body) =>
        SEntMan.GetComponent<WolfmedConsciousnessComponent>(body);

    /// <summary>The organ in a body slot, with its health.</summary>
    private Entity<WolfmedOrganComponent> Organ(EntityUid body, string slot)
    {
        var (id, _) = SEntMan.System<SharedBodySystem>().GetBodyOrgans(body)
            .First(organ => organ.Component.SlotId == slot);
        return (id, SEntMan.GetComponent<WolfmedOrganComponent>(id));
    }

    private float Hp(EntityUid body, string slot) =>
        SEntMan.System<SharedBodySystem>().GetBodyOrgans(body)
            .Where(organ => organ.Component.SlotId == slot)
            .Select(organ => SEntMan.GetComponent<WolfmedOrganComponent>(organ.Id).Health.Float())
            .DefaultIfEmpty(0f)
            .First();

    private void SetHealthFraction(EntityUid body, string slot, float fraction)
    {
        var organ = Organ(body, slot);
        OrganHealth.SetHealth(organ, organ.Comp.MaxHealth * fraction);
    }

    /// <summary>The whole-body notes of a close examination: breathing, pulse, pupils.</summary>
    private string ExamineNotes(EntityUid examined, EntityUid examiner) =>
        string.Join(" ", SEntMan.System<Content.Shared._WF.Wolfmed.Examine.WolfmedVisualInspectionSystem>()
            .GetLook(examined, examiner, true)?.Notes ?? new List<string>());

    /// <summary>
    /// Plan §8 calibration: ten identical rifle rounds (Piercing 14) to the chest do the same organ damage on every
    /// body; the lungs are impaired within 3-5 hits and the heart fails within 12-16 at the shipped scale.
    /// </summary>
    [Test]
    public async Task OrganCalibrationTest()
    {
        await Pin();
        var map = await Pair.CreateTestMap();
        var s = new WolfmedScenario(SEntMan);
        string[] slots = ["lungs", "heart", "liver", "stomach", "kidneys"];

        await Server.WaitAssertion(() =>
        {
            var shooter = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var a = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var b = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var torsoA = s.Part(a, BodyPartType.Torso);
            var torsoB = s.Part(b, BodyPartType.Torso);

            int? lungsImpaired = null;
            int? heartFailed = null, lungsFailed = null;
            var trace = new List<string>();
            for (var hit = 1; hit <= 32 && heartFailed == null; hit++)
            {
                if (!Routing.TryApplyPartDamage(a, torsoA, Spec("Piercing", 14), shooter))
                {
                    trace.Add($"{hit}: routing refused the hit, state {s.State(a)}");
                    break;
                }
                Routing.TryApplyPartDamage(b, torsoB, Spec("Piercing", 14), shooter);

                var hp = slots.Select(slot => Hp(a, slot)).ToArray();
                if (hit <= 10)
                {
                    var other = slots.Select(slot => Hp(b, slot)).ToArray();
                    Assert.That(other, Is.EqualTo(hp), $"hit {hit}: two identical bodies took different organ damage.");
                }

                trace.Add($"{hit}: " + string.Join(" ", slots.Zip(hp, (slot, value) => $"{slot} {value:0.00}")));
                if (lungsImpaired == null && hp[0] < 7.5f)
                {
                    lungsImpaired = hit;
                    Assert.That(s.Analyzer(a), Does.Contain("Lungs impaired"), // playtest 3: no effect in brackets
                        "the analyzer does not name the impaired lungs.");
                }

                if (lungsFailed == null && hp[0] <= 0f)
                    lungsFailed = hit;
                if (heartFailed == null && hp[1] <= 0f)
                    heartFailed = hit;
            }

            _log.Add($"OrganCalibration at scale {Scale}: lungs impaired on hit {lungsImpaired}, failed on hit {lungsFailed}, " +
                                      $"heart failed on hit {heartFailed}. {string.Join(" | ", trace)}");
            Assert.Multiple(() =>
            {
                // Playtest 4 (organs 25, cap 3): lungs impaired on hit 10 and failed on hit 14; the heart was at 4.4 on hit 18.
                Assert.That(lungsImpaired, Is.InRange(8, 12), "the lungs are not impaired within 8-12 rifle rounds.");
                Assert.That(lungsFailed, Is.InRange(12, 16), "the lungs do not fail within 12-16 rifle rounds.");
                // The routing stops taking hits on this torso around hit 19 with the heart still beating: rifle fire
                // takes the lungs, never the heart.
                Assert.That(heartFailed, Is.Null, "rifle rounds failed the heart before the lungs finished the body.");
            });

            // Under the line nothing reaches the organs: a Piercing 10 round is exactly the torso's line.
            var clean = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            Routing.TryApplyPartDamage(clean, s.Part(clean, BodyPartType.Torso), Spec("Piercing", 10), shooter);
            Assert.That(slots.All(slot => Hp(clean, slot) >= 25f), Is.True, "a hit at the reach line reached an organ.");

            // Same shot, same organ damage, saturated or not (plan §6.2): a torso already holding its 250 hands the
            // organ step the whole hit, exactly as a fresh one does. The saturating hits sit on the line.
            var fresh = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var saturated = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var freshTorso = s.Part(fresh, BodyPartType.Torso);
            var saturatedTorso = s.Part(saturated, BodyPartType.Torso);
            for (var i = 0; i < 26; i++)
                Routing.TryApplyPartDamage(saturated, saturatedTorso, Spec("Piercing", 10), shooter);

            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.GetComponent<DamageableComponent>(saturatedTorso).TotalDamage.Float(),
                    Is.EqualTo(250f).Within(0.01f), "the torso is not saturated.");
                Assert.That(slots.All(slot => Hp(saturated, slot) >= 25f), Is.True, "saturating reached an organ.");
            });

            for (var shot = 1; shot <= 5; shot++)
            {
                Routing.TryApplyPartDamage(fresh, freshTorso, Spec("Piercing", 14), shooter);
                Routing.TryApplyPartDamage(saturated, saturatedTorso, Spec("Piercing", 14), shooter);
                Assert.That(slots.Select(slot => Hp(saturated, slot)).ToArray(),
                    Is.EqualTo(slots.Select(slot => Hp(fresh, slot)).ToArray()),
                    $"shot {shot}: the saturated torso's organs took different damage.");
            }
        });
    }

    /// <summary>
    /// Plan §3.3: lungs at 30% raise the breathing level by (0.5 - 0.3) / 0.5; in station air the patient is short
    /// of breath, then Downed, then Unconscious from hypoxia with the lungs named as the source.
    /// </summary>
    [Test]
    public async Task LungRouteTest()
    {
        await Pin();
        var map = await Pair.CreateTestMap();
        var s = new WolfmedScenario(SEntMan);

        await Server.WaitAssertion(() =>
        {
            s.KeepGrid(map.Grid);
            s.SetAir(map.MapUid, true);
            var body = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            SetHealthFraction(body, "lungs", 0.3f);
            s.Advance(body, 1);

            Assert.Multiple(() =>
            {
                Assert.That(s.Life.LungDamageLevel(body), Is.EqualTo(0.4f).Within(0.01f));
                Assert.That(s.Breathing.Assess(body).Breathing, Is.EqualTo(WolfmedBreathing.Laboured));
                Assert.That(Consc(body).Breathing, Is.EqualTo(WolfmedBreathing.Laboured), "examine does not see it.");
                Assert.That(ExamineNotes(body, body), Does.Contain("short of breath"));
                Assert.That(s.Analyzer(body), Does.Contain("Breathing laboured")); // playtest 3
            });

            // Drain 0.4 / 180 per second: Downed at oxygenation 0.54 (~207 s), Unconscious at 0.45 (~248 s).
            var downed = s.Advance(body, 400, _ => Consc(body).State != WolfmedConsciousness.Up);
            Assert.Multiple(() =>
            {
                Assert.That(Consc(body).State, Is.EqualTo(WolfmedConsciousness.Downed));
                Assert.That(Consc(body).Cause, Is.EqualTo(WolfmedCause.Hypoxia));
                Assert.That(Consc(body).CauseSource, Is.EqualTo(WolfmedCauseSource.Lungs));
                Assert.That(downed, Is.EqualTo(207f).Within(207f * Band), "short of breath at the wrong time.");
            });

            var out1 = s.Advance(body, 400, _ => Consc(body).State == WolfmedConsciousness.Unconscious);
            Assert.Multiple(() =>
            {
                Assert.That(Consc(body).State, Is.EqualTo(WolfmedConsciousness.Unconscious));
                Assert.That(Consc(body).Cause, Is.EqualTo(WolfmedCause.Hypoxia));
                Assert.That(Consc(body).CauseSource, Is.EqualTo(WolfmedCauseSource.Lungs));
                Assert.That(downed + out1, Is.EqualTo(248f).Within(248f * Band), "hypoxic at the wrong time.");
            });
            _log.Add($"LungRoute: lungs 30%, Downed at {downed} s, Unconscious at {downed + out1} s.");
        });
    }

    /// <summary>
    /// Review fix (M2 with M3): an arrest from damaged lungs alone, in clear air with no sedation, is named "oxygen".
    /// After the shock the "After a restart" line says the cause is still present while the lungs stay impaired, and
    /// the routes name the lungs; once the lungs are healed the cause reads gone.
    /// </summary>
    [Test]
    public async Task LungArrestRestartMemoryTest()
    {
        await Pin();
        await OverrideCVar(Side.Server, WolfmedCVars.ArrestOxygenation, 0.15f);
        await OverrideCVar(Side.Server, WolfmedCVars.ArrestBlood, 0.30f);
        await OverrideCVar(Side.Server, WolfmedCVars.DefibBlood, 0.25f);
        await OverrideCVar(Side.Server, WolfmedCVars.ArrestCauseMemorySeconds, 300f);
        var map = await Pair.CreateTestMap();
        var s = new WolfmedScenario(SEntMan);

        await Server.WaitAssertion(() =>
        {
            s.KeepGrid(map.Grid);
            s.SetAir(map.MapUid, true);
            var body = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            SetHealthFraction(body, "lungs", 0.1f);
            s.Advance(body, 1);

            // The premise: the damaged lungs are the only breath input.
            Assert.Multiple(() =>
            {
                Assert.That(Organ(body, "lungs").Comp.Band, Is.EqualTo(WolfmedOrganBand.Impaired));
                Assert.That(s.Life.LungDamageLevel(body), Is.EqualTo(0.8f).Within(0.01f));
                Assert.That(s.Life.BreathingLevel(body), Is.Zero, "the body is suffocating, not only short of breath.");
                Assert.That(SEntMan.System<WolfmedPainReliefSystem>().GetRespiratoryDepression(body), Is.Zero);
            });

            // Drain 0.8 / 180 per second: the arrest line 0.15 at about 191 s.
            var arrested = s.Advance(body, 400, _ => s.Life.InArrest(body));
            Assert.Multiple(() =>
            {
                Assert.That(s.Life.InArrest(body), Is.True, "the damaged lungs never stopped the heart.");
                Assert.That(SEntMan.GetComponent<WolfmedCardiacArrestComponent>(body).Cause, Is.EqualTo("oxygen"));
                Assert.That(arrested, Is.EqualTo(191f).Within(191f * Band), "arrested at the wrong time.");
            });

            Assert.That(s.Shock(body, out var line), Is.True, line);
            var report = s.Report(body);
            var restart = WolfmedVitalsText.RestartLine(report);
            _log.Add($"LungArrestRestartMemory: arrest at {arrested} s; {restart} | {WolfmedVitalsText.DoFirstLine(report)}");
            Assert.Multiple(() =>
            {
                Assert.That(s.Life.GetRestartMemory(body), Is.EqualTo((WolfmedCauseSource.ArrestOxygen, true)));
                Assert.That(report.RestartCause, Is.EqualTo(WolfmedCauseSource.ArrestOxygen));
                Assert.That(report.RestartPresent, Is.True, "the lungs are still damaged but the cause reads gone.");
                Assert.That(restart, Does.Contain($"Still present: {Loc.GetString("wolfmed-vitals-yes")}"));
                Assert.That(report.Routes & WolfmedRoutes.Lungs, Is.EqualTo(WolfmedRoutes.Lungs),
                    "the routes do not name the damaged lungs.");
            });

            SetHealthFraction(body, "lungs", 1f);
            report = s.Report(body);
            Assert.Multiple(() =>
            {
                Assert.That(s.Life.LungDamageLevel(body), Is.Zero);
                Assert.That(s.Life.GetRestartMemory(body), Is.EqualTo((WolfmedCauseSource.ArrestOxygen, false)));
                Assert.That(report.RestartPresent, Is.False, "healed lungs still read as the cause.");
                Assert.That(WolfmedVitalsText.RestartLine(report),
                    Does.Contain($"Still present: {Loc.GetString("wolfmed-vitals-no")}"));
                Assert.That(report.Routes & WolfmedRoutes.Lungs, Is.EqualTo(WolfmedRoutes.None));
            });
        });
    }

    /// <summary>
    /// Plan §3.6: a brain at 20% holds the patient Downed (cause Brain), breathing, never Unconscious; at 30% it does
    /// not. A Blunt-30 head hit knocks out for at most 5 s with cause HeadBlow, and a second blow does not lengthen it.
    /// </summary>
    [Test]
    public async Task BrainInjuryInputTest()
    {
        await Pin();
        var map = await Pair.CreateTestMap();
        var s = new WolfmedScenario(SEntMan);
        var alerts = SEntMan.System<WolfmedConditionAlertSystem>();
        EntityUid attacker = default, struck = default, glancing = default;

        await Server.WaitAssertion(() =>
        {
            s.SetAir(map.MapUid, true);
            var injured = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var bruised = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            SetHealthFraction(injured, "brain", 0.2f);
            SetHealthFraction(bruised, "brain", 0.3f);
            s.Advance(injured, 2);
            s.Advance(bruised, 2);

            Assert.Multiple(() =>
            {
                Assert.That(Consc(injured).State, Is.EqualTo(WolfmedConsciousness.Downed));
                Assert.That(Consc(injured).Cause, Is.EqualTo(WolfmedCause.Brain));
                Assert.That(Consc(injured).Breathing, Is.EqualTo(WolfmedBreathing.Normal));
                Assert.That(alerts.GetShownHealthAlert(injured)?.Id, Is.EqualTo("WFWolfmedDownedBrain"));
                Assert.That(alerts.GetTitle(injured), Is.EqualTo("Downed: head injury"));
                Assert.That(s.AnalyzerLines(injured)[0], Is.EqualTo("DOWNED: head injury"));
                Assert.That(ExamineNotes(injured, bruised), Does.Contain("unequal pupils"));
                Assert.That(Consc(bruised).State, Is.EqualTo(WolfmedConsciousness.Up), "30% brain holds a patient down.");
            });

            // Never out, however long: nothing heals the brain and nothing makes it worse on its own.
            s.Advance(injured, 120);
            Assert.That(Consc(injured).State, Is.EqualTo(WolfmedConsciousness.Downed));

            attacker = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            struck = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            glancing = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
        });
        await RunSeconds(1);

        float start = 0;
        await Server.WaitAssertion(() =>
        {
            start = (float) SGameTiming.CurTime.TotalSeconds;
            Routing.TryApplyPartDamage(struck, s.Part(struck, BodyPartType.Head), Spec("Blunt", 30), attacker);
            Routing.TryApplyPartDamage(glancing, s.Part(glancing, BodyPartType.Head), Spec("Blunt", 29), attacker);

            Assert.Multiple(() =>
            {
                Assert.That(Consc(struck).State, Is.EqualTo(WolfmedConsciousness.Unconscious));
                Assert.That(Consc(struck).Cause, Is.EqualTo(WolfmedCause.HeadBlow));
                Assert.That(Consc(struck).Breathing, Is.Not.EqualTo(WolfmedBreathing.None));
                Assert.That(s.Consciousness.InFaint(struck), Is.True, "the pod would treat the knockout as an emergency.");
                Assert.That(alerts.GetShownHealthAlert(struck)?.Id, Is.EqualTo("WFWolfmedFaintHeadBlow"));
                Assert.That(s.Consciousness.GetFaintSecondsLeft(struck), Is.LessThanOrEqualTo((int) KnockoutSeconds));
                Assert.That(s.AnalyzerLines(struck)[0], Does.StartWith("FAINTED: head blow"));
                Assert.That(Consc(glancing).State, Is.Not.EqualTo(WolfmedConsciousness.Unconscious),
                    "Blunt 29 knocked a patient out.");
            });
        });

        await RunSeconds(2);
        await Server.WaitAssertion(() =>
        {
            // A second heavy blow inside the knockout never lengthens it.
            Routing.TryApplyPartDamage(struck, s.Part(struck, BodyPartType.Head), Spec("Blunt", 30), attacker);
            Assert.That(Consc(struck).Cause, Is.EqualTo(WolfmedCause.HeadBlow));
        });

        float? woke = null;
        for (var waited = 0f; waited <= 10f && woke == null; waited += 0.5f)
        {
            await RunSeconds(0.5f);
            await Server.WaitPost(() =>
            {
                if (Consc(struck).State != WolfmedConsciousness.Unconscious)
                    woke = (float) SGameTiming.CurTime.TotalSeconds - start;
            });
        }

        _log.Add($"HeadBlow: out for {woke} s after a Blunt-30 blow and a second one 2 s in.");
        Assert.That(woke, Is.Not.Null, "the head blow never ended.");
        Assert.That(woke!.Value, Is.LessThanOrEqualTo(KnockoutSeconds + 1f), "the knockout ran past its 5 s.");
    }

    /// <summary>
    /// Plan §3.6: an IPC core at 20% is Downed with cause Core and is not shut down; at 30% it is Up; the HUD says
    /// CORE INTEGRITY. And a measurement: rifle rounds to the chassis torso until the core Downs it and fails.
    /// </summary>
    [Test]
    public async Task IpcCoreInputTest()
    {
        await Pin();
        var map = await Pair.CreateTestMap();
        var s = new WolfmedScenario(SEntMan);
        EntityUid damaged = default, scratched = default;

        await Server.WaitAssertion(() =>
        {
            s.SetAir(map.MapUid, true);
            damaged = SEntMan.SpawnEntity("MobIPC", map.GridCoords);
            scratched = SEntMan.SpawnEntity("MobIPC", map.GridCoords);
            SetHealthFraction(damaged, "posbrain", 0.2f);
            SetHealthFraction(scratched, "posbrain", 0.3f);
            s.Advance(damaged, 2);
            s.Advance(scratched, 2);

            Assert.Multiple(() =>
            {
                Assert.That(Consc(damaged).State, Is.EqualTo(WolfmedConsciousness.Downed));
                Assert.That(Consc(damaged).Cause, Is.EqualTo(WolfmedCause.Core));
                Assert.That(SEntMan.System<Content.Server._WF.Wolfmed.Life.WolfmedShutdownSystem>().IsShutDown(damaged), Is.False);
                Assert.That(Consc(scratched).State, Is.EqualTo(WolfmedConsciousness.Up));
            });
        });
        await RunSeconds(1);

        await Server.WaitAssertion(() =>
        {
            var hud = SEntMan.GetComponent<WolfmedSyntheticHudComponent>(damaged);
            Assert.Multiple(() =>
            {
                Assert.That(Consc(damaged).State, Is.EqualTo(WolfmedConsciousness.Downed), "the core stopped holding it.");
                Assert.That(hud.CauseLine, Is.EqualTo("wolfmed-synthetic-cause-core"));
                Assert.That(Loc.GetString(hud.CauseLine), Does.Contain("CORE INTEGRITY"));
            });

            // Penetrating torso hits reach the core (OD10). Measured for the record, asserted only for order.
            var shooter = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var shot = SEntMan.SpawnEntity("MobIPC", map.GridCoords);
            var torso = s.Part(shot, BodyPartType.Torso);
            int? down = null, failed = null;
            for (var hit = 1; hit <= 20 && failed == null; hit++)
            {
                Routing.TryApplyPartDamage(shot, torso, Spec("Piercing", 14), shooter);
                var core = Hp(shot, "posbrain");
                if (down == null && core > 0f && core < 3.75f)
                    down = hit;
                if (core <= 0f)
                    failed = hit;
            }

            _log.Add($"IpcCore: rifle rounds to the torso, core under 25% on hit {down}, " +
                                      $"core failed on hit {failed}; pump {Hp(shot, "pump"):0.00} HP left.");
            Assert.That(failed, Is.Not.Null, "twenty rifle rounds to the chassis never reached the core.");
        });
    }

    /// <summary>OD15: Shock 35 takes 0.2 × (35 - 15) = 4 off the heart every time; Shock 15 takes nothing.</summary>
    [Test]
    public async Task ElectricalHeartBandTest()
    {
        await Pin();
        var map = await Pair.CreateTestMap();
        var s = new WolfmedScenario(SEntMan);

        await Server.WaitAssertion(() =>
        {
            var source = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            for (var run = 0; run < 3; run++)
            {
                var body = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
                Routing.TryApplyPartDamage(body, s.Part(body, BodyPartType.Arm, BodyPartSymmetry.Left),
                    Spec("Shock", 35), source);
                Assert.That(Hp(body, "heart"), Is.EqualTo(21f).Within(0.01f), $"run {run}: the heart band is not 4.");
            }

            var mild = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            Routing.TryApplyPartDamage(mild, s.Part(mild, BodyPartType.Arm, BodyPartSymmetry.Left),
                Spec("Shock", 15), source);
            Assert.That(Hp(mild, "heart"), Is.EqualTo(25f), "Shock 15 reached the heart.");
        });
    }

    /// <summary>OD15: every Blunt hit of 40 or more bleeds inside; 35 never does.</summary>
    [Test]
    public async Task CrushInternalBleedBandTest()
    {
        await Pin();
        var map = await Pair.CreateTestMap();
        var s = new WolfmedScenario(SEntMan);

        await Server.WaitAssertion(() =>
        {
            var attacker = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var wounds = SEntMan.System<WoundSystem>();
            bool Bleeds(EntityUid part) => wounds.GetWounds(part)
                .Any(wound => wound.Comp.Prototype == new ProtoId<WoundPrototype>("InternalBleedingWound"));

            for (var run = 0; run < 5; run++)
            {
                var heavy = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
                var light = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
                var heavyLeg = s.Part(heavy, BodyPartType.Leg, BodyPartSymmetry.Left);
                var lightLeg = s.Part(light, BodyPartType.Leg, BodyPartSymmetry.Left);
                Routing.TryApplyPartDamage(heavy, heavyLeg, Spec("Blunt", 40), attacker);
                Routing.TryApplyPartDamage(light, lightLeg, Spec("Blunt", 35), attacker);

                Assert.Multiple(() =>
                {
                    Assert.That(Bleeds(heavyLeg), Is.True, $"run {run}: a Blunt 40 blow did not bleed inside.");
                    Assert.That(Bleeds(lightLeg), Is.False, $"run {run}: a Blunt 35 blow bled inside.");
                });
            }
        });
    }

    /// <summary>
    /// P19: a club (Blunt 15) and a laser (Heat 16) take a limb off at its sever threshold, leaving a bleeding
    /// stump, well before the stored damage could reach the limb's destruction trigger.
    /// </summary>
    [Test]
    public async Task StumpTest()
    {
        await Pin();
        var map = await Pair.CreateTestMap();
        var s = new WolfmedScenario(SEntMan);

        await Server.WaitAssertion(() =>
        {
            var attacker = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var body = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var graph = SEntMan.System<SharedBodySystem>();
            var wounds = SEntMan.System<WoundSystem>();

            var cases = new (BodyPartType Type, BodyPartSymmetry Side, string Damage, float Hit)[]
            {
                (BodyPartType.Arm, BodyPartSymmetry.Left, "Blunt", 15),
                (BodyPartType.Leg, BodyPartSymmetry.Right, "Blunt", 15),
                (BodyPartType.Hand, BodyPartSymmetry.Right, "Blunt", 15),
                (BodyPartType.Foot, BodyPartSymmetry.Left, "Blunt", 15),
                (BodyPartType.Arm, BodyPartSymmetry.Right, "Heat", 16),
            };

            foreach (var (type, side, damage, hit) in cases)
            {
                var limb = s.Part(body, type, side);
                var parent = graph.GetParentPartOrNull(limb)!.Value;
                var destroyAt = DestructionTrigger(limb, damage);
                var severAt = SEntMan.GetComponent<WolfmedBodyPartComponent>(limb).AmputationThresholds[damage].Float();
                var stumpsBefore = Stumps(wounds, parent).Count;
                var stored = 0f;
                var hits = 0;
                while (graph.BodyHasChild(body, limb) && hits < 60)
                {
                    stored = StoredOf(limb, damage) + hit;
                    Routing.TryApplyPartDamage(body, limb, Spec(damage, hit), attacker);
                    hits++;
                }

                var stumps = Stumps(wounds, parent);
                _log.Add($"Stump: {side} {type} came off after {hits} {damage} {hit} hits at {stored} " +
                                          $"stored; sever threshold {severAt}, destruction {destroyAt}.");
                Assert.Multiple(() =>
                {
                    Assert.That(destroyAt, Is.GreaterThan(severAt), $"{type} {damage}: destroyed at or before severing.");
                    Assert.That(graph.BodyHasChild(body, limb), Is.False, $"{side} {type}: {hits} hits never took it off.");
                    Assert.That(SEntMan.Deleted(limb), Is.False, $"{side} {type} was destroyed, not severed.");
                    Assert.That(stored, Is.LessThan(destroyAt), $"{side} {type} came off past its destruction trigger.");
                    Assert.That(stumps, Has.Count.GreaterThan(stumpsBefore), $"{side} {type} left no stump.");
                    Assert.That(stumps.Any(stump =>
                            SEntMan.TryGetComponent(stump, out WoundBleedingComponent? bleeding) && bleeding.CurrentRate > 0f),
                        Is.True, $"{side} {type}: the stump does not bleed.");
                });
            }
        });
    }

    private List<EntityUid> Stumps(WoundSystem wounds, EntityUid part) =>
        wounds.GetWounds(part)
            .Where(wound => wound.Comp.Prototype == new ProtoId<WoundPrototype>("DismembermentWound"))
            .Select(wound => wound.Owner)
            .ToList();

    private float StoredOf(EntityUid part, string type) =>
        SEntMan.GetComponent<DamageableComponent>(part).Damage.DamageDict.GetValueOrDefault(type).Float();

    private float DestructionTrigger(EntityUid part, string type)
    {
        foreach (var threshold in SEntMan.GetComponent<DestructibleComponent>(part).Thresholds)
        {
            if (threshold.Trigger is DamageTypeTrigger typed && typed.DamageType == type)
                return typed.Damage;
        }

        return float.MaxValue;
    }

    /// <summary>
    /// P23: barotrauma on a body whose doll selects the left arm spreads across the parts by weight, not all onto the
    /// left arm, and goes through the ambient ceiling like any harm nobody dealt.
    /// </summary>
    [Test]
    public async Task BarotraumaPartTest()
    {
        await Pin();
        var map = await Pair.CreateTestMap();
        var s = new WolfmedScenario(SEntMan);
        var hits = new Dictionary<EntityUid, int>();
        var hitSystem = SEntMan.System<WolfmedPartHitSystem>();
        EntityUid body = default;

        await Server.WaitPost(() =>
        {
            body = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            SEntMan.GetComponent<TargetingComponent>(body).Target = TargetBodyPart.LeftArm;
        });

        hitSystem.Observer = (part, hit) =>
        {
            if (hit.Body == body && hit.Origin == null)
                hits[part] = hits.GetValueOrDefault(part) + 1;
        };

        try
        {
            // The test map has no atmosphere: vacuum, the low-pressure branch.
            await Server.WaitAssertion(() =>
            {
                var barotrauma = SEntMan.System<BarotraumaSystem>();
                hits.Clear();
                // Pinned: the 84 hits are rolled by weight, and the 4:2 torso-to-arm split came out 10:10 one run in
                // four on the pool's own seed. The count does not grow with more ticks (the harm is capped).
                Server.ResolveDependency<IRobustRandom>().SetSeed(20260927);
                for (var tick = 0; tick < 100; tick++)
                    barotrauma.Update(1f);

                var total = hits.Values.Sum();
                var leftArm = hits.GetValueOrDefault(s.Part(body, BodyPartType.Arm, BodyPartSymmetry.Left));
                var torso = hits.GetValueOrDefault(s.Part(body, BodyPartType.Torso));
                _log.Add($"Barotrauma: {total} hits over {hits.Count} parts; left arm {leftArm}, torso {torso}.");
                Assert.Multiple(() =>
                {
                    Assert.That(total, Is.GreaterThan(40), "barotrauma barely landed.");
                    Assert.That(hits.Count, Is.GreaterThanOrEqualTo(6), "barotrauma landed on too few parts.");
                    Assert.That(leftArm, Is.LessThan(total / 2), "barotrauma still follows the doll.");
                    Assert.That(torso, Is.GreaterThan(leftArm), "the torso (weight 4) took fewer hits than an arm (2).");
                });
            });
        }
        finally
        {
            hitSystem.Observer = null;
        }
    }

    /// <summary>P27: with wolfmed.blast_dismember_head off a blast never takes the head, even one already severable.</summary>
    [Test]
    public async Task BlastHeadTest()
    {
        await Pin();
        var map = await Pair.CreateTestMap();
        var s = new WolfmedScenario(SEntMan);
        var explosion = SEntMan.System<WolfmedExplosionSystem>();
        EntityUid attacker = default, control = default;

        await Server.WaitAssertion(() =>
        {
            var graph = SEntMan.System<SharedBodySystem>();
            attacker = SEntMan.SpawnEntity("MobHuman", map.GridCoords);

            // Slash 200 is the head's sever threshold: the next finishing hit would take it off.
            var masked = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var head = s.Part(masked, BodyPartType.Head);
            Routing.TryApplyPartDamage(masked, head, Spec("Slash", 200), attacker);
            Assert.That(SEntMan.GetComponent<WoundableComponent>(head).Severable, Is.True);

            // The whole blast on the head: Onyx's explosion roll saturates at progress 2.
            Routing.TryRouteDistributedDamage(masked, Spec("Piercing", 500), TargetBodyPart.Head,
                DamageDistribution.SplitEvenly, variation: 0f, isExplosion: true);
            Assert.That(graph.BodyHasChild(masked, head), Is.True, "a masked blast took the head.");

            // Ordinary blasts through the wrapper, every limb roll forced to land.
            var blasted = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var blastedHead = s.Part(blasted, BodyPartType.Head);
            Routing.TryApplyPartDamage(blasted, blastedHead, Spec("Slash", 200), attacker);
            explosion.ForcedRoll = 0f;
            try
            {
                for (var i = 0; i < 5; i++)
                    explosion.TryApplyExplosionDamage(blasted, Spec("Piercing", 300));
            }
            finally
            {
                explosion.ForcedRoll = null;
            }

            Assert.That(graph.BodyHasChild(blasted, blastedHead), Is.True, "a blast took the head.");

            control = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            Routing.TryApplyPartDamage(control, s.Part(control, BodyPartType.Head), Spec("Slash", 200), attacker);
        });

        // The veto is what holds: with the CVar on, the same masked blast takes the head.
        await OverrideCVar(Side.Server, WolfmedCVars.BlastDismemberHead, true);
        await Server.WaitAssertion(() =>
        {
            var head = s.Part(control, BodyPartType.Head);
            Routing.TryRouteDistributedDamage(control, Spec("Piercing", 500), TargetBodyPart.Head,
                DamageDistribution.SplitEvenly, variation: 0f, isExplosion: true);
            Assert.That(SEntMan.System<SharedBodySystem>().BodyHasChild(control, head), Is.False,
                "with the CVar on the blast should take a severable head; the test proves nothing.");
        });
    }

    /// <summary>
    /// Plan §8 graded bands: an impaired heart (under half health) regenerates blood at half the rate, and the
    /// analyzer names it.
    /// </summary>
    [Test]
    public async Task HeartBandTest()
    {
        await Pin();
        var map = await Pair.CreateTestMap();
        var s = new WolfmedScenario(SEntMan);
        EntityUid healthy = default, weak = default;

        await Server.WaitAssertion(() =>
        {
            s.KeepGrid(map.Grid);
            s.SetAir(map.MapUid, true);
            healthy = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            weak = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            SetHealthFraction(weak, "heart", 0.4f);
            s.SetBlood(healthy, 0.8f);
            s.SetBlood(weak, 0.8f);
            s.Life.UpdateVitalSigns(weak);

            Assert.Multiple(() =>
            {
                Assert.That(Consc(weak).PulseIrregular, Is.True);
                Assert.That(ExamineNotes(weak, healthy), Does.Contain("irregular pulse"));
                Assert.That(s.Life.BloodRegenFactor(weak), Is.EqualTo(0.5f).Within(0.001f));
                Assert.That(s.Life.BloodRegenFactor(healthy), Is.EqualTo(1f));
                Assert.That(Organ(weak, "heart").Comp.Band, Is.EqualTo(WolfmedOrganBand.Impaired));
                // Playtest 3: the organs are items on the vitals line.
                Assert.That(s.Analyzer(weak), Does.Contain("Heart impaired"));
                Assert.That(s.Analyzer(healthy), Does.Not.Contain("impaired"));
            });
        });

        await RunSeconds(20);
        await Server.WaitAssertion(() =>
        {
            var gainHealthy = s.Blood(healthy) - 0.8f;
            var gainWeak = s.Blood(weak) - 0.8f;
            _log.Add($"HeartBand: 20 s regeneration, healthy +{gainHealthy:0.0000}, impaired +{gainWeak:0.0000}.");
            Assert.That(gainHealthy, Is.GreaterThan(0f), "no regeneration to compare.");
            Assert.That(gainWeak / gainHealthy, Is.EqualTo(0.5f).Within(0.5f * Band), "the impaired heart does not halve it.");
        });
    }

    /// <summary>
    /// Playtest 3: an IPC went into shutdown after six turret rounds (Piercing 22) to the chassis. The chassis torso
    /// holds only the core and the pump, so each takes a far larger share of every hit than a human heart does among
    /// five organs. With the same rounds a chassis must last at least as long as a human chest: the pump fails no
    /// sooner than the human heart (it now lasts twice as long, the chassis being plated), and the core later still.
    /// </summary>
    [Test]
    public async Task IpcTorsoLastsAsLongAsAHumanChestTest()
    {
        await Pin();
        var map = await Pair.CreateTestMap();
        var s = new WolfmedScenario(SEntMan);

        await Server.WaitAssertion(() =>
        {
            var shooter = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var human = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var ipc = SEntMan.SpawnEntity("MobIPC", map.GridCoords);
            var humanTorso = s.Part(human, BodyPartType.Torso);
            var ipcTorso = s.Part(ipc, BodyPartType.Torso);

            int? heartFailed = null, lungsFailed = null, pumpFailed = null, coreFailed = null;
            var trace = new List<string>();
            for (var hit = 1; hit <= 40 && (heartFailed == null || coreFailed == null); hit++)
            {
                Routing.TryApplyPartDamage(human, humanTorso, Spec("Piercing", 22), shooter);
                Routing.TryApplyPartDamage(ipc, ipcTorso, Spec("Piercing", 22), shooter);
                if (heartFailed == null && Hp(human, "heart") <= 0f)
                    heartFailed = hit;
                if (lungsFailed == null && Hp(human, "lungs") <= 0f)
                    lungsFailed = hit;
                if (pumpFailed == null && Hp(ipc, "pump") <= 0f)
                    pumpFailed = hit;
                if (coreFailed == null && Hp(ipc, "posbrain") <= 0f)
                    coreFailed = hit;
                trace.Add($"{hit}: heart {Hp(human, "heart"):0.0} lungs {Hp(human, "lungs"):0.0} | pump {Hp(ipc, "pump"):0.0} core {Hp(ipc, "posbrain"):0.0}");
            }

            _log.Add("IpcTorsoLastsAsLongAsAHumanChestTest (Piercing 22 to the torso): human lungs fail on hit " +
                     $"{lungsFailed}, heart on hit {heartFailed}; IPC pump on hit {pumpFailed}, core on hit {coreFailed}.");
            _log.AddRange(trace);
            Assert.Multiple(() =>
            {
                Assert.That(heartFailed, Is.Not.Null, "forty rounds never failed the human heart.");
                Assert.That(pumpFailed, Is.Not.Null, "forty rounds never failed the pump.");
                // Playtest 4 (organs 25, cap 3): the human heart and the pump both go on hit 10, the core on hit 16.
                Assert.That(pumpFailed ?? 0, Is.GreaterThanOrEqualTo(heartFailed ?? 0), "the pump fails before a human heart.");
                Assert.That(coreFailed ?? 0, Is.GreaterThan(pumpFailed ?? 0), "the core failed no later than the pump.");
            });
        });
    }
}
