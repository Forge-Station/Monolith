#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.Consciousness;
using Content.Shared._WF.Wolfmed.Reagents;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry.Components;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Content.IntegrationTests.Fixtures.Attributes;

namespace Content.IntegrationTests.Tests._WF.Wolfmed.Scenarios;

/// <summary>
/// Playtest 1: painkillers the patient can feel. A swallowed dose takes hold within about ten seconds and says
/// so; the analgesic and opiate pens work on yourself while Downed, lift a pain-Downed body inside their tier's
/// promise and never lift a blood-Downed one.
/// </summary>
[TestFixture]
[TestOf(typeof(WolfmedPainReliefSystem))]
public sealed class WolfmedPainkillerTest : GameTest
{
    /// <summary>The plan's lines and the absorption delay, pinned.</summary>
    private async Task Pin()
    {
        await OverrideCVar(Side.Server, WolfmedCVars.Consciousness, true);
        await OverrideCVar(Side.Server, WolfmedCVars.ConsciousnessPainDown, 0.95f);
        await OverrideCVar(Side.Server, WolfmedCVars.ConsciousnessPainOut, 1.4f);
        await OverrideCVar(Side.Server, WolfmedCVars.ConsciousnessBloodDown, 0.5f);
        await OverrideCVar(Side.Server, WolfmedCVars.ConsciousnessBloodOut, 0.35f);
        await OverrideCVar(Side.Server, WolfmedCVars.ConsciousnessHysteresis, 0.1f);
        await OverrideCVar(Side.Server, WolfmedCVars.PainFaintSeconds, 20f);
        await OverrideCVar(Side.Server, WolfmedCVars.PainFaintCooldown, 30f);
        await OverrideCVar(Side.Server, WolfmedCVars.PainkillerAbsorbSeconds, 4f);
    }

    /// <summary>Pain on one part, with its wound floor at the same value so it does not recover away.</summary>
    private void SetPain(EntityUid body, BodyPartType type, float value)
    {
        var part = SEntMan.System<SharedBodySystem>().GetBodyChildren(body).First(p => p.Component.PartType == type).Id;
        var pain = SEntMan.GetComponent<PainComponent>(part);
        pain.WoundPain = FixedPoint2.New(value);
        SEntMan.System<PainSystem>().SetPain((part, pain), FixedPoint2.New(value));
    }

    private WolfmedConsciousnessComponent Consc(EntityUid body) => SEntMan.GetComponent<WolfmedConsciousnessComponent>(body);

    private WolfmedPainReliefTier Tier(EntityUid body) => SEntMan.System<WolfmedPainReliefSystem>().GetTier(body);

    private void Swallow(EntityUid body, string reagent, float units)
    {
        var stomach = SEntMan.System<SharedBodySystem>().GetBodyOrgans(body)
            .First(organ => SEntMan.HasComponent<StomachComponent>(organ.Id)).Id;
        Assert.That(SEntMan.System<StomachSystem>().TryTransferSolution(stomach, new Solution(reagent, FixedPoint2.New(units))),
            Is.True, "the dose did not go down.");
    }

    /// <summary>Seconds, polled each half second, until the condition holds; null past the limit.</summary>
    private async Task<float?> WaitFor(Func<bool> condition, float limit)
    {
        for (var waited = 0f; waited <= limit; waited += 0.5f)
        {
            var met = false;
            await Server.WaitPost(() => met = condition());
            if (met)
                return waited;

            await RunSeconds(0.5f);
        }

        return null;
    }

    /// <summary>
    /// A pill and a swig. The analgesic pill stands a pain-Downed patient up, the opiate ends a pain faint, both
    /// inside ten seconds of swallowing, and the patient is told the dose took hold. Records the sedation a
    /// swallowed 5 u of opiate costs.
    /// </summary>
    [Test]
    public async Task OralPainkillerTakesHoldTest()
    {
        await Pin();
        var map = await Pair.CreateTestMap();
        EntityUid pill = default, swig = default;

        await Server.WaitPost(() =>
        {
            pill = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            swig = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
        });
        await RunSeconds(2);

        await Server.WaitAssertion(() =>
        {
            SetPain(pill, BodyPartType.Torso, 129);
            SetPain(swig, BodyPartType.Torso, 100);
            SetPain(swig, BodyPartType.Head, 100);
            Assert.That(Consc(pill).Cause, Is.EqualTo(WolfmedCause.Pain));
            Assert.That(Consc(swig).Cause, Is.EqualTo(WolfmedCause.PainFaint));

            Swallow(pill, "WFWolfmedAnalgesic", 15);
            Swallow(swig, "WFWolfmedOpiate", 5);
        });

        var pillTier = await WaitFor(() => Tier(pill) >= WolfmedPainReliefTier.Weak, 15f);
        var pillUp = await WaitFor(() => Consc(pill).State == WolfmedConsciousness.Up, 15f);
        var swigUp = await WaitFor(() => Consc(swig).State == WolfmedConsciousness.Up, 15f);

        var peak = 0f;
        for (var second = 0; second < 60; second++)
        {
            await RunSeconds(1);
            await Server.WaitPost(() => peak = MathF.Max(peak, SEntMan.System<WolfmedPainReliefSystem>().GetSedation(swig)));
        }

        TestContext.Out.WriteLine($"analgesic pill: tier at {pillTier} s, up at {pillUp} s after swallowing; " +
                                  $"5 u opiate: up by {swigUp} s after the pill's wait; peak sedation {peak:P0}.");
        Assert.Multiple(() =>
        {
            Assert.That(pillTier, Is.Not.Null.And.LessThanOrEqualTo(10f), "the pill took more than ten seconds to take hold.");
            Assert.That(pillUp, Is.Not.Null, "a weak painkiller did not stand a pain-Downed patient up.");
            Assert.That(pillTier + pillUp, Is.LessThanOrEqualTo(10f));
            Assert.That(swigUp, Is.Not.Null, "the opiate did not end the faint.");
            Assert.That(pillTier + pillUp + swigUp, Is.LessThanOrEqualTo(10f), "the opiate took more than ten seconds.");
        });
    }

