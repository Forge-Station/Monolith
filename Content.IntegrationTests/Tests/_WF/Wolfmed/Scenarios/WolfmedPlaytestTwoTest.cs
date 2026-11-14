#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._WF.Wolfmed.Consciousness;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.Consciousness;
using Content.Shared._WF.Wolfmed.Reagents;
using Content.Shared.ActionBlocker;
using Content.Shared.Alert;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Movement.Components;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;

namespace Content.IntegrationTests.Tests._WF.Wolfmed.Scenarios;

/// <summary>
/// Playtest 2 (2026-09-23): the pain faint says when it ends, and burns never take the hands or the crawl away.
/// The 120 s fire itself is <see cref="WolfmedFireHelplessnessTest"/>.
/// </summary>
[TestFixture]
public sealed class WolfmedPlaytestTwoTest : GameTest
{
    private const float FaintSeconds = 20f;
    private const float CrawlFloor = 0.35f;

    private async Task Pin()
    {
        await OverrideCVar(Side.Server, WolfmedCVars.Consciousness, true);
        await OverrideCVar(Side.Server, WolfmedCVars.ConsciousnessPainDown, 0.95f);
        await OverrideCVar(Side.Server, WolfmedCVars.ConsciousnessPainOut, 1.4f);
        await OverrideCVar(Side.Server, WolfmedCVars.ConsciousnessBloodDown, 0.5f);
        await OverrideCVar(Side.Server, WolfmedCVars.ConsciousnessBloodOut, 0.35f);
        await OverrideCVar(Side.Server, WolfmedCVars.ConsciousnessHysteresis, 0.1f);
        await OverrideCVar(Side.Server, WolfmedCVars.PainFaintSeconds, FaintSeconds);
        await OverrideCVar(Side.Server, WolfmedCVars.PainFaintCooldown, 50f);
        await OverrideCVar(Side.Server, WolfmedCVars.AmbientPartCapFraction, 0.8f);
        await OverrideCVar(Side.Server, WolfmedCVars.CrawlFloor, CrawlFloor);
    }

    private WolfmedConsciousnessComponent Consc(EntityUid body) => SEntMan.GetComponent<WolfmedConsciousnessComponent>(body);

    private WoundDamageRoutingSystem Routing => SEntMan.System<WoundDamageRoutingSystem>();

    /// <summary>Pain on one part, with its wound floor at the same value so it does not recover away.</summary>
    private void SetPain(EntityUid body, BodyPartType type, float value)
    {
        var part = SEntMan.System<SharedBodySystem>().GetBodyChildren(body).First(p => p.Component.PartType == type).Id;
        var pain = SEntMan.GetComponent<PainComponent>(part);
        pain.WoundPain = FixedPoint2.New(value);
        SEntMan.System<PainSystem>().SetPain((part, pain), FixedPoint2.New(value));
    }

    /// <summary>The alert in the body's health slot, with its cooldown.</summary>
    private AlertState HealthAlert(EntityUid body)
    {
        Assert.That(SEntMan.System<AlertsSystem>().TryGetAlertState(body, AlertKey.ForCategory("Health"), out var state),
            Is.True, "the body shows no health alert.");
        return state;
    }

    private bool Enabled(EntityUid part) => SEntMan.GetComponent<BodyPartComponent>(part).Enabled;

    private int Hands(EntityUid body) => SEntMan.GetComponent<HandsComponent>(body).Hands.Count;

    private float Walk(EntityUid body) => SEntMan.GetComponent<MovementSpeedModifierComponent>(body).CurrentWalkSpeed;

