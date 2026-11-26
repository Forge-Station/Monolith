#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Server.Medical;
using Content.Server.Medical.Components;
using Content.Server._WF.Wolfmed.Life;
using Content.Shared._Onyx.Medical.Tourniquet;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.Consciousness;
using Content.Shared._WF.Wolfmed.Life;
using Content.Shared.Body.Components;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Inventory;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Stunnable;
using NUnit.Framework;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._WF.Wolfmed.Scenarios;

/// <summary>
/// M1a package A: breathing and the clock (plan §4, §7.1, §7.2). Unconscious bodies breathe, the brain's
/// breathing input is real suffocation, a shock leaves a breathing patient with a grace and a named
/// remaining problem, and the revival refusals are one shared list. Times are asserted in ±20% bands around
/// the plan's derived figures; every CVar the arithmetic reads is pinned.
/// </summary>
[TestFixture]
[TestOf(typeof(WolfmedBreathingSystem))]
public sealed class WolfmedBreathingClockTest : GameTest
{
    private const float Band = 0.2f;

    /// <summary>The plan's starting values for everything the clock arithmetic reads.</summary>
    private async Task PinClock()
    {
        await OverrideCVar(Side.Server, WolfmedCVars.Consciousness, true);
        await OverrideCVar(Side.Server, WolfmedCVars.ConsciousnessBloodDown, 0.5f);
        await OverrideCVar(Side.Server, WolfmedCVars.ConsciousnessBloodOut, 0.35f);
        await OverrideCVar(Side.Server, WolfmedCVars.ConsciousnessHysteresis, 0.1f);
        await OverrideCVar(Side.Server, WolfmedCVars.ArrestBlood, 0.30f);
        await OverrideCVar(Side.Server, WolfmedCVars.ArrestOxygenation, 0.15f);
        // The M2 figures every derivation below uses. Playtest 5 ships 180 / 600 / 0.75 / 0.05 and no CPR drain past
        // wolfmed.brain_cpr_floor; WolfmedArrestClockTest walks those.
        await OverrideCVar(Side.Server, WolfmedCVars.BrainArrestSeconds, 120f);
        await OverrideCVar(Side.Server, WolfmedCVars.BrainAirlossSeconds, 180f);
        await OverrideCVar(Side.Server, WolfmedCVars.BrainBloodSeconds, 300f);
        await OverrideCVar(Side.Server, WolfmedCVars.BrainBloodStart, 0.5f);
        await OverrideCVar(Side.Server, WolfmedCVars.BrainBloodFull, 0.3f);
        await OverrideCVar(Side.Server, WolfmedCVars.BrainRefillFactor, 0.5f);
        await OverrideCVar(Side.Server, WolfmedCVars.BrainPressureStart, 0.75f);
        await OverrideCVar(Side.Server, WolfmedCVars.BrainPressureOut, 0.45f);
        await OverrideCVar(Side.Server, WolfmedCVars.BrainDamageOxygenation, 0.4f);
        await OverrideCVar(Side.Server, WolfmedCVars.BrainDamageRate, 0.1f);
        await OverrideCVar(Side.Server, WolfmedCVars.AirlossFull, 100f);
        await OverrideCVar(Side.Server, WolfmedCVars.DefibBlood, 0.25f);
        await OverrideCVar(Side.Server, WolfmedCVars.PostShockOxygenation, 0.5f);
        await OverrideCVar(Side.Server, WolfmedCVars.PostShockGraceSeconds, 45f);
        await OverrideCVar(Side.Server, WolfmedCVars.PostShockRepeatSeconds, 300f);
        await OverrideCVar(Side.Server, WolfmedCVars.PostShockBloodTarget, 0.35f);
    }

