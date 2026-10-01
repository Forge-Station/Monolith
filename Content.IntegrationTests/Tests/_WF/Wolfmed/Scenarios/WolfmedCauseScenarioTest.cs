#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Body.Systems;
using Content.Server.Temperature.Components;
using Content.Server._WF.Wolfmed.Consciousness;
using Content.Server._WF.Wolfmed.Hud;
using Content.Server._WF.Wolfmed.Life;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Body.Organ;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.Consciousness;
using Content.Shared._WF.Wolfmed.Hud;
using Content.Shared._WF.Wolfmed.Life;
using Content.Shared._WF.Wolfmed.Reagents;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Mind;
using Content.Shared.Mobs.Systems;
using Content.Shared.Stunnable;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Robust.Shared.Localization;
using NUnit.Framework;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WF.Wolfmed.Scenarios;

/// <summary>
/// M1a package B scenarios (plan §12 M1a): every state names its cause and what else holds the body; pain
/// knocks a patient out briefly and never for long; the body's pain is one number; an IPC's shutdown says
/// why. Human and IPC only (plan §9.1); the report-mode conformance test tracks the other species.
/// </summary>
/// <remarks>
/// Times are asserted as order plus a ±20% band, never exact seconds, and every CVar the arithmetic reads is
/// pinned to the plan's starting value. Pain goes on the parts with its wound floor set to the same value,
/// the stand-in for a steady injury: a part's pain only recovers down to its floor.
/// </remarks>
[TestFixture]
[TestOf(typeof(WolfmedConsciousnessSystem))]
public sealed class WolfmedCauseScenarioTest : GameTest
{
    private const float Band = 0.2f;
    private const float FaintSeconds = 20f;
    private const float Cooldown = 30f;

    /// <summary>
    /// M1b: the shipped cooldown. With fire no longer stopped at the old 600 its burns keep climbing, and at 30 s a
    /// fire plus blows chained three faints (53 s in two minutes); plan §3.1's fallback is 50.
    /// </summary>
    private const float ShippedCooldown = 50f;

    private async Task PinPain()
    {
        await OverrideCVar(Side.Server, WolfmedCVars.Consciousness, true);
        await OverrideCVar(Side.Server, WolfmedCVars.ConsciousnessPainDown, 0.95f);
        await OverrideCVar(Side.Server, WolfmedCVars.ConsciousnessPainOut, 1.4f);
        await OverrideCVar(Side.Server, WolfmedCVars.ConsciousnessBloodDown, 0.5f);
        await OverrideCVar(Side.Server, WolfmedCVars.ConsciousnessBloodOut, 0.35f);
        await OverrideCVar(Side.Server, WolfmedCVars.ConsciousnessHysteresis, 0.1f);
        await OverrideCVar(Side.Server, WolfmedCVars.PainFaintSeconds, FaintSeconds);
        await OverrideCVar(Side.Server, WolfmedCVars.PainFaintRise, 40f);
        await OverrideCVar(Side.Server, WolfmedCVars.PainFaintCooldown, Cooldown);
        await OverrideCVar(Side.Server, WolfmedCVars.PainShockThreshold, 130f);
        await OverrideCVar(Side.Server, WolfmedCVars.PainShockRearm, 110f);
        await OverrideCVar(Side.Server, WolfmedCVars.AdrenalineSeconds, 30f);
        await OverrideCVar(Side.Server, WolfmedCVars.AdrenalineCrawlMultiplier, 1.5f);
        await OverrideCVar(Side.Server, WolfmedCVars.ArrestShockBlood, 0f);
        await OverrideCVar(Side.Server, WolfmedCVars.ArrestBlood, 0.30f);
        await OverrideCVar(Side.Server, WolfmedCVars.BrainAirlossSeconds, 180f);
        await OverrideCVar(Side.Server, WolfmedCVars.BrainPressureStart, 0.75f);
        await OverrideCVar(Side.Server, WolfmedCVars.BrainPressureOut, 0.45f);
        await OverrideCVar(Side.Server, WolfmedCVars.AirlossFull, 100f);
    }

    /// <summary>Pain on one part, with its wound floor at the same value so it does not recover away.</summary>
    private void SetPain(EntityUid body, BodyPartType type, float value,
        BodyPartSymmetry symmetry = BodyPartSymmetry.None)
    {
        var part = SEntMan.System<SharedBodySystem>().GetBodyChildren(body)
            .First(p => p.Component.PartType == type && p.Component.Symmetry == symmetry).Id;
        var pain = SEntMan.GetComponent<PainComponent>(part);
        pain.WoundPain = FixedPoint2.New(value);
        SEntMan.System<PainSystem>().SetPain((part, pain), FixedPoint2.New(value));
    }

    private WolfmedConsciousnessComponent Consc(EntityUid body) =>
        SEntMan.GetComponent<WolfmedConsciousnessComponent>(body);

    private float Now => (float) SGameTiming.CurTime.TotalSeconds;

    /// <summary>Polls every half second until the condition holds; returns the seconds it took, or null.</summary>
    private async Task<float?> WaitFor(Func<bool> condition, float limit)
    {
        var start = 0f;
        await Server.WaitPost(() => start = Now);
        for (var waited = 0f; waited <= limit; waited += 0.5f)
        {
            var met = false;
            await Server.WaitPost(() => met = condition());
            if (met)
            {
                var end = 0f;
                await Server.WaitPost(() => end = Now);
                return end - start;
            }

            await RunSeconds(0.5f);
        }

        return null;
    }