    /// <summary>
    /// Item 1. The faint alert carries a countdown from the faint's start to <c>PainFaintUntil</c>; the condition
    /// text and the analyzer name the seconds; an opiate ends the faint and the countdown with it. A faint that
    /// something else holds shows no countdown and says what holds it instead.
    /// </summary>
    [Test]
    public async Task FaintCountdownTest()
    {
        await Pin();
        var map = await Pair.CreateTestMap();
        var s = new WolfmedScenario(SEntMan);
        var alerts = SEntMan.System<WolfmedConditionAlertSystem>();
        EntityUid a = default, b = default;

        await Server.WaitPost(() =>
        {
            a = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            b = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
        });
        await RunSeconds(2);

        await Server.WaitAssertion(() =>
        {
            SetPain(a, BodyPartType.Torso, 100);
            SetPain(a, BodyPartType.Head, 100);

            var comp = Consc(a);
            var alert = HealthAlert(a);
            Assert.Multiple(() =>
            {
                Assert.That(comp.Cause, Is.EqualTo(WolfmedCause.PainFaint));
                Assert.That(alert.Type.Id, Is.EqualTo("WFWolfmedFaintPain"));
                Assert.That(alert.Cooldown, Is.Not.Null, "the faint alert has no countdown.");
                Assert.That(alert.Cooldown!.Value.Item2, Is.EqualTo(comp.PainFaintUntil), "the countdown does not end with the faint.");
                Assert.That((alert.Cooldown.Value.Item2 - alert.Cooldown.Value.Item1).TotalSeconds,
                    Is.EqualTo(FaintSeconds).Within(0.01), "the countdown does not run the faint's length.");
                Assert.That(alert.ShowCooldown, Is.True);
                Assert.That(alerts.GetConditionText(a),
                    Does.Contain(Loc.GetString("wolfmed-cause-pain-faint-help-timed", ("seconds", (int) FaintSeconds))));
                Assert.That(s.AnalyzerLines(a)[0], Is.EqualTo($"FAINTED: pain, {(int) FaintSeconds} s"));
            });
        });

        await RunSeconds(5);
        await Server.WaitAssertion(() =>
        {
            var left = s.Consciousness.GetFaintSecondsLeft(a);
            Assert.That(left, Is.InRange(14, 16), "the faint's seconds did not count down.");
            Assert.That(alerts.GetConditionText(a),
                Does.Contain(Loc.GetString("wolfmed-cause-pain-faint-help-timed", ("seconds", left!.Value))));
            Assert.That(s.AnalyzerLines(a)[0], Is.EqualTo($"FAINTED: pain, {left} s"));

            // A strong painkiller ends the faint; the countdown goes with it.
            SEntMan.System<WolfmedPainReliefSystem>().AddDose(a, "opiate", WolfmedPainReliefTier.Strong, 70f,
                TimeSpan.FromSeconds(120), 0f);
            s.Consciousness.Refresh(a);

            var alert = HealthAlert(a);
            Assert.Multiple(() =>
            {
                Assert.That(Consc(a).State, Is.Not.EqualTo(WolfmedConsciousness.Unconscious), "the opiate did not end the faint.");
                Assert.That(alert.Type.Id, Is.Not.EqualTo("WFWolfmedFaintPain"));
                Assert.That(alert.Cooldown, Is.Null, "the countdown outlived the faint.");
                Assert.That(s.Consciousness.GetFaintWindow(a), Is.Null);
            });
        });

        // Blocked: a faint at 40% blood. No countdown; the text says what else holds them.
        await Server.WaitAssertion(() =>
        {
            s.SetBlood(b, 0.40f);
            SetPain(b, BodyPartType.Torso, 100);
            SetPain(b, BodyPartType.Head, 100);

            var comp = Consc(b);
            var alert = HealthAlert(b);
            var text = alerts.GetConditionText(b);
            Assert.Multiple(() =>
            {
                Assert.That(comp.Cause, Is.EqualTo(WolfmedCause.PainFaint));
                Assert.That(comp.Blockers, Is.EqualTo(WolfmedCauseFlags.Blood));
                Assert.That(alert.Type.Id, Is.EqualTo("WFWolfmedFaintPain"));
                Assert.That(alert.Cooldown, Is.Null, "a blocked faint promised waking with a countdown.");
                Assert.That(text, Does.Contain(Loc.GetString("wolfmed-cause-pain-faint-help-blocked")));
                Assert.That(text, Does.Not.Contain("Coming round in"));
                Assert.That(s.AnalyzerLines(b)[0], Does.Not.Contain(" s"), "a blocked faint's analyzer line counted down.");
            });
        });
    }