    /// <summary>
    /// Bleeding from start to finish (plan §12 M1a). A real arterial cut Downs at 50% and knocks out at 35%
    /// with the chest still working; the heart stops at 30%; the paddles refuse at 24% naming the units and
    /// shock at 25%; the grace holds; the analyzer's two numbers; transfusing N keeps the heart going, 60%
    /// gets the patient up; untransfused, the heart stops again at 45 s. Bloodloss damage never touches the
    /// brain, and a tourniquet at 55% keeps a patient conscious.
    /// </summary>
    [Test]
    public async Task BleedingScenarioTest()
    {
        await PinClock();
        var map = await Pair.CreateTestMap();
        var s = new WolfmedScenario(SEntMan);
        EntityUid a = default;
        var bleedRate = 0f;

        await Server.WaitPost(() =>
        {
            s.SetAir(map.MapUid, true);
            a = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
        });
        await RunSeconds(3);

        // --- An arterial arm cut, from 56% so the real bleed is short. ---
        float startBlood = 0f;
        await Server.WaitAssertion(() =>
        {
            s.SetBlood(a, 0.56f);
            startBlood = s.Blood(a);
            SEntMan.System<DamageableSystem>().TryChangeDamage(a, WolfmedScenario.Spec("Slash", 25),
                origin: null, targetPart: TargetBodyPart.LeftArm);
            Assert.That(s.State(a), Is.EqualTo(WolfmedConsciousness.Up), "a 56% patient with one cut was already down.");
        });

        int? downedAt = null;
        int? outAt = null;
        string[] downedLines = [];
        var elapsed = 0;
        while (outAt == null && elapsed < 400)
        {
            await RunSeconds(1);
            elapsed++;
            await Server.WaitAssertion(() =>
            {
                var blood = s.Blood(a);
                var state = s.State(a);
                if (blood > 0.505f)
                    Assert.That(state, Is.EqualTo(WolfmedConsciousness.Up), $"down at {blood:P1} blood, above the Downed line.");
                if (blood > 0.355f)
                    Assert.That(state, Is.Not.EqualTo(WolfmedConsciousness.Unconscious), $"out at {blood:P1} blood.");

                if (downedAt == null && state != WolfmedConsciousness.Up)
                {
                    downedAt = elapsed;
                    downedLines = s.AnalyzerLines(a); // M1a D: what the medic reads at the Downed line.
                }
                if (state == WolfmedConsciousness.Unconscious)
                    outAt = elapsed;
            });
        }

        Assert.That(downedAt, Is.Not.Null, "the bleed never put the patient down.");
        Assert.That(outAt, Is.Not.Null, "the bleed never knocked the patient out.");
        TestContext.Out.WriteLine($"analyzer at Downed: {string.Join(" | ", downedLines)}");
        Assert.Multiple(() =>
        {
            Assert.That(downedLines[0], Is.EqualTo("DOWNED: blood loss"));
            // Playtest 3: normal breathing is not listed; the pulse and the blood are items on the vitals line.
            Assert.That(downedLines[1], Does.Not.Contain("reathing"));
            Assert.That(downedLines[1], Does.Match(@"^Pulse weak, rapid · Blood (49|50)% ↓"),
                "the vitals line does not name the blood %.");
        });

        await Server.WaitAssertion(() =>
        {
            bleedRate = (startBlood - s.Blood(a)) * s.Pool(a) / elapsed;
            TestContext.Out.WriteLine($"arterial arm cut (Slash 25): {bleedRate:F2} u/s net; Downed at {downedAt} s, Unconscious at {outAt} s from 56%.");
            // Fresh: the networked band is written by the once-a-second life tick and can trail the bloodstream's
            // tick that knocked the patient out, now that the slower bleed lands there a second earlier.
            Assert.That(s.Life.GetBloodBand(a), Is.EqualTo(WolfmedBloodBand.Critical));
        });

        // Unconscious and breathing: the respirator keeps cycling and never runs short in station air.
        Assert.That(await Breathes(s, a), Is.True, "an unconscious patient stopped breathing.");
        await Server.WaitAssertion(() =>
        {
            var respirator = s.Respirator(a);
            Assert.Multiple(() =>
            {
                Assert.That(s.State(a), Is.EqualTo(WolfmedConsciousness.Unconscious));
                Assert.That(respirator.SuffocationCycles, Is.EqualTo(0), "an unconscious patient in air is suffocating.");
                Assert.That(respirator.Saturation, Is.GreaterThan(respirator.SuffocationThreshold));
                Assert.That(s.Vitals(a).Breathing, Is.EqualTo(WolfmedBreathing.Normal));
                Assert.That(s.Breathing.BreathingSuppressed(a), Is.False);
            });
        });

        // --- Untreated: the heart stops at 30%, cause blood. ---
        elapsed = 0;
        var arrested = false;
        while (!arrested && elapsed < 200)
        {
            await RunSeconds(1);
            elapsed++;
            await Server.WaitPost(() => arrested = s.Life.InArrest(a));
        }

        await Server.WaitAssertion(() =>
        {
            Assert.That(arrested, Is.True, "the patient bled past 30% with a pulse.");
            Assert.That(SEntMan.GetComponent<WolfmedCardiacArrestComponent>(a).Cause, Is.EqualTo("blood"));
            Assert.That(s.Blood(a), Is.LessThanOrEqualTo(0.305f));
            Assert.That(s.Vitals(a).BloodBand, Is.EqualTo(WolfmedBloodBand.None));
            Assert.That(s.Vitals(a).Breathing, Is.EqualTo(WolfmedBreathing.None));
            Assert.That(s.Breathing.BreathingSuppressed(a), Is.True);

            // Stop the artery, then the gate: 24% is refused and the refusal names the units.
            var arm = s.Part(a, BodyPartType.Arm, BodyPartSymmetry.Left);
            Assert.That(SEntMan.System<TourniquetSystem>().Apply(a, arm), Is.True);

            s.SetBlood(a, 0.24f);
            Assert.That(s.Shock(a, out var refused), Is.False);
            Assert.That(refused, Is.EqualTo(WolfmedRevivalSystem.NoBlood));
            var (units, safe) = s.Life.GetTransfusionGuidance(a);
            var text = s.Revival.LocalizeLine(a, refused);
            TestContext.Out.WriteLine($"24% refusal: {text}");
            Assert.Multiple(() =>
            {
                Assert.That(units, Is.EqualTo(0.35f * s.Pool(a) - 0.24f * s.Pool(a)).Within(1f));
                Assert.That(safe, Is.EqualTo(0.5f * s.Pool(a) - 0.24f * s.Pool(a)).Within(1f));
                Assert.That(text, Does.Contain($"{MathF.Ceiling(units)} u"), "the refusal does not name the units.");
                Assert.That(text, Does.Not.Contain("will work"));
            });

            // At the gate the shock takes.
            s.SetBlood(a, 0.25f);
            Assert.That(s.Shock(a, out var line), Is.True, $"the gate refused 25%: {line}.");
            Assert.Multiple(() =>
            {
                Assert.That(s.Life.InArrest(a), Is.False);
                Assert.That(s.State(a), Is.EqualTo(WolfmedConsciousness.Unconscious), "25% blood woke up.");
                Assert.That(s.Life.InPostShockGrace(a), Is.True, "no grace after the shock.");
                Assert.That(s.Life.GetOxygenation(a), Is.GreaterThanOrEqualTo(0.5f - 0.001f));
                Assert.That(s.Breathing.BreathingSuppressed(a), Is.False, "a restarted patient is not allowed to breathe.");
            });

            // The analyzer's two numbers, bleed stopped: N to 35%, M to 50%.
            var analyzer = SEntMan.System<HealthAnalyzerSystem>();
            var stopped = analyzer.BuildWoundDiagnostics(a)!;
            var pool = s.Pool(a);
            Assert.Multiple(() =>
            {
                Assert.That(stopped.PostShockUnits, Is.EqualTo(0.10f * pool).Within(1f), "N with the bleed stopped.");
                Assert.That(stopped.PostShockSafeUnits, Is.EqualTo(0.25f * pool).Within(1f), "M.");
                Assert.That(stopped.PostShockGraceSeconds, Is.EqualTo(45f).Within(1f));
            });

            var shown = WolfmedPostShockText.Format(s.Blood(a), stopped.PostShockUnits, stopped.PostShockSafeUnits,
                stopped.PostShockGraceSeconds, stopped.PostShockSafeLine);
            TestContext.Out.WriteLine($"analyzer after the shock: {shown}");
            Assert.That(shown, Does.Contain($"Transfuse ≈ {MathF.Ceiling(0.10f * pool)} u within 45 s"));
            Assert.That(shown, Does.Contain($"≈ {MathF.Ceiling(0.25f * pool)} u to 50%"));

            // A slow bleed: N grows by what it takes over the grace.
            SEntMan.System<DamageableSystem>().TryChangeDamage(a, WolfmedScenario.Spec("Slash", 10),
                origin: null, targetPart: TargetBodyPart.RightArm);
            var rate = s.Life.GetBleedRate(a);
            var bleeding = analyzer.BuildWoundDiagnostics(a)!;
            Assert.Multiple(() =>
            {
                Assert.That(rate, Is.GreaterThan(0f), "the slow cut is not bleeding.");
                Assert.That(bleeding.PostShockUnits, Is.EqualTo(stopped.PostShockUnits + rate * 45f).Within(0.5f),
                    "N does not carry the bleed over the grace.");
            });

            Assert.That(SEntMan.System<TourniquetSystem>().Apply(a, s.Part(a, BodyPartType.Arm, BodyPartSymmetry.Right)), Is.True);
        });

        // The chest has been still for the whole arrest; a few real breaths reset the respirator.
        await RunSeconds(5);
        await Server.WaitAssertion(() =>
        {
            Assert.That(s.Breathing.IsSuffocating(a), Is.False, "the restarted patient is still not breathing.");
            Assert.That(s.Vitals(a).Breathing, Is.EqualTo(WolfmedBreathing.Normal));

            // Not transfused: nothing happens inside the grace, and the heart stops again when it ends.
            var graceLeft = s.Life.GetPostShockGraceSeconds(a);
            var since = 45f - graceLeft;
            var rearrest = s.Advance(a, 120, _ => s.Life.InArrest(a));
            var at = since + rearrest;
            TestContext.Out.WriteLine($"untransfused re-arrest {at:F0} s after the shock.");
            Assert.That(s.Life.InArrest(a), Is.True, "25% blood never re-arrested.");
            Assert.That(at, Is.InRange(45f * (1f - Band), 45f * (1f + Band)));
            Assert.That(at, Is.GreaterThanOrEqualTo(45f), "the heart stopped inside the grace.");
        });

        // --- Transfused by exactly N inside the grace: no re-arrest when it ends. ---
        await Server.WaitAssertion(() =>
        {
            var b = ArrestAndShock(s, map);
            var n = SEntMan.System<HealthAnalyzerSystem>().BuildWoundDiagnostics(b)!.PostShockUnits;
            s.Advance(b, 10);
            s.Transfuse(b, n);
            s.Advance(b, 50, _ =>
            {
                Assert.That(s.Life.InArrest(b), Is.False, "N units inside the grace did not hold the heart.");
                return false;
            });
        });

        // --- Transfused to 60% at 10 s: Downed within 10 s, Up within 60 s, no re-arrest in 10 min. ---
        EntityUid c = default;
        var cSeconds = 0;
        await Server.WaitAssertion(() =>
        {
            c = ArrestAndShock(s, map);
            cSeconds += s.Advance(c, 10);
            s.SetBlood(c, 0.6f);
            var toDowned = s.Advance(c, 10, _ => s.State(c) == WolfmedConsciousness.Downed);
            cSeconds += toDowned;
            Assert.That(s.State(c), Is.EqualTo(WolfmedConsciousness.Downed), "60% blood did not bring the patient round.");
        });

        // Downed has a two-second dwell on the real clock before anyone stands.
        await RunSeconds(2.5f);
        cSeconds += 2;
        await Server.WaitAssertion(() =>
        {
            if (s.State(c) != WolfmedConsciousness.Up)
                cSeconds += s.Advance(c, 60, _ => s.State(c) == WolfmedConsciousness.Up);

            TestContext.Out.WriteLine($"transfused to 60% at 10 s: up about {cSeconds} s after the shock.");
            Assert.That(s.State(c), Is.EqualTo(WolfmedConsciousness.Up), "never stood after the transfusion.");
            Assert.That(cSeconds, Is.LessThanOrEqualTo(60));

            s.Advance(c, 600, _ =>
            {
                Assert.That(s.Life.InArrest(c), Is.False, "re-arrested with 60% blood.");
                return false;
            });
        });

        // --- Blood held at 60% for ten minutes: Bloodloss damage never reaches the brain (P7). ---
        await Server.WaitAssertion(() =>
        {
            var d = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            s.SetBlood(d, 0.6f);
            SEntMan.System<DamageableSystem>().TryChangeDamage(d, WolfmedScenario.Spec("Bloodloss", 60),
                ignoreResistances: true);
            Assert.That(s.Damage(d, "Bloodloss"), Is.GreaterThan(FixedPoint2.Zero));
            s.Advance(d, 600);
            Assert.That(s.Life.GetOxygenation(d), Is.EqualTo(1f).Within(0.001f),
                "Bloodloss damage drained the brain of a patient with 60% blood.");
        });

        // --- A tourniquet at 55%: never unconscious, on their feet as the blood comes back. ---
        EntityUid e = default;
        await Server.WaitAssertion(() =>
        {
            e = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            s.SetBlood(e, 0.58f);
            SEntMan.System<DamageableSystem>().TryChangeDamage(e, WolfmedScenario.Spec("Slash", 25),
                origin: null, targetPart: TargetBodyPart.LeftArm);
        });

        elapsed = 0;
        var tied = false;
        var tiedBlood = 0f;
        while (elapsed < 120)
        {
            await RunSeconds(1);
            elapsed++;
            await Server.WaitAssertion(() =>
            {
                Assert.That(s.State(e), Is.Not.EqualTo(WolfmedConsciousness.Unconscious), "a tourniqueted patient went out.");
                if (!tied && s.Blood(e) <= 0.55f)
                {
                    tied = SEntMan.System<TourniquetSystem>().Apply(e, s.Part(e, BodyPartType.Arm, BodyPartSymmetry.Left));
                    tiedBlood = s.Blood(e);
                    Assert.That(tied, Is.True);
                }
            });

            if (tied && elapsed >= 60)
                break;
        }

        await Server.WaitAssertion(() =>
        {
            Assert.That(tied, Is.True, "the cut never took the patient to 55%.");
            Assert.That(s.Blood(e), Is.GreaterThan(tiedBlood), "the blood did not come back after the tourniquet.");
            Assert.That(s.State(e), Is.EqualTo(WolfmedConsciousness.Up));

            // OD6 (c) input: how fast a hand medic can put blood in.
            if (SProtoMan.Index<EntityPrototype>("Bloodpack")
                .TryGetComponent<HealingComponent>(out var pack, SEntMan.ComponentFactory))
            {
                var perSecond = pack.ModifyBloodLevel / pack.Delay;
                var self = pack.ModifyBloodLevel / (pack.Delay * pack.SelfHealPenaltyMultiplier);
                TestContext.Out.WriteLine(
                    $"blood pack: {pack.ModifyBloodLevel} u per {pack.Delay} s use = {perSecond:F1} u/s on a patient, {self:F1} u/s on yourself; " +
                    $"the grace's 30 u takes {MathF.Ceiling(30f / pack.ModifyBloodLevel) * pack.Delay:F0} s.");
            }
        });
    }