    /// <summary>
    /// The pain loop (plan §3.1, OD4, OD5, OD9): a faint of fixed length that nothing extends, breathing all
    /// the while; no second faint on a steady injury; a fresh rise faints again but never inside the cooldown,
    /// and pain added during a faint never counts toward the rise; an opiate ends a faint and stands the
    /// patient up; the pain shock no longer does; a machine is Downed by pain and never knocked out.
    /// </summary>
    [Test]
    public async Task PainScenarioTest()
    {
        await PinPain();
        var map = await Pair.CreateTestMap();
        var s = new WolfmedScenario(SEntMan);
        var alerts = SEntMan.System<WolfmedConditionAlertSystem>();
        EntityUid a = default;

        await Server.WaitPost(() =>
        {
            s.SetAir(map.MapUid, true);
            s.KeepGrid(map.Grid); // M1b: the ten-minute stretch outlived Mono's grid cleanup in full runs.
            a = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
        });
        await RunSeconds(3);

        // --- Summed pain 200 at full blood: past the 189 faint line. ---
        await Server.WaitAssertion(() =>
        {
            SetPain(a, BodyPartType.Torso, 100);
            SetPain(a, BodyPartType.Head, 100);

            var comp = Consc(a);
            Assert.Multiple(() =>
            {
                Assert.That(comp.State, Is.EqualTo(WolfmedConsciousness.Unconscious), "200 summed pain did not faint.");
                Assert.That(comp.Cause, Is.EqualTo(WolfmedCause.PainFaint));
                Assert.That(comp.Blockers, Is.EqualTo(WolfmedCauseFlags.None));
                Assert.That(SEntMan.System<MobStateSystem>().IsCritical(a), Is.True, "a faint is Critical.");
                Assert.That(s.Breathing.BreathingSuppressed(a), Is.False, "a fainted patient stopped breathing.");
                Assert.That(alerts.GetShownHealthAlert(a)?.Id, Is.EqualTo("WFWolfmedFaintPain"));
                // Playtest 2: the seconds, counted from the faint's start.
                Assert.That(alerts.GetConditionText(a),
                    Does.Contain(Loc.GetString("wolfmed-cause-pain-faint-help-timed", ("seconds", (int) FaintSeconds))),
                    "an unblocked faint did not say when the patient comes round.");
                // M1a D: what the medic reads (plan §12 M1a).
                Assert.That(s.AnalyzerLines(a)[0], Is.EqualTo($"FAINTED: pain, {(int) FaintSeconds} s"));
            });
        });

        // Hits during the faint never lengthen it: 20 more pain ten seconds in.
        await RunSeconds(10);
        await Server.WaitPost(() => SetPain(a, BodyPartType.Arm, 20, BodyPartSymmetry.Left));

        var woke = await WaitFor(() => Consc(a).State != WolfmedConsciousness.Unconscious, 20f);
        Assert.That(woke, Is.Not.Null, "the faint lasted past its 20 s.");
        var faintLength = 10f + woke!.Value;
        Assert.That(faintLength, Is.InRange(FaintSeconds * (1 - Band), FaintSeconds * (1 + Band)),
            "the faint did not last its fixed length.");
        Assert.That(faintLength, Is.LessThanOrEqualTo(FaintSeconds + 1f), "damage during the faint lengthened it.");

        await Server.WaitAssertion(() =>
        {
            var comp = Consc(a);
            Assert.That(comp.State, Is.EqualTo(WolfmedConsciousness.Downed), "the patient did not come round Downed.");
            Assert.That(comp.Cause, Is.EqualTo(WolfmedCause.Pain));
            Assert.That(comp.LastConditionLine, Does.Contain(Loc.GetString("wolfmed-cause-pain-wake")));

            // +50 over the pain at waking, inside the cooldown.
            SetPain(a, BodyPartType.Arm, 50, BodyPartSymmetry.Right);
        });

        var refaint = await WaitFor(() => Consc(a).State == WolfmedConsciousness.Unconscious, Cooldown + 5f);
        Assert.That(refaint, Is.Not.Null, "a +40 rise over the pain at waking never fainted again.");
        Assert.That(refaint!.Value, Is.GreaterThanOrEqualTo(Cooldown - 1f), "a faint started inside the cooldown.");
        Assert.That(refaint.Value, Is.LessThanOrEqualTo(Cooldown + 1.5f), "the cooldown ran long.");

        // Pain added during this faint does not count: the baseline is the pain at waking.
        await RunSeconds(5);
        await Server.WaitPost(() => SetPain(a, BodyPartType.Leg, 45, BodyPartSymmetry.Left));
        Assert.That(await WaitFor(() => Consc(a).State != WolfmedConsciousness.Unconscious, 20f), Is.Not.Null);

        // Steady for ten minutes: no faint, no suffocation, no arrest.
        for (var second = 0; second < 600; second += 5)
        {
            await RunSeconds(5);
            await Server.WaitAssertion(() =>
            {
                var comp = Consc(a);
                Assert.That(comp.State, Is.EqualTo(WolfmedConsciousness.Downed),
                    $"a steady injury changed state at {second + 5} s: {comp.State}, {comp.Cause}.");
                Assert.That(s.Life.InArrest(a), Is.False, "pain stopped the heart.");
            });
        }

        await Server.WaitAssertion(() =>
        {
            Assert.That(s.Damage(a, "Asphyxiation"), Is.EqualTo(FixedPoint2.Zero), "a pain patient suffocated.");
            Assert.That(s.Life.GetOxygenation(a), Is.EqualTo(1f).Within(0.001f), "a pain patient lost oxygen.");
        });

        // --- An opiate ends a faint and stands the patient up. ---
        EntityUid b = default;
        await Server.WaitAssertion(() =>
        {
            b = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
        });
        await RunSeconds(2);
        await Server.WaitAssertion(() =>
        {
            SetPain(b, BodyPartType.Torso, 100);
            SetPain(b, BodyPartType.Head, 100);
            Assert.That(Consc(b).Cause, Is.EqualTo(WolfmedCause.PainFaint));
            SEntMan.System<BloodstreamSystem>().TryAddToChemicals(b, new Solution("WFWolfmedOpiate", FixedPoint2.New(5)));
        });

        var stood = await WaitFor(() => Consc(b).State == WolfmedConsciousness.Up, 8f);
        Assert.That(stood, Is.Not.Null, "an opiate did not end the faint and stand the patient up.");
        Assert.That(stood!.Value, Is.LessThan(FaintSeconds / 2f), "the faint ended on its timer, not the opiate.");

        // --- The pain shock no longer stands a Downed patient up (OD5). ---
        EntityUid c = default;
        await Server.WaitAssertion(() =>
        {
            c = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
        });
        await RunSeconds(2);
        await Server.WaitAssertion(() =>
        {
            SetPain(c, BodyPartType.Torso, 129);
            Assert.That(Consc(c).State, Is.EqualTo(WolfmedConsciousness.Downed));
            SetPain(c, BodyPartType.Torso, 135);

            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.HasComponent<StunnedComponent>(c), Is.True, "no pain shock at 135.");
                Assert.That(SEntMan.System<WolfmedBodyPainSystem>().HasAdrenaline(c), Is.True);
                Assert.That(Consc(c).LastConditionLine, Is.EqualTo(Loc.GetString("wolfmed-condition-adrenaline-start")));
            });
        });
        await RunSeconds(5);
        var boosted = 0f;
        var delayed = 0f;
        await Server.WaitAssertion(() =>
        {
            Assert.That(Consc(c).State, Is.EqualTo(WolfmedConsciousness.Downed), "the pain shock stood the patient up.");

            // OD5: the adrenaline is a crawl boost and lifts the Downed do-after penalty.
            var delay = new Content.Shared._Goobstation.DoAfter.GetDoAfterDelayMultiplierEvent();
            SEntMan.EventBus.RaiseLocalEvent(c, delay);
            delayed = delay.Multiplier;
            boosted = SEntMan.GetComponent<Content.Shared.Movement.Components.MovementSpeedModifierComponent>(c).WalkSpeedModifier;

            // End the window now rather than waiting out its 30 s.
            SEntMan.GetComponent<PainShockTargetComponent>(c).AdrenalineEnds = SGameTiming.CurTime;
        });
        await RunSeconds(2);
        await Server.WaitAssertion(() =>
        {
            var walk = SEntMan.GetComponent<Content.Shared.Movement.Components.MovementSpeedModifierComponent>(c).WalkSpeedModifier;
            Assert.That(boosted / walk, Is.EqualTo(1.5f).Within(0.01f), "the adrenaline was not a x1.5 crawl.");
            Assert.That(Consc(c).LastConditionLine, Is.EqualTo(Loc.GetString("wolfmed-condition-adrenaline-end")));

            var delay = new Content.Shared._Goobstation.DoAfter.GetDoAfterDelayMultiplierEvent();
            SEntMan.EventBus.RaiseLocalEvent(c, delay);
            // Other multipliers apply as well; the Downed 1.5 is the only thing the adrenaline lifted.
            Assert.That(delay.Multiplier / delayed, Is.EqualTo(1.5f).Within(0.01f),
                "adrenaline did not lift exactly the Downed do-after penalty.");
        });

        // --- A machine: heavy chassis damage Downs it and never knocks it out (OD9). ---
        EntityUid ipc = default;
        await Server.WaitPost(() => ipc = SEntMan.SpawnEntity("MobIPC", map.GridCoords));
        await RunSeconds(2);
        await Server.WaitAssertion(() =>
        {
            foreach (var (part, _) in SEntMan.System<SharedBodySystem>().GetBodyChildren(ipc))
            {
                if (!SEntMan.TryGetComponent(part, out PainComponent? pain))
                    continue;

                pain.WoundPain = FixedPoint2.New(135);
                SEntMan.System<PainSystem>().SetPain((part, pain), FixedPoint2.New(135));
            }

            Assert.That(SEntMan.System<WolfmedConsciousnessSystem>().GetUncappedPain(ipc), Is.GreaterThan(189f),
                "the chassis never reached the faint line, so the test proves nothing.");
        });

        for (var second = 0; second < 30; second += 2)
        {
            await RunSeconds(2);
            await Server.WaitAssertion(() =>
            {
                Assert.That(Consc(ipc).State, Is.EqualTo(WolfmedConsciousness.Downed), "a machine was knocked out by pain.");
                Assert.That(Consc(ipc).Cause, Is.EqualTo(WolfmedCause.Pain));
            });
        }

        await Server.WaitAssertion(() =>
        {
            Assert.That(alerts.GetShownHealthAlert(ipc)?.Id, Is.EqualTo("WFWolfmedDownedFrame"));
            Assert.That(SEntMan.GetComponent<WolfmedSyntheticHudComponent>(ipc).CauseLine,
                Is.EqualTo("wolfmed-synthetic-cause-pain"), "the HUD did not say MOBILITY LOST: FRAME DAMAGE.");
        });
    }

    /// <summary>
    /// The per-encounter budget (plan §2.3): a 10-stack fire and a weapon hit every 2 s for 2 minutes. At
    /// most 40 s Critical, Downed for the rest once down; no faint over 20 s; none inside the cooldown (the
    /// shipped 50 s since M1b).
    /// </summary>
    [Test]
    public async Task SustainedFireFaintTest()
    {
        await PinPain();
        await OverrideCVar(Side.Server, WolfmedCVars.PainFaintCooldown, ShippedCooldown);
        var map = await Pair.CreateTestMap();
        var s = new WolfmedScenario(SEntMan);
        EntityUid a = default;

        await Server.WaitPost(() =>
        {
            s.SetAir(map.MapUid, true);
            a = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
        });
        await RunSeconds(2);
        await Server.WaitPost(() => SEntMan.System<FlammableSystem>().SetFireStacks(a, 10, ignite: true));

        var targets = new[]
        {
            TargetBodyPart.Torso, TargetBodyPart.LeftArm, TargetBodyPart.RightLeg,
            TargetBodyPart.RightArm, TargetBodyPart.LeftLeg, TargetBodyPart.Head,
        };

        var critical = 0f;
        var wentDown = false;
        float? faintStart = null;
        float? lastWake = null;
        TimeSpan? cooldownUntil = null;
        var faints = new List<(float Start, float Length)>();
        var causes = new HashSet<WolfmedCause>();

        // M1b: times are the server's clock. Every WaitPost and WaitAssertion runs ticks of its own, so counting
        // half-second loops ran about 6% slow and read a 50 s cooldown as 47 s.
        var t0 = 0f;
        var previous = 0f;
        await Server.WaitPost(() => t0 = previous = Now);
        for (var tick = 0; ; tick++)
        {
            var elapsed = 0f;
            await Server.WaitPost(() => elapsed = Now - t0);
            if (elapsed >= 120f)
                break;

            if (tick % 4 == 0)
            {
                var target = targets[tick / 4 % targets.Length];
                await Server.WaitPost(() =>
                    SEntMan.System<DamageableSystem>().TryChangeDamage(a, WolfmedScenario.Spec("Blunt", 6),
                        origin: null, targetPart: target));
            }

            await RunSeconds(0.5f);
            await Server.WaitAssertion(() =>
            {
                var t = Now - t0;
                var dt = Now - previous;
                previous = Now;
                var comp = Consc(a);
                if (comp.State == WolfmedConsciousness.Unconscious)
                {
                    critical += dt;
                    causes.Add(comp.Cause);
                    if (faintStart == null)
                    {
                        faintStart = t;
                        // M1b: the rule itself, on the server's clock, and the sampled gap within a poll.
                        if (cooldownUntil is { } until)
                            Assert.That(SGameTiming.CurTime, Is.GreaterThanOrEqualTo(until),
                                "a faint started inside the cooldown.");
                        if (lastWake is { } wake)
                            Assert.That(t - wake, Is.GreaterThanOrEqualTo(ShippedCooldown - 1f),
                                $"a faint started {t - wake:0.0} s after waking.");
                    }
                }
                else
                {
                    if (faintStart is { } start)
                    {
                        faints.Add((start, t - start));
                        lastWake = t;
                        cooldownUntil = comp.PainFaintCooldownUntil;
                        faintStart = null;
                    }

                    if (wentDown)
                        Assert.That(comp.State, Is.EqualTo(WolfmedConsciousness.Downed),
                            $"the burning, beaten patient stood up at {t} s.");
                }

                wentDown |= comp.State != WolfmedConsciousness.Up;
            });
        }

        TestContext.Out.WriteLine($"SustainedFireFaint: Critical {critical:0.0} s of 120; faints " +
                                  string.Join(", ", faints.Select(f => $"{f.Start:0.0}s+{f.Length:0.0}s")) +
                                  $"; causes while Critical: {string.Join(", ", causes)}.");

        Assert.Multiple(() =>
        {
            Assert.That(faints.Count + (faintStart != null ? 1 : 0), Is.GreaterThan(0), "the encounter never fainted.");
            // The budget is two 20 s faints. Each is seen from the sample after it starts to the one after the
            // half-second poll that ends it, so each can read up to a second long.
            Assert.That(faints.Count + (faintStart != null ? 1 : 0), Is.LessThanOrEqualTo(2),
                "more than two faints in two minutes.");
            Assert.That(critical, Is.LessThanOrEqualTo(2 * (FaintSeconds + 1f)), "more than 40 s helpless in two minutes.");
            Assert.That(faints.All(f => f.Length <= FaintSeconds + 1f), Is.True, "a faint ran past 20 s.");
            Assert.That(causes, Is.EquivalentTo(new[] { WolfmedCause.PainFaint }),
                "something other than a pain faint held the patient under.");
        });
    }

    /// <summary>
    /// P13: body pain is min(135, Σ parts) after every change, direct part edits included, and a direct set
    /// on the body is rederived; suppression and relief come off once, not once per part.
    /// </summary>
    [Test]
    public async Task BodyPainTracksPartsTest()
    {
        // Playtest 4: these sets carry no wound floor, so the loose rate would fade them mid-assertion; this test is
        // the P13 arithmetic, not the decay.
        await OverrideCVar(Side.Server, WolfmedCVars.PainLooseRecovery, 0f);
        await PinPain();
        var map = await Pair.CreateTestMap();
        EntityUid a = default;

        await Server.WaitPost(() => a = SEntMan.SpawnEntity("MobHuman", map.GridCoords));
        await RunSeconds(1);

        await Server.WaitAssertion(() =>
        {
            var pain = SEntMan.System<PainSystem>();
            var graph = SEntMan.System<SharedBodySystem>();
            var parts = graph.GetBodyChildren(a).ToList();
            var torso = parts.First(p => p.Component.PartType == BodyPartType.Torso).Id;
            var head = parts.First(p => p.Component.PartType == BodyPartType.Head).Id;
            var arm = parts.First(p => p.Component.PartType == BodyPartType.Arm).Id;

            float Sum() => parts.Sum(p => SEntMan.TryGetComponent(p.Id, out PainComponent? c) ? c.Value.Float() : 0f);
            void Check(string when)
            {
                var expected = MathF.Min(135f, Sum());
                Assert.That(pain.GetRawPain(a).Float(), Is.EqualTo(expected).Within(0.011f),
                    $"body pain drifted from the parts after {when}.");
            }

            pain.SetPain(torso, FixedPoint2.New(50));
            Check("a torso set");
            pain.SetPain(head, FixedPoint2.New(40));
            Check("a head set");
            pain.ChangePain(arm, FixedPoint2.New(60));
            Check("an arm change");
            pain.ChangePain(arm, FixedPoint2.New(-30));
            Check("an arm drop");
            pain.RecoverPain(head, 9f);
            Check("recovery");

            // A direct set on the body does not stick: the parts are the truth.
            pain.SetPain(a, FixedPoint2.New(5));
            Check("a direct body set");

            // Past the cap: the parts carry 149, the body 135.
            pain.SetPain(arm, FixedPoint2.New(60));
            Check("going past the cap");
            Assert.That(Sum(), Is.GreaterThan(135f));

            // Onyx suppression on the body comes off the parts once in total, not once per part.
            Assert.That(pain.SuppressPain(a, "BodyPainTracksPartsTest", 70, TimeSpan.FromSeconds(60)));
            var partsAfter = parts.Sum(p => SEntMan.TryGetComponent(p.Id, out PainComponent? c)
                ? pain.GetPain((p.Id, c)).Float()
                : 0f);
            Assert.That(partsAfter, Is.EqualTo(Sum() - 70f).Within(0.1f), "suppression was taken off more than once.");
            Assert.That(pain.GetPain(a).Float(), Is.EqualTo(135f - 70f).Within(0.011f));
            pain.ClearPainSuppression(a);

            // Wolfmed relief comes off the Downed reading once: (135 - 70) / 128.25.
            SEntMan.System<WolfmedPainReliefSystem>().AddDose(a, "opiate", WolfmedPainReliefTier.Strong, 70f,
                TimeSpan.FromSeconds(60), 0f);
            SEntMan.System<WolfmedConsciousnessSystem>().Refresh(a);
            Assert.That(Consc(a).DownLevel, Is.EqualTo((135f - 70f) / (135f * 0.95f)).Within(0.01f));
        });
    }

    /// <summary>
    /// Overlapping causes (plan §5.1-5.2): the cause, the blockers and the text that names them, with no
    /// promise of waking or standing while something else holds the body.
    /// </summary>
    [Test]
    public async Task OverlappingCausesTest()
    {
        await PinPain();
        var map = await Pair.CreateTestMap();
        var s = new WolfmedScenario(SEntMan);
        var alerts = SEntMan.System<WolfmedConditionAlertSystem>();
        EntityUid a = default;
        EntityUid b = default;

        await Server.WaitPost(() =>
        {
            s.SetAir(map.MapUid, true);
            a = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            b = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
        });
        await RunSeconds(2);

        // --- Pain plus bleeding: Downed by pain with blood at 52% after a dip below 50%. ---
        await Server.WaitAssertion(() =>
        {
            SetPain(a, BodyPartType.Torso, 129);
            Assert.That(Consc(a).Cause, Is.EqualTo(WolfmedCause.Pain));

            s.SetBlood(a, 0.49f);
            Assert.That(Consc(a).Cause, Is.EqualTo(WolfmedCause.Blood), "blood past its line did not take the cause.");
            Assert.That(Consc(a).Blockers, Is.EqualTo(WolfmedCauseFlags.Pain));

            s.SetBlood(a, 0.52f);
            var comp = Consc(a);
            var text = alerts.GetConditionText(a);
            Assert.Multiple(() =>
            {
                Assert.That(comp.State, Is.EqualTo(WolfmedConsciousness.Downed));
                Assert.That(comp.Cause, Is.EqualTo(WolfmedCause.Pain));
                Assert.That(comp.Blockers, Is.EqualTo(WolfmedCauseFlags.Blood), "blood in its leave band is not a blocker.");
                Assert.That(text, Does.Contain("A painkiller gets you moving, unless something else holds you"));
                Assert.That(text, Does.Contain("Also: blood loss"));
            });

            // An opiate does not stand them up until blood clears its leave line.
            SEntMan.System<WolfmedPainReliefSystem>().AddDose(a, "opiate", WolfmedPainReliefTier.Strong, 70f,
                TimeSpan.FromSeconds(120), 0f);
            s.Consciousness.Refresh(a);
            Assert.That(Consc(a).State, Is.EqualTo(WolfmedConsciousness.Downed), "an opiate stood up a patient held by blood.");
            Assert.That(Consc(a).Cause, Is.EqualTo(WolfmedCause.Blood));
            Assert.That(Consc(a).Blockers, Is.EqualTo(WolfmedCauseFlags.None));

            s.SetBlood(a, 0.56f);
            Consc(a).DownedUntil = TimeSpan.Zero;
            s.Consciousness.Refresh(a);
            Assert.That(Consc(a).State, Is.EqualTo(WolfmedConsciousness.Up), "blood past its leave line did not free them.");
        });

        // --- A pain faint at 40% blood, then bleeding to 34%, then an opiate. ---
        await Server.WaitAssertion(() =>
        {
            s.SetBlood(b, 0.40f);
            Assert.That(Consc(b).Cause, Is.EqualTo(WolfmedCause.Blood));
            SetPain(b, BodyPartType.Torso, 100);
            SetPain(b, BodyPartType.Head, 100);

            var comp = Consc(b);
            var text = alerts.GetConditionText(b);
            Assert.Multiple(() =>
            {
                Assert.That(comp.State, Is.EqualTo(WolfmedConsciousness.Unconscious));
                Assert.That(comp.Cause, Is.EqualTo(WolfmedCause.PainFaint));
                Assert.That(comp.Blockers, Is.EqualTo(WolfmedCauseFlags.Blood));
                Assert.That(text, Does.Contain(Loc.GetString("wolfmed-cause-pain-faint-help-blocked")));
                Assert.That(text, Does.Not.Contain(Loc.GetString("wolfmed-cause-pain-faint-help")),
                    "a blocked faint still promised coming round in a few seconds.");
                Assert.That(text, Does.Contain("Also: blood loss"));
            });

            s.SetBlood(b, 0.34f);
            Assert.That(Consc(b).Cause, Is.EqualTo(WolfmedCause.Blood));
            Assert.That(Consc(b).Blockers, Is.EqualTo(WolfmedCauseFlags.PainFaint));

            SEntMan.System<WolfmedPainReliefSystem>().AddDose(b, "opiate", WolfmedPainReliefTier.Strong, 70f,
                TimeSpan.FromSeconds(120), 0f);
            s.Consciousness.Refresh(b);
            comp = Consc(b);
            text = alerts.GetConditionText(b);
            Assert.Multiple(() =>
            {
                Assert.That(comp.State, Is.EqualTo(WolfmedConsciousness.Unconscious), "the opiate woke a bled-out patient.");
                Assert.That(comp.Cause, Is.EqualTo(WolfmedCause.Blood));
                Assert.That(comp.Blockers, Is.EqualTo(WolfmedCauseFlags.None));
                Assert.That(text, Does.Contain(Loc.GetString("wolfmed-cause-blood")));
                Assert.That(text, Does.Not.Contain("few seconds"), "the text still spoke of the faint.");
            });
        });

        // --- Sedation plus hypoxia on an airless tile. ---
        EntityUid c = default;
        await Server.WaitPost(() =>
        {
            s.SetAir(map.MapUid, false);
            c = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            // A sedating dose that holds sedation at 1.0 while it lasts. M2: a target past full, reached in 20 s.
            SEntMan.System<WolfmedPainReliefSystem>().AddDose(c, "overdose", WolfmedPainReliefTier.Strong, 0f,
                TimeSpan.FromSeconds(600), 1.5f);
        });
        await RunSeconds(22);

        await Server.WaitAssertion(() =>
        {
            Assert.That(Consc(c).State, Is.EqualTo(WolfmedConsciousness.Unconscious), "an overdose did not knock out.");
            Assert.That(Consc(c).Cause, Is.EqualTo(WolfmedCause.Sedation));
            Assert.That(s.Breathing.IsSuffocating(c), Is.True, "the airless tile is not suffocating the patient.");

            // Brain clock forward to the hypoxic line: both now hold the body, hypoxia names it.
            s.Advance(c, 100);
            var comp = Consc(c);
            Assert.That(comp.Cause, Is.EqualTo(WolfmedCause.Hypoxia), $"oxygenation {s.Life.GetOxygenation(c):0.00}, " +
                $"pressures {string.Join(", ", comp.Pressures.Select(p => $"{p.Key}={p.Value:R}"))}, state {comp.State}");
            Assert.That(comp.Blockers & WolfmedCauseFlags.Sedation, Is.EqualTo(WolfmedCauseFlags.Sedation));
            Assert.That(alerts.GetConditionText(c), Does.Contain("Also: " + Loc.GetString("wolfmed-cause-sedation")));
        });

        // Air back: the airway drain stops, the overdose's own drain does not, and the text names it.
        await Server.WaitPost(() => s.SetAir(map.MapUid, true));
        await RunSeconds(12);
        await Server.WaitAssertion(() =>
        {
            var comp = Consc(c);
            Assert.That(s.Breathing.IsSuffocating(c), Is.False, "air did not reach the patient.");
            Assert.That(comp.State, Is.EqualTo(WolfmedConsciousness.Unconscious), "air woke an overdosed patient.");
            var text = alerts.GetConditionText(c);
            Assert.That(text.Contains(Loc.GetString("wolfmed-cause-sedation")) ||
                        text.Contains(Loc.GetString("wolfmed-cause-hypoxia-source-sedation")), Is.True,
                $"the text does not name the overdose: {text}");
            Assert.That(comp.Cause == WolfmedCause.Sedation ||
                        comp.Cause == WolfmedCause.Hypoxia && comp.CauseSource == WolfmedCauseSource.Sedation, Is.True,
                $"with air back the cause is {comp.Cause} ({comp.CauseSource}), not the overdose.");

            // The dose ends; the patient wakes only as sedation falls.
            SEntMan.GetComponent<WolfmedPainReliefComponent>(c).Doses.Clear();
        });

        // Sedation falls at 0.035/s and stops draining the brain under 0.6 (about 11 s); the brain then
        // refills at 0.0042/s to the hypoxic leave line (derived: about a minute from where it is now).
        var woke = await WaitFor(() => Consc(c).State != WolfmedConsciousness.Unconscious, 150f);
        Assert.That(woke, Is.Not.Null, "the patient never woke as the sedation fell.");
        await Server.WaitAssertion(() =>
        {
            TestContext.Out.WriteLine($"OverlappingCauses: woke {woke} s after the dose ended.");
            Assert.That(SEntMan.TryGetComponent(c, out WolfmedPainReliefComponent? relief) ? relief.Sedation : 0f,
                Is.LessThan(0.6f), "the patient woke before the sedation fell.");
        });

        // --- M4: IPC power loss plus overheating (plan §12 M4, deferred from M1a). ---
        // The core past its 500 K line with the cell pulled: Cause CoreHeat, Blockers Shutdown (Power), Succumb offered.
        // Cooled under 450 K: Cause Shutdown (Power), Succumb removed, no text promising the IPC comes back Downed.
        await OverrideCVar(Side.Server, WolfmedCVars.IpcCoreHeatK, 500f);
        await OverrideCVar(Side.Server, WolfmedCVars.IpcCoreHeatWakeK, 450f);
        EntityUid ipc = default;
        EntityUid cell = default;
        await Server.WaitPost(() =>
        {
            ipc = SEntMan.SpawnEntity("MobIPC", map.GridCoords);
            var minds = SEntMan.System<SharedMindSystem>();
            minds.TransferTo(minds.CreateMind(null).Owner, ipc); // the charge loop runs only on a chassis with a mind
        });
        await RunSeconds(2);

        await Server.WaitAssertion(() =>
        {
            var overheat = SEntMan.System<WolfmedOverheatSystem>();
            SEntMan.GetComponent<TemperatureComponent>(ipc).CurrentTemperature = 900f;
            overheat.SetCoreTemperature(ipc, 600f);
            overheat.Tick(ipc, 1f);
            Assert.That(Consc(ipc).Cause, Is.EqualTo(WolfmedCause.CoreHeat), "a core past its line is not thermal shutdown.");

            var slots = SEntMan.System<ItemSlotsSystem>();
            Assert.That(slots.TryGetSlot(ipc, "cell_slot", out var slot), Is.True);
            cell = slot!.Item!.Value;
            Assert.That(SEntMan.System<SharedContainerSystem>().Remove(cell, slot.ContainerSlot!), Is.True);
        });
        await RunSeconds(2);

        await Server.WaitAssertion(() =>
        {
            var comp = Consc(ipc);
            Assert.Multiple(() =>
            {
                Assert.That(comp.State, Is.EqualTo(WolfmedConsciousness.Unconscious));
                Assert.That(comp.Cause, Is.EqualTo(WolfmedCause.CoreHeat), "the shutdown outranked thermal shutdown.");
                Assert.That(comp.Blockers & WolfmedCauseFlags.Shutdown, Is.EqualTo(WolfmedCauseFlags.Shutdown),
                    "the pulled cell is not a blocker.");
                Assert.That(SEntMan.GetComponent<WolfmedShutdownComponent>(ipc).Reason, Is.EqualTo(WolfmedCauseSource.Power));
                Assert.That(SEntMan.HasComponent<WolfmedDyingActionsComponent>(ipc), Is.True, "thermal shutdown offers no Succumb.");
                Assert.That(alerts.GetConditionText(ipc), Does.Contain("Also: shutdown"));
            });

            // Cooled under the wake line; the cell is still out.
            var overheat = SEntMan.System<WolfmedOverheatSystem>();
            SEntMan.GetComponent<TemperatureComponent>(ipc).CurrentTemperature = 300f;
            overheat.SetCoreTemperature(ipc, 440f);
            overheat.Tick(ipc, 1f);

            comp = Consc(ipc);
            var text = alerts.GetConditionText(ipc);
            Assert.Multiple(() =>
            {
                Assert.That(comp.State, Is.EqualTo(WolfmedConsciousness.Unconscious), "a cooled chassis with no cell came up.");
                Assert.That(comp.Cause, Is.EqualTo(WolfmedCause.Shutdown));
                Assert.That(comp.CauseSource, Is.EqualTo(WolfmedCauseSource.Power));
                Assert.That(SEntMan.HasComponent<WolfmedDyingActionsComponent>(ipc), Is.False, "Succumb outlived thermal shutdown.");
                Assert.That(comp.LastConditionLine, Does.Not.Contain("Downed").And.Not.Contain("online"),
                    $"the cooling line promised the chassis back: {comp.LastConditionLine}");
                Assert.That(text, Does.Not.Contain("Downed"), $"the condition text promised Downed: {text}");
            });

            var slots = SEntMan.System<ItemSlotsSystem>();
            Assert.That(slots.TryGetSlot(ipc, "cell_slot", out var slot), Is.True);
            Assert.That(SEntMan.System<SharedContainerSystem>().Insert(cell, slot!.ContainerSlot!), Is.True);
        });

        var back = await WaitFor(() => Consc(ipc).State != WolfmedConsciousness.Unconscious, 5f);
        Assert.That(back, Is.Not.Null, "the cell back did not bring the cooled chassis round.");
    }

    /// <summary>
    /// IPC shutdown (plan §3.11): a pulled cell shuts the chassis down with reason Power and the HUD says so;
    /// nothing kills it; the cell back brings it up within 2 s. A pulled pump is reason Pump; back in, up.
    /// Oil at 45% Downs it with cause Oil. And the chassis temperature in a 10-stack fire, for M4.
    /// </summary>
    [Test]
    public async Task IpcShutdownScenarioTest()
    {
        await PinPain();
        var map = await Pair.CreateTestMap();
        var s = new WolfmedScenario(SEntMan);
        var alerts = SEntMan.System<WolfmedConditionAlertSystem>();
        var slots = SEntMan.System<ItemSlotsSystem>();
        var containers = SEntMan.System<SharedContainerSystem>();
        EntityUid ipc = default;
        EntityUid cell = default;

        await Server.WaitPost(() =>
        {
            s.SetAir(map.MapUid, true);
            ipc = SEntMan.SpawnEntity("MobIPC", map.GridCoords);

            // The charge loop skips a chassis with no mind, so this one has one.
            var minds = SEntMan.System<SharedMindSystem>();
            minds.TransferTo(minds.CreateMind(null).Owner, ipc);
        });
        await RunSeconds(2);

        // --- The cell out. ---
        await Server.WaitAssertion(() =>
        {
            Assert.That(slots.TryGetSlot(ipc, "cell_slot", out var slot), Is.True, "the IPC has no cell slot.");
            cell = slot!.Item!.Value;
            Assert.That(containers.Remove(cell, slot.ContainerSlot!), Is.True);
        });
        await RunSeconds(2);

        await Server.WaitAssertion(() =>
        {
            var comp = Consc(ipc);
            Assert.Multiple(() =>
            {
                Assert.That(comp.State, Is.EqualTo(WolfmedConsciousness.Unconscious), "no cell, still running.");
                Assert.That(comp.Cause, Is.EqualTo(WolfmedCause.Shutdown));
                Assert.That(comp.CauseSource, Is.EqualTo(WolfmedCauseSource.Power));
                Assert.That(SEntMan.GetComponent<WolfmedShutdownComponent>(ipc).Reason, Is.EqualTo(WolfmedCauseSource.Power));
                Assert.That(SEntMan.GetComponent<WolfmedSyntheticHudComponent>(ipc).CauseLine,
                    Is.EqualTo("wolfmed-synthetic-cause-shutdown"));
                Assert.That(Loc.GetString("wolfmed-synthetic-cause-shutdown", ("source", comp.CauseSource.ToString())),
                    Does.Contain("CELL EMPTY"));
                Assert.That(alerts.GetShownHealthAlert(ipc)?.Id, Is.EqualTo("WFWolfmedOutShutdown"));
                Assert.That(alerts.GetTitle(ipc), Is.EqualTo("Shutdown: no power"));
                Assert.That(s.AnalyzerLines(ipc)[0], Is.EqualTo("SHUTDOWN: no power"), "M1a D: the analyzer line.");
                Assert.That(SEntMan.HasComponent<WolfmedSyntheticHudComponent>(ipc), Is.True,
                    "no synthetic readout, so the client heartbeat gate has nothing to read.");
            });

            // Thirty minutes on the life tick: nothing runs out on a shut-down chassis.
            s.Advance(ipc, 1800);
            Assert.That(SEntMan.System<MobStateSystem>().IsDead(ipc), Is.False, "a shutdown killed the chassis.");
        });

        await RunSeconds(10);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.System<MobStateSystem>().IsDead(ipc), Is.False, "a shutdown killed the chassis.");
            Assert.That(slots.TryGetSlot(ipc, "cell_slot", out var slot), Is.True);
            Assert.That(containers.Insert(cell, slot!.ContainerSlot!), Is.True);
        });

        var up = await WaitFor(() => Consc(ipc).State == WolfmedConsciousness.Up, 3f);
        Assert.That(up, Is.Not.Null, "the cell back did not bring the chassis up.");
        Assert.That(up!.Value, Is.LessThanOrEqualTo(2f * (1 + Band)), "the chassis took too long to come up.");

        // --- The pump out, then back in. ---
        EntityUid pump = default;
        EntityUid pumpPart = default;
        await Server.WaitAssertion(() =>
        {
            var graph = SEntMan.System<SharedBodySystem>();
            pump = graph.GetBodyOrgans(ipc).First(o => SEntMan.HasComponent<HeartComponent>(o.Id)).Id;
            Assert.That(containers.TryGetContainingContainer(pump, out var holder), Is.True);
            pumpPart = holder!.Owner;
            Assert.That(graph.RemoveOrgan(pump), Is.True);
            var comp = Consc(ipc);
            Assert.That(comp.Cause, Is.EqualTo(WolfmedCause.Shutdown));
            Assert.That(comp.CauseSource, Is.EqualTo(WolfmedCauseSource.Pump));
            Assert.That(alerts.GetTitle(ipc), Is.EqualTo("Shutdown: coolant pump offline"));
        });

        await RunSeconds(1);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Loc.GetString("wolfmed-synthetic-cause-shutdown", ("source", Consc(ipc).CauseSource.ToString())),
                Does.Contain("PUMP OFFLINE"));

            var graph = SEntMan.System<SharedBodySystem>();
            var slot = SEntMan.GetComponent<Content.Shared.Body.Organ.OrganComponent>(pump).SlotId;
            Assert.That(graph.InsertOrgan(pumpPart, pump, slot), Is.True, "the pump would not go back in.");
        });

        up = await WaitFor(() => Consc(ipc).State == WolfmedConsciousness.Up, 3f);
        Assert.That(up, Is.Not.Null, "the pump back did not bring the chassis up.");

        // --- Oil at 45%: Downed, cause Oil. ---
        await Server.WaitAssertion(() =>
        {
            s.SetBlood(ipc, 0.45f);
            Assert.That(Consc(ipc).State, Is.EqualTo(WolfmedConsciousness.Downed));
            Assert.That(Consc(ipc).Cause, Is.EqualTo(WolfmedCause.Oil));
            Assert.That(alerts.GetShownHealthAlert(ipc)?.Id, Is.EqualTo("WFWolfmedDownedOil"));
            s.SetBlood(ipc, 1f);
        });

        // --- Measurement for M4: chassis temperature in a 10-stack fire. ---
        EntityUid burning = default;
        await Server.WaitPost(() =>
        {
            burning = SEntMan.SpawnEntity("MobIPC", map.GridCoords);
        });
        await RunSeconds(1);
        await Server.WaitPost(() => SEntMan.System<FlammableSystem>().SetFireStacks(burning, 10, ignite: true));

        var trace = new List<string>();
        var above383 = 0;
        var above500 = 0;
        var peak = 0f;
        for (var second = 0; second <= 240; second += 5)
        {
            await Server.WaitPost(() =>
            {
                var kelvin = SEntMan.GetComponent<TemperatureComponent>(burning).CurrentTemperature;
                peak = MathF.Max(peak, kelvin);
                if (kelvin > 383f)
                    above383 += 5;
                if (kelvin > 500f)
                    above500 += 5;
                trace.Add($"{second}s {kelvin:0}K");
            });
            await RunSeconds(5);
        }

        TestContext.Out.WriteLine($"IpcFireMeasurement (M4 input): peak {peak:0} K, about {above383} s above 383 K, " +
                                  $"about {above500} s above 500 K. Trace: {string.Join(", ", trace)}");
        Assert.That(peak, Is.GreaterThan(Atmospherics293()), "the fire never heated the chassis.");
    }

    private static float Atmospherics293() => 293.15f;
}