    /// <summary>
    /// Item 2, the hands. Burns no longer switch a limb off: an arm and a hand burned to their ceilings stay on,
    /// keep their hand, and are slower to use instead, which the patient is told once. A blow that breaks the
    /// other arm still switches it off (Shitmed's rule, unchanged for everything but burns).
    /// </summary>
    [Test]
    public async Task BurnsNeverSwitchALimbOffTest()
    {
        await Pin();
        // The pain of burns this deep faints a body; the faint is FaintCountdownTest's, not this test's.
        await OverrideCVar(Side.Server, WolfmedCVars.ConsciousnessPainOut, 0f);
        await OverrideCVar(Side.Server, WolfmedCVars.PainShockThreshold, 100000f);
        var map = await Pair.CreateTestMap();
        var s = new WolfmedScenario(SEntMan);
        EntityUid body = default, attacker = default, item = default;
        EntityUid leftArm = default, leftHand = default, rightArm = default;

        await Server.WaitPost(() =>
        {
            body = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            attacker = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            item = SEntMan.SpawnEntity("WFWolfmedAnalgesicPen", map.GridCoords);
        });
        await RunSeconds(2);

        await Server.WaitAssertion(() =>
        {
            leftArm = s.Part(body, BodyPartType.Arm, BodyPartSymmetry.Left);
            leftHand = s.Part(body, BodyPartType.Hand, BodyPartSymmetry.Left);
            rightArm = s.Part(body, BodyPartType.Arm, BodyPartSymmetry.Right);

            // Ambient Heat under the ceilings (168 arm, 144 hand since M3), far past Shitmed's 90.
            Routing.TryApplyPartDamage(body, leftArm, WolfmedScenario.Spec("Heat", 150));
            Routing.TryApplyPartDamage(body, leftHand, WolfmedScenario.Spec("Heat", 120));
        });

        // Going down knocks the body over for a moment; the stun has to pass before it can use anything.
        await RunSeconds(4);

        await Server.WaitAssertion(() =>
        {
            var hands = SEntMan.System<SharedHandsSystem>();
            var comp = SEntMan.GetComponent<HandsComponent>(body);
            var left = comp.Hands.Values.FirstOrDefault(h => h.Location == HandLocation.Left);
            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.GetComponent<Content.Shared.Damage.DamageableComponent>(leftArm).TotalDamage.Float(),
                    Is.GreaterThan(90f), "the arm never reached Shitmed's switch-off line.");
                Assert.That(Enabled(leftArm), Is.True, "burns switched the arm off.");
                Assert.That(Enabled(leftHand), Is.True, "burns switched the hand off.");
                Assert.That(Hands(body), Is.EqualTo(2), "a burned hand stopped being a hand.");
                Assert.That(left, Is.Not.Null);
            });

            Assert.That(hands.TryPickup(body, item, left!), Is.True, "the burned hand could not hold a pen.");
            var multiplier = SEntMan.System<FractureEffectSystem>().GetDurationMultiplier(body, item);
            Assert.That(multiplier, Is.GreaterThan(1f), "a badly burned hand works at full speed.");

            var line = Loc.GetString("wolfmed-limb-penalty-hands-burn");
            Assert.That(Consc(body).HandsPenaltyTold, Is.True, "the patient was never told about the hands.");
            Assert.That(SEntMan.System<WolfmedConditionAlertSystem>().GetConditionText(body), Does.Contain(line));
            TestContext.Out.WriteLine($"burned hand: duration x{multiplier:0.00}, state {Consc(body).State}");