    /// <summary>
    /// Playtest 1: the bleed route from a healthy body at the shipped bleed rate. One untreated arterial arm cut
    /// takes at least four minutes from Up to arrest, Downed and Unconscious on the way; a plain cut (Slash 15)
    /// clots on its own long before it could put anyone down.
    /// </summary>
    [Test]
    public async Task BleedTimingTest()
    {
        await PinClock();
        await OverrideCVar(Side.Server, WolfmedCVars.BleedRate, ShippedBleedRate);
        var map = await Pair.CreateTestMap();
        var s = new WolfmedScenario(SEntMan);
        EntityUid artery = default;
        EntityUid cut = default;

        await Server.WaitPost(() =>
        {
            s.SetAir(map.MapUid, true);
            artery = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            cut = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
        });
        await RunSeconds(3);

        float arteryRate = 0f, cutRate = 0f;
        await Server.WaitAssertion(() =>
        {
            var damage = SEntMan.System<DamageableSystem>();
            damage.TryChangeDamage(artery, WolfmedScenario.Spec("Slash", 25), origin: null, targetPart: TargetBodyPart.LeftArm);
            damage.TryChangeDamage(cut, WolfmedScenario.Spec("Slash", 15), origin: null, targetPart: TargetBodyPart.RightArm);

            var arm = s.Part(artery, BodyPartType.Arm, BodyPartSymmetry.Left);
            Assert.That(SEntMan.System<WoundSystem>().GetWounds(arm)
                .Any(w => w.Comp.Prototype == "WFWolfmedArterialBleedWound"), Is.True, "Slash 25 did not cut the artery.");
            arteryRate = s.Life.GetBleedRate(artery);
            cutRate = s.Life.GetBleedRate(cut);
        });

        int? downedAt = null, outAt = null, arrestAt = null, clottedAt = null;
        var cutLowest = 1f;
        var cutStayedUp = true;
        var elapsed = 0;
        while ((arrestAt == null || clottedAt == null) && elapsed < 900)
        {
            await RunSeconds(1);
            elapsed++;
            await Server.WaitPost(() =>
            {
                var state = s.State(artery);
                if (downedAt == null && state != WolfmedConsciousness.Up)
                    downedAt = elapsed;
                if (outAt == null && state == WolfmedConsciousness.Unconscious)
                    outAt = elapsed;
                if (arrestAt == null && s.Life.InArrest(artery))
                    arrestAt = elapsed;

                cutLowest = MathF.Min(cutLowest, s.Blood(cut));
                cutStayedUp &= s.State(cut) == WolfmedConsciousness.Up;
                if (clottedAt == null && s.Life.GetBleedRate(cut) <= 0f)
                    clottedAt = elapsed;
            });
        }

        TestContext.Out.WriteLine($"bleed_rate {ShippedBleedRate}: arterial arm cut (Slash 25) {arteryRate:F2} u/s at the cut; " +
                                  $"Downed at {downedAt} s, Unconscious at {outAt} s, arrest at {arrestAt} s from full blood.");
        TestContext.Out.WriteLine($"plain cut (Slash 15) {cutRate:F2} u/s at the cut; clotted at {clottedAt} s, lowest blood {cutLowest:P1}.");
        Assert.Multiple(() =>
        {
            Assert.That(arrestAt, Is.Not.Null, "the artery never stopped the heart.");
            Assert.That(downedAt, Is.Not.Null.And.LessThan(outAt ?? int.MaxValue));
            Assert.That(outAt, Is.Not.Null.And.LessThan(arrestAt ?? int.MaxValue));
            Assert.That(clottedAt, Is.Not.Null, "the plain cut never clotted.");
            Assert.That(cutStayedUp, Is.True, "a plain cut put the patient down.");

            // Playtest 1: at least four minutes from Up to arrest. Measured 186 / 253 / 274 s (100 s at 0.6).
            Assert.That(arrestAt, Is.GreaterThanOrEqualTo(300), "an arterial arm cut kills in under five minutes.");
            Assert.That(arrestAt, Is.LessThanOrEqualTo(372f * (1f + Band)));
            Assert.That(downedAt, Is.InRange(256f * (1f - Band), 256f * (1f + Band)));
            Assert.That(clottedAt, Is.LessThanOrEqualTo(60), "a plain cut is still bleeding after a minute.");
            Assert.That(cutLowest, Is.GreaterThan(0.95f), "a plain cut lost more than 5% of the blood.");
        });
    }