    /// <summary>
    /// Each pen, on yourself while Downed: it injects, lifts a pain-Downed body within a few seconds and never
    /// lifts a blood-Downed one. The patient hears the dose take hold and wear off. The opiate pen stays under
    /// the sedation line that depresses breathing.
    /// </summary>
    [TestCase("WFWolfmedAnalgesicPen", WolfmedPainReliefTier.Weak)]
    [TestCase("WFWolfmedOpiatePen", WolfmedPainReliefTier.Strong)]
    public async Task PainkillerPenTest(string pen, WolfmedPainReliefTier tier)
    {
        await Pin();
        var map = await Pair.CreateTestMap();
        EntityUid pained = default, bled = default, penA = default, penB = default;

        await Server.WaitPost(() =>
        {
            pained = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            bled = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            penA = SEntMan.SpawnEntity(pen, map.GridCoords);
            penB = SEntMan.SpawnEntity(pen, map.GridCoords);
        });
        await RunSeconds(2);

        await Server.WaitAssertion(() =>
        {
            SetPain(pained, BodyPartType.Torso, 129);
            new WolfmedScenario(SEntMan).SetBlood(bled, 0.45f);
            Assert.That(Consc(pained).Cause, Is.EqualTo(WolfmedCause.Pain));
            Assert.That(Consc(bled).Cause, Is.EqualTo(WolfmedCause.Blood));
        });

        // Going down knocks the body over for a moment; the stun has to pass before it can use anything.
        await RunSeconds(4);

        await Server.WaitAssertion(() =>
        {
            var hands = SEntMan.System<SharedHandsSystem>();
            var interaction = SEntMan.System<SharedInteractionSystem>();
            foreach (var (body, item) in new[] { (pained, penA), (bled, penB) })
            {
                Assert.That(Consc(body).State, Is.EqualTo(WolfmedConsciousness.Downed));
                Assert.That(hands.TryPickupAnyHand(body, item), Is.True, "a Downed body could not pick the pen up.");
                interaction.UseInHandInteraction(body, item);
            }
        });

        var lifted = await WaitFor(() => Consc(pained).State == WolfmedConsciousness.Up, 5f);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Tier(bled), Is.EqualTo(tier), "the pen did not inject its user.");
            Assert.That(Consc(bled).LastConditionLine,
                Is.EqualTo(Loc.GetString($"wolfmed-painkiller-takes-hold-{tier.ToString().ToLowerInvariant()}")),
                "the patient was not told the dose took hold.");
        });

        var peak = 0f;
        float? wornOff = null;
        for (var second = 1; second <= 120 && wornOff == null; second++)
        {
            await RunSeconds(1);
            var now = second;
            await Server.WaitAssertion(() =>
            {
                // Held at 45%: the blood coming back on its own is not the painkiller's doing.
                new WolfmedScenario(SEntMan).SetBlood(bled, 0.45f);
                Assert.That(Consc(bled).State, Is.Not.EqualTo(WolfmedConsciousness.Up), "a painkiller lifted a blood-Downed body.");
                peak = MathF.Max(peak, SEntMan.System<WolfmedPainReliefSystem>().GetSedation(bled));
                if (Tier(bled) == WolfmedPainReliefTier.None)
                    wornOff = now;
            });
        }

        TestContext.Out.WriteLine($"{pen}: pain-Downed up after {lifted} s; the dose lasted {wornOff} s; peak sedation {peak:P0}.");
        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(lifted, Is.Not.Null, "the pen did not lift a pain-Downed body.");
                Assert.That(lifted, Is.LessThanOrEqualTo(3f), "the pen took more than three seconds.");
                Assert.That(wornOff, Is.Not.Null.And.GreaterThanOrEqualTo(20), "the dose wore off inside twenty seconds.");
                Assert.That(Consc(bled).LastConditionLine, Is.EqualTo(Loc.GetString("wolfmed-painkiller-worn-off")));
                Assert.That(peak, Is.LessThan(0.6f), "one pen depresses breathing.");
                Assert.That(Consc(bled).Cause, Is.EqualTo(WolfmedCause.Blood));
            });
        });
    }
}