            // Brute past 90 still switches a limb off, as before.
            Routing.TryApplyPartDamage(body, rightArm, WolfmedScenario.Spec("Blunt", 95), attacker);
        });
        await RunTicksSync(2);

        await Server.WaitAssertion(() =>
        {
            Assert.That(Enabled(rightArm), Is.False, "a broken arm stayed on.");
            Assert.That(Hands(body), Is.EqualTo(1), "the broken arm's hand kept working.");
        });
    }

    /// <summary>
    /// Item 2, the crawl. Downed with badly hurt legs: never under the floor. Both legs switched off: exactly the
    /// floor, on the arms. Both arms off too: no crawl. Faints and the pain shock are pinned off so the body stays
    /// Downed and no adrenaline moves the numbers.
    /// </summary>
    [Test]
    public async Task CrawlFloorTest()
    {
        await Pin();
        await OverrideCVar(Side.Server, WolfmedCVars.ConsciousnessPainOut, 0f);
        await OverrideCVar(Side.Server, WolfmedCVars.PainShockThreshold, 100000f);
        var map = await Pair.CreateTestMap();
        var s = new WolfmedScenario(SEntMan);
        EntityUid body = default, attacker = default;
        var normal = 0f;
        var floor = 0f;

        await Server.WaitPost(() =>
        {
            body = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            attacker = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
        });
        await RunSeconds(2);

        await Server.WaitPost(() =>
        {
            normal = Walk(body);
            floor = CrawlFloor * SEntMan.GetComponent<Content.Shared._White.Standing.LayingDownComponent>(body).SpeedModify *
                    MovementSpeedModifierComponent.DefaultBaseWalkSpeed;
            SetPain(body, BodyPartType.Torso, 129);
            foreach (var symmetry in new[] { BodyPartSymmetry.Left, BodyPartSymmetry.Right })
            {
                Routing.TryApplyPartDamage(body, s.Part(body, BodyPartType.Leg, symmetry), WolfmedScenario.Spec("Blunt", 85), attacker);
                Routing.TryApplyPartDamage(body, s.Part(body, BodyPartType.Foot, symmetry), WolfmedScenario.Spec("Blunt", 60), attacker);
            }
        });
        await RunSeconds(4);

        await Server.WaitAssertion(() =>
        {
            TestContext.Out.WriteLine($"normal walk {normal:0.000}, floor {floor:0.000}; hurt legs {Walk(body):0.000}");
            Assert.Multiple(() =>
            {
                Assert.That(Consc(body).State, Is.EqualTo(WolfmedConsciousness.Downed));
                Assert.That(Walk(body), Is.GreaterThanOrEqualTo(floor * 0.99f), "hurt legs took the crawl under the floor.");
                Assert.That(SEntMan.System<ActionBlockerSystem>().CanMove(body), Is.True);
            });

            foreach (var symmetry in new[] { BodyPartSymmetry.Left, BodyPartSymmetry.Right })
                Routing.TryApplyPartDamage(body, s.Part(body, BodyPartType.Leg, symmetry), WolfmedScenario.Spec("Blunt", 10), attacker);
        });
        await RunSeconds(3);

        await Server.WaitAssertion(() =>
        {
            TestContext.Out.WriteLine($"legs off: walk {Walk(body):0.000}");
            Assert.Multiple(() =>
            {
                Assert.That(Enabled(s.Part(body, BodyPartType.Leg, BodyPartSymmetry.Left)), Is.False, "the legs never switched off.");
                Assert.That(Enabled(s.Part(body, BodyPartType.Leg, BodyPartSymmetry.Right)), Is.False);
                Assert.That(Consc(body).State, Is.EqualTo(WolfmedConsciousness.Downed));
                Assert.That(Walk(body), Is.EqualTo(floor).Within(0.01f), "a legless body does not drag itself at the floor.");
                Assert.That(SEntMan.System<ActionBlockerSystem>().CanMove(body), Is.True);
            });

            foreach (var symmetry in new[] { BodyPartSymmetry.Left, BodyPartSymmetry.Right })
                Routing.TryApplyPartDamage(body, s.Part(body, BodyPartType.Arm, symmetry), WolfmedScenario.Spec("Blunt", 95), attacker);
        });
        await RunTicksSync(3);

        await Server.WaitAssertion(() =>
        {
            TestContext.Out.WriteLine($"legs and arms off: walk {Walk(body):0.000}");
            Assert.That(Walk(body), Is.EqualTo(0f).Within(0.001f), "a body with no working limb still crawls.");
        });
    }
}