    /// <summary>The wolfmed.bleed_rate default; the timing test pins it so the numbers it reports are the shipped ones.</summary>
    private const float ShippedBleedRate = 0.25f;

    /// <summary>
    /// Once per arrest episode (plan §7.1 item 6). A second shock inside the repeat window restarts the heart
    /// and nothing else, so with blood still at 25% the heart stops again at once; with blood above 30% it
    /// holds. Two shocks and no blood leave the patient worse than one shock and N units. A patient who
    /// recovered in between starts a new episode.
    /// </summary>
    [Test]
    public async Task RepeatedShockTest()
    {
        await PinClock();
        var map = await Pair.CreateTestMap();
        var s = new WolfmedScenario(SEntMan);
        await Server.WaitPost(() => s.SetAir(map.MapUid, true));

        await Server.WaitAssertion(() =>
        {
            // One shock, no blood: the heart stops again when the grace ends.
            var twice = ArrestAndShock(s, map);
            var first = s.Advance(twice, 120, _ => s.Life.InArrest(twice));
            Assert.That(first, Is.InRange(45f * (1f - Band), 45f * (1f + Band)), "the first re-arrest missed the grace.");

            // The second shock, well inside the repeat window.
            var before = s.Life.GetOxygenation(twice);
            Assert.That(s.Shock(twice, out var line), Is.True, line);
            Assert.Multiple(() =>
            {
                Assert.That(s.Life.GetOxygenation(twice), Is.EqualTo(before).Within(0.001f),
                    "a repeat shock restored oxygenation.");
                Assert.That(s.Life.InPostShockGrace(twice), Is.False, "a repeat shock opened another grace.");
                Assert.That(s.Life.InArrest(twice), Is.True, "25% blood held a restarted heart with no grace.");
            });

            // The same, with blood above 30% before the second shock: it holds.
            var raised = ArrestAndShock(s, map);
            s.Advance(raised, 120, _ => s.Life.InArrest(raised));
            s.SetBlood(raised, 0.32f);
            Assert.That(s.Shock(raised, out line), Is.True, line);
            Assert.That(s.Life.InArrest(raised), Is.False, "blood above 30% re-arrested at once.");

            // The comparison: one shock plus the analyzer's N units inside the grace.
            var once = ArrestAndShock(s, map);
            var n = SEntMan.System<HealthAnalyzerSystem>().BuildWoundDiagnostics(once)!.PostShockUnits;
            s.Advance(once, 10);
            s.Transfuse(once, n);

            // Line the clocks up: 'twice' is already `first` seconds past its shock.
            var twiceElapsed = first;
            var onceElapsed = 10;
            s.Advance(twice, 120 - twiceElapsed);
            s.Advance(once, 120 - onceElapsed);
            TestContext.Out.WriteLine($"2 min: two shocks {s.Life.GetOxygenation(twice):F3} / brain {s.Life.GetBrainActivity(twice):P1}; " +
                                      $"one shock + {n:F0} u {s.Life.GetOxygenation(once):F3} / brain {s.Life.GetBrainActivity(once):P1}");
            Assert.That(s.Life.GetOxygenation(twice), Is.LessThan(s.Life.GetOxygenation(once)),
                "at 2 min two shocks without blood kept more oxygen than one shock with N units.");

            s.Advance(twice, 60);
            s.Advance(once, 60);
            TestContext.Out.WriteLine($"3 min: two shocks {s.Life.GetOxygenation(twice):F3} / brain {s.Life.GetBrainActivity(twice):P1}; " +
                                      $"one shock + {n:F0} u {s.Life.GetOxygenation(once):F3} / brain {s.Life.GetBrainActivity(once):P1}");
            Assert.Multiple(() =>
            {
                Assert.That(s.Life.GetOxygenation(twice), Is.LessThanOrEqualTo(s.Life.GetOxygenation(once)));
                Assert.That(s.Life.GetBrainActivity(twice), Is.LessThan(s.Life.GetBrainActivity(once)),
                    "two shocks without blood lost no more brain than one shock with N units.");
            });
        });

        // A patient who got up with the blood back has recovered: a new arrest well inside the repeat window
        // is its own episode, with its own restore and grace.
        EntityUid well = default;
        await Server.WaitAssertion(() =>
        {
            well = ArrestAndShock(s, map);
            s.SetBlood(well, 0.6f);
            s.Advance(well, 10, _ => s.State(well) == WolfmedConsciousness.Downed);
        });

        // Downed has a two-second dwell on the real clock before anyone stands.
        await RunSeconds(2.5f);
        await Server.WaitAssertion(() =>
        {
            // Well inside the 300 s repeat window: only the recovery can end the episode here.
            s.Advance(well, 90, _ => !SEntMan.HasComponent<WolfmedPostShockComponent>(well));
            Assert.Multiple(() =>
            {
                Assert.That(s.State(well), Is.EqualTo(WolfmedConsciousness.Up), "never stood after the transfusion.");
                Assert.That(SEntMan.HasComponent<WolfmedPostShockComponent>(well), Is.False,
                    "a recovered patient's arrest episode never ended.");
            });

            s.SetBlood(well, 0.29f);
            s.Life.Tick(well, 1f);
            Assert.That(s.Life.InArrest(well), Is.True, "29% blood kept a pulse.");
            s.SetBlood(well, 0.25f);
            Assert.That(s.Shock(well, out var line), Is.True, line);
            Assert.Multiple(() =>
            {
                Assert.That(s.Life.InPostShockGrace(well), Is.True, "a new arrest after recovery was treated as a repeat.");
                Assert.That(s.Life.GetOxygenation(well), Is.GreaterThanOrEqualTo(0.5f - 0.001f));
                Assert.That(s.Life.InArrest(well), Is.False);
            });
        });
    }

    /// <summary>
    /// Oxygen arrest in an airless room, air back, a shock (plan §7.1): the patient comes round Downed at
    /// once, breathing, and stands in about 15 s without ever going out again.
    /// </summary>
    [Test]
    public async Task PostShockOxygenTest()
    {
        await PinClock();
        var map = await Pair.CreateTestMap();
        var s = new WolfmedScenario(SEntMan);
        EntityUid body = default;

        await Server.WaitPost(() =>
        {
            s.SetAir(map.MapUid, false);
            body = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
        });

        await WaitForSuffocation(s, body);

        await Server.WaitAssertion(() =>
        {
            // Past the point where suffocation drains at its full rate, then the clock to the oxygen trigger.
            var missing = 100f - s.Damage(body, "Asphyxiation").Float();
            if (missing > 0f)
                SEntMan.System<DamageableSystem>().TryChangeDamage(body, WolfmedScenario.Spec("Asphyxiation", missing), ignoreResistances: true);

            s.Advance(body, 400, _ => s.Life.InArrest(body));
            Assert.That(s.Life.InArrest(body), Is.True, "the brain ran dry without the heart stopping.");
            Assert.That(SEntMan.GetComponent<WolfmedCardiacArrestComponent>(body).Cause, Is.EqualTo("oxygen"));

            s.SetAir(map.MapUid, true);
            Assert.That(s.Shock(body, out var line), Is.True, line);
            Assert.Multiple(() =>
            {
                Assert.That(s.State(body), Is.EqualTo(WolfmedConsciousness.Downed), "an oxygen arrest did not come round Downed at once.");
                Assert.That(s.Breathing.BreathingSuppressed(body), Is.False);
                Assert.That(s.Life.GetOxygenation(body), Is.EqualTo(0.5f).Within(0.001f));
            });
        });

        int? upAt = null;
        int? breathingAt = null;
        var breathingOxygen = 0f;
        for (var second = 1; second <= 40 && upAt == null; second++)
        {
            await RunSeconds(1);
            var now = second;
            await Server.WaitAssertion(() =>
            {
                Assert.That(s.State(body), Is.Not.EqualTo(WolfmedConsciousness.Unconscious), $"went out again {now} s after the shock.");
                if (breathingAt == null && !s.Breathing.IsSuffocating(body))
                {
                    breathingAt = now;
                    breathingOxygen = s.Life.GetOxygenation(body);
                }

                if (s.State(body) == WolfmedConsciousness.Up)
                    upAt = now;
            });
        }

        // Derived: the stand line is oxygenation 0.75 - 0.9 x 0.7 x 0.3 = 0.561 and the refill is 0.5 / 120 a
        // second, so 0.5 stands in 15 s (the plan's figure). The chest has been still for the whole arrest,
        // though: the respirator takes a few cycles to land a breath and refill its saturation, and the brain
        // drains at the full suffocation rate until it has. The band is taken from where it got its breath.
        Assert.That(upAt, Is.Not.Null, "never stood up.");
        Assert.That(breathingAt, Is.Not.Null, "never breathed again.");
        var derived = breathingAt!.Value + (0.561f - breathingOxygen) / (0.5f / 120f);
        TestContext.Out.WriteLine($"oxygen arrest shocked: breathing again at {breathingAt} s (oxygenation {breathingOxygen:F3}), up at {upAt} s; derived {derived:F0} s, plan 15 s from 0.5.");
        Assert.That(upAt!.Value, Is.InRange(derived * (1f - Band), derived * (1f + Band)));
        Assert.That(breathingAt!.Value, Is.LessThanOrEqualTo(8), "the respirator took more than four cycles to recover.");
    }

    /// <summary>
    /// No air (plan §3.3): Downed and then Unconscious at the derived times, gasping; internals fitted at
    /// Unconscious stop the drain at the first good breath although the Asphyxiation is still there, and the
    /// patient comes round. Lungs out in station air suffocate with the lungs named.
    /// </summary>
    [Test]
    public async Task OxygenScenarioTest()
    {
        await PinClock();
        var map = await Pair.CreateTestMap();
        var s = new WolfmedScenario(SEntMan);
        EntityUid body = default;

        await Server.WaitPost(() =>
        {
            s.SetAir(map.MapUid, false);
            body = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
        });

        int? downedAt = null;
        int? outAt = null;
        string[] outLines = [];
        var gasping = false;
        for (var second = 1; second <= 320 && outAt == null; second++)
        {
            await RunSeconds(1);
            var now = second;
            await Server.WaitAssertion(() =>
            {
                gasping |= s.Vitals(body).Breathing == WolfmedBreathing.Gasping &&
                           s.Vitals(body).BreathingSource == WolfmedBreathingSource.NoAir;
                if (downedAt == null && s.State(body) != WolfmedConsciousness.Up)
                {
                    downedAt = now;
                    TestContext.Out.WriteLine($"no air, Downed: Asphyxiation {s.Damage(body, "Asphyxiation")}, oxygenation {s.Life.GetOxygenation(body):F3}, blood {s.Blood(body):P0}.");
                }

                if (s.State(body) == WolfmedConsciousness.Unconscious)
                {
                    outAt = now;
                    outLines = s.AnalyzerLines(body); // M1a D
                }
            });
        }

        TestContext.Out.WriteLine($"no air: Downed at {downedAt} s, Unconscious at {outAt} s.");
        TestContext.Out.WriteLine($"analyzer at Unconscious: {string.Join(" | ", outLines)}");
        Assert.Multiple(() =>
        {
            Assert.That(outLines[0], Does.StartWith("UNCONSCIOUS: no oxygen"));
            Assert.That(outLines[1], Does.StartWith("Not breathing: no air")); // playtest 3
        });
        Assert.Multiple(() =>
        {
            Assert.That(gasping, Is.True, "a suffocating patient never read as gasping for air.");
            Assert.That(downedAt, Is.Not.Null.And.InRange(188f * (1f - Band), 188f * (1f + Band)));
            Assert.That(outAt, Is.Not.Null.And.InRange(205f * (1f - Band), 205f * (1f + Band)));
        });

        // Internals at Unconscious.
        await Server.WaitAssertion(() =>
        {
            var inventory = SEntMan.System<InventorySystem>();
            var mask = SEntMan.SpawnEntity("ClothingMaskBreath", map.GridCoords);
            var tank = SEntMan.SpawnEntity("EmergencyOxygenTankFilled", map.GridCoords);
            Assert.That(inventory.TryEquip(body, mask, "mask", silent: true, force: true), Is.True);
            Assert.That(inventory.TryEquip(body, tank, "belt", silent: true, force: true), Is.True);
            var internals = SEntMan.System<InternalsSystem>();
            Assert.That(internals.ToggleInternals(body, body, force: true), Is.True);
            Assert.That(internals.AreInternalsWorking(body), Is.True, "the internals are not connected.");
        });

        int? drainStopped = null;
        int? awakeAt = null;
        for (var second = 1; second <= 30 && awakeAt == null; second++)
        {
            await RunSeconds(1);
            var now = second;
            await Server.WaitAssertion(() =>
            {
                var brain = s.Life.GetBrain(body)!.Value;
                if (drainStopped == null && s.Life.DrainRate(body, brain) <= 0f)
                {
                    drainStopped = now;
                    Assert.That(s.Damage(body, "Asphyxiation"), Is.GreaterThan(FixedPoint2.Zero),
                        "the drain only stopped once the damage was gone.");
                }

                if (s.State(body) != WolfmedConsciousness.Unconscious)
                    awakeAt = now;
            });
        }

        TestContext.Out.WriteLine($"internals: drain stopped at {drainStopped} s, awake at {awakeAt} s.");
        Assert.Multiple(() =>
        {
            // Plan: 3 s. The respirator lands a breath on its own 2 s cycle and the saturation it carries is
            // counted on the next one, so the honest bound is two cycles.
            // The drain stops on the respirator's next breath, and a pooled pair's respirator is mid-cycle when the
            // internals go on, so the sample can land a breath later than on a fresh pair: 3 to 6 s (pod atmosphere).
            Assert.That(drainStopped, Is.Not.Null.And.LessThanOrEqualTo(6));
            // Plan: 15 s (7 s from the line at the refill rate, plus margin). The drain runs at the full rate
            // until the first breath lands, which is what the measured 16 s is made of; ±20% on the plan.
            Assert.That(awakeAt, Is.Not.Null.And.LessThanOrEqualTo(15f * (1f + Band)));
        });

        // Lungs out in station air.
        EntityUid lungless = default;
        await Server.WaitAssertion(() =>
        {
            s.SetAir(map.MapUid, true);
            lungless = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var graph = SEntMan.System<SharedBodySystem>();
            foreach (var lung in graph.GetBodyOrganEntityComps<LungComponent>((lungless, null)).ToList())
                Assert.That(graph.RemoveOrgan(lung.Owner), Is.True);
        });

        await WaitForSuffocation(s, lungless);
        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(s.Vitals(lungless).Breathing, Is.EqualTo(WolfmedBreathing.None));
                Assert.That(s.Vitals(lungless).BreathingSource, Is.EqualTo(WolfmedBreathingSource.Lungs));
                Assert.That(s.Life.BreathingLevel(lungless), Is.GreaterThan(0f), "lungs out in air is not suffocation.");
            });
        });
    }

    /// <summary>P22: an internal bleed loses what its rate says, instead of rounding to nothing every frame.</summary>
    [Test]
    public async Task InternalBleedTickTest()
    {
        await PinClock();
        var map = await Pair.CreateTestMap();
        var s = new WolfmedScenario(SEntMan);
        EntityUid control = default, ten = default, twenty = default;
        float c0 = 0, t0 = 0, w0 = 0;

        await Server.WaitAssertion(() =>
        {
            s.SetAir(map.MapUid, true);
            control = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            ten = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            twenty = SEntMan.SpawnEntity("MobHuman", map.GridCoords);

            // All three below full so all three regenerate alike; the difference is the bleed.
            foreach (var body in new[] { control, ten, twenty })
                s.SetBlood(body, 0.8f);

            var wounds = SEntMan.System<WoundSystem>();
            Assert.That(wounds.CreateOrMergeWound(s.Part(ten, BodyPartType.Torso), "InternalBleedingWound", FixedPoint2.New(10)), Is.Not.Null);
            Assert.That(wounds.CreateOrMergeWound(s.Part(twenty, BodyPartType.Torso), "InternalBleedingWound", FixedPoint2.New(20)), Is.Not.Null);
            Assert.That(s.Life.GetBleedRate(ten), Is.EqualTo(0.1f).Within(0.001f), "severity 10 is not 0.1 u/s by the prototype.");

            c0 = s.Blood(control) * s.Pool(control);
            t0 = s.Blood(ten) * s.Pool(ten);
            w0 = s.Blood(twenty) * s.Pool(twenty);
        });

        await RunSeconds(60);

        await Server.WaitAssertion(() =>
        {
            var regen = s.Blood(control) * s.Pool(control) - c0;
            var lostTen = (t0 + regen - s.Blood(ten) * s.Pool(ten)) / 60f;
            var lostTwenty = (w0 + regen - s.Blood(twenty) * s.Pool(twenty)) / 60f;
            TestContext.Out.WriteLine($"internal bleed: severity 10 {lostTen:F3} u/s, severity 20 {lostTwenty:F3} u/s (control regenerated {regen:F1} u).");
            Assert.Multiple(() =>
            {
                Assert.That(lostTen, Is.InRange(0.1f * (1f - Band), 0.1f * (1f + Band)));
                Assert.That(lostTwenty, Is.InRange(0.2f * (1f - Band), 0.2f * (1f + Band)));
            });
        });
    }

    /// <summary>P14: a pain shock at 45% blood stuns and floors the patient and never stops the heart.</summary>
    [Test]
    public async Task PainShockNoArrestTest()
    {
        await OverrideCVar(Side.Server, WolfmedCVars.PainScale, 1f); // playtest 5: Blunt 60 + 40 reaches the shock line at Onyx's figures

        await PinClock();
        var map = await Pair.CreateTestMap();
        var s = new WolfmedScenario(SEntMan);

        await Server.WaitAssertion(() =>
        {
            Assert.That(WolfmedCVars.ArrestShockBlood.DefaultValue, Is.EqualTo(0f),
                "the pain-shock arrest is back on by default.");

            s.SetAir(map.MapUid, true);
            var body = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            s.SetBlood(body, 0.45f);

            var routing = SEntMan.System<WoundDamageRoutingSystem>();
            Assert.That(routing.TryApplyPartDamage(body, s.Part(body, BodyPartType.Torso), WolfmedScenario.Spec("Blunt", 60)));
            // Package B: 60 on the head as well sums past the 189 faint line, and a fainted body is Critical, which
            // takes no paralysis. 40 keeps the summed pain under the line and the body's (capped) pain at 135.
            // M3: on an arm, not the head: Blunt 30 or more to the head is a knockout (plan §3.6), Critical again.
            Assert.That(routing.TryApplyPartDamage(body, s.Part(body, BodyPartType.Arm, BodyPartSymmetry.Left), WolfmedScenario.Spec("Blunt", 40)));
            Assert.That(SEntMan.GetComponent<WolfmedConsciousnessComponent>(body).Cause, Is.Not.EqualTo(WolfmedCause.PainFaint),
                "the fixture fainted, so the shock's stun cannot be checked.");
            s.Advance(body, 5);

            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.HasComponent<StunnedComponent>(body), Is.True, "no pain shock fired.");
                Assert.That(SEntMan.HasComponent<WolfmedCardiacArrestComponent>(body), Is.False,
                    "a pain shock at 45% blood stopped the heart.");
            });
        });
    }

    /// <summary>
    /// The hook's other half (plan §4.5): a Critical body that is not a wound host still does not inhale,
    /// while a wound host held unconscious by a pressure does.
    /// </summary>
    [Test]
    public async Task NonWoundHostCriticalStillDoesNotBreatheTest()
    {
        await PinClock();
        var map = await Pair.CreateTestMap();
        var s = new WolfmedScenario(SEntMan);
        EntityUid stock = default, host = default;
        RespiratorStatus stockBefore = default;

        await Server.WaitAssertion(() =>
        {
            s.SetAir(map.MapUid, true);
            stock = SEntMan.SpawnEntity("WolfmedStockBreather", map.GridCoords);
            host = SEntMan.SpawnEntity("MobHuman", map.GridCoords);

            Assert.That(SEntMan.HasComponent<WoundHostComponent>(stock), Is.False);
            SEntMan.System<MobStateSystem>().ChangeMobState(stock, MobState.Critical);
            s.Consciousness.SetExternalPressure(host, "test", 1f);

            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.System<MobStateSystem>().IsCritical(stock), Is.True);
                Assert.That(SEntMan.System<MobStateSystem>().IsCritical(host), Is.True);
                Assert.That(s.Breathing.BreathingSuppressed(stock), Is.True, "a stock Critical body is allowed to breathe.");
                Assert.That(s.Breathing.BreathingSuppressed(host), Is.False, "an unconscious wound host is not allowed to breathe.");
            });

            stockBefore = s.Respirator(stock).Status;
        });

        Assert.That(await Breathes(s, host), Is.True, "the unconscious wound host did not breathe.");
        await Server.WaitAssertion(() =>
            Assert.That(s.Respirator(stock).Status, Is.EqualTo(stockBefore), "the stock Critical body took a breath."));
    }

    /// <summary>
    /// A human arrested from blood, a minute on the arrest clock (oxygenation 0.5, the plan's §7.1 table),
    /// then shocked once at 25%. All on the life tick: no real time passes.
    /// </summary>
    private EntityUid ArrestAndShock(WolfmedScenario s, TestMapData map)
    {
        var body = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
        s.SetBlood(body, 0.29f);
        s.Life.Tick(body, 1f);
        Assert.That(s.Life.InArrest(body), Is.True, "29% blood kept a pulse.");
        s.Advance(body, 59);
        Assert.That(s.Life.GetOxygenation(body), Is.EqualTo(0.5f).Within(0.01f));
        s.SetBlood(body, 0.25f);
        Assert.That(s.Shock(body, out var line), Is.True, line);
        Assert.That(s.Life.InPostShockGrace(body), Is.True);
        return body;
    }

    /// <summary>Whether the respirator changes between inhaling and exhaling inside two of its cycles.</summary>
    private async Task<bool> Breathes(WolfmedScenario s, EntityUid body)
    {
        RespiratorStatus first = default;
        var changed = false;
        await Server.WaitPost(() => first = s.Respirator(body).Status);
        for (var step = 0; step < 10 && !changed; step++)
        {
            await RunSeconds(0.5f);
            await Server.WaitPost(() => changed = s.Respirator(body).Status != first);
        }

        return changed;
    }

    /// <summary>Real time until the respirator is suffocating: a few cycles once the saturation runs out.</summary>
    private async Task WaitForSuffocation(WolfmedScenario s, EntityUid body)
    {
        var suffocating = false;
        for (var second = 0; second < 30 && !suffocating; second++)
        {
            await RunSeconds(1);
            await Server.WaitPost(() => suffocating = s.Breathing.IsSuffocating(body));
        }

        Assert.That(suffocating, Is.True, "the respirator never ran short.");
    }

    [TestPrototypes]
    private const string Prototypes = @"
# A breathing body with a mob state and no wound host: the upstream respirator rule applies to it.
- type: entity
  id: WolfmedStockBreather
  components:
  - type: SolutionContainerManager
  - type: Body
    prototype: Human
  - type: MobState
    allowedStates:
    - Alive
    - Critical
    - Dead
  - type: MobThresholds
    thresholds:
      0: Alive
      100: Critical
      200: Dead
  - type: Damageable
  - type: Respirator
    damage:
      types:
        Asphyxiation: 1.5
    damageRecovery:
      types:
        Asphyxiation: -1.5
";
}
