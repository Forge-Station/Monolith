#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._WF.Wolfmed.Consciousness;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.Consciousness;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.FixedPoint;
using NUnit.Framework;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Wolfmed.Scenarios;

/// <summary>
/// Playtest 4: a limb holds less pain than the body (wolfmed.part_pain_cap_*), so bullets into one arm can no longer
/// reach the Downed line on their own; limbs together still add up to Downed and the faint, and the torso alone still
/// reaches the body's cap.
/// </summary>
[TestFixture]
[TestOf(typeof(WolfmedBodyPainSystem))]
public sealed class WolfmedPartPainCapTest : GameTest
{
    private void SetPain(EntityUid body, BodyPartType type, float value, BodyPartSymmetry symmetry = BodyPartSymmetry.None)
    {
        var part = SEntMan.System<SharedBodySystem>().GetBodyChildren(body)
            .First(p => p.Component.PartType == type && p.Component.Symmetry == symmetry).Id;
        var pain = SEntMan.GetComponent<PainComponent>(part);
        pain.WoundPain = FixedPoint2.New(value);
        SEntMan.System<PainSystem>().SetPain((part, pain), FixedPoint2.New(value));
    }

    private float PartPain(EntityUid body, BodyPartType type, BodyPartSymmetry symmetry = BodyPartSymmetry.None)
    {
        var part = SEntMan.System<SharedBodySystem>().GetBodyChildren(body)
            .First(p => p.Component.PartType == type && p.Component.Symmetry == symmetry).Id;
        return SEntMan.GetComponent<PainComponent>(part).Value.Float();
    }

    private WolfmedConsciousness State(EntityUid body) =>
        SEntMan.GetComponent<WolfmedConsciousnessComponent>(body).State;

    [Test]
    public async Task OneArmCannotDownYouTest()
    {
        await OverrideCVar(Side.Server, WolfmedCVars.Consciousness, true);
        await OverrideCVar(Side.Server, WolfmedCVars.ConsciousnessPainDown, 0.95f);
        await OverrideCVar(Side.Server, WolfmedCVars.ConsciousnessPainOut, 1.4f);
        await OverrideCVar(Side.Server, WolfmedCVars.PartPainCapArm, 80f);
        await OverrideCVar(Side.Server, WolfmedCVars.PartPainCapHand, 50f);
        await OverrideCVar(Side.Server, WolfmedCVars.PartPainCapLeg, 90f);
        await OverrideCVar(Side.Server, WolfmedCVars.PartPainCapFoot, 50f);

        var map = await Pair.CreateTestMap();
        var s = new WolfmedScenario(SEntMan);
        EntityUid a = default;
        await Server.WaitPost(() =>
        {
            s.SetAir(map.MapUid, true);
            a = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
        });
        await RunSeconds(3);

        // The body's cap on one arm: the part holds 80, the body reads 80, nobody falls over.
        await Server.WaitPost(() => SetPain(a, BodyPartType.Arm, 135, BodyPartSymmetry.Left));
        await RunSeconds(2);
        await Server.WaitAssertion(() =>
        {
            Assert.That(PartPain(a, BodyPartType.Arm, BodyPartSymmetry.Left), Is.EqualTo(80f).Within(0.01f), "the arm was not capped at 80.");
            Assert.That(SEntMan.GetComponent<PainComponent>(a).Value.Float(), Is.EqualTo(80f).Within(0.01f), "body pain is not the arm's 80.");
            Assert.That(State(a), Is.EqualTo(WolfmedConsciousness.Up), "one arm Downed the patient.");
        });

        // A leg on top: 80 + 90 = 170 summed, 135 effective, past the 128 Downed line and under the 189 faint.
        await Server.WaitPost(() => SetPain(a, BodyPartType.Leg, 135, BodyPartSymmetry.Left));
        await RunSeconds(2);
        await Server.WaitAssertion(() =>
        {
            Assert.That(PartPain(a, BodyPartType.Leg, BodyPartSymmetry.Left), Is.EqualTo(90f).Within(0.01f), "the leg was not capped at 90.");
            Assert.That(State(a), Is.EqualTo(WolfmedConsciousness.Downed), "an arm and a leg did not Down the patient.");
        });

        // A hand and a foot on top: 270 summed, past the faint line.
        await Server.WaitPost(() =>
        {
            SetPain(a, BodyPartType.Hand, 135, BodyPartSymmetry.Left);
            SetPain(a, BodyPartType.Foot, 135, BodyPartSymmetry.Left);
        });
        await RunSeconds(2);
        await Server.WaitAssertion(() =>
        {
            Assert.That(PartPain(a, BodyPartType.Hand, BodyPartSymmetry.Left), Is.EqualTo(50f).Within(0.01f), "the hand was not capped at 50.");
            Assert.That(PartPain(a, BodyPartType.Foot, BodyPartSymmetry.Left), Is.EqualTo(50f).Within(0.01f), "the foot was not capped at 50.");
            Assert.That(State(a), Is.EqualTo(WolfmedConsciousness.Unconscious), "four limbs at their caps did not faint the patient.");
        });
    }

    [Test]
    public async Task TorsoKeepsTheBodyCapTest()
    {
        await OverrideCVar(Side.Server, WolfmedCVars.Consciousness, true);
        await OverrideCVar(Side.Server, WolfmedCVars.ConsciousnessPainDown, 0.95f);
        await OverrideCVar(Side.Server, WolfmedCVars.PartPainCapArm, 80f);

        var map = await Pair.CreateTestMap();
        var s = new WolfmedScenario(SEntMan);
        EntityUid a = default;
        await Server.WaitPost(() =>
        {
            s.SetAir(map.MapUid, true);
            a = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
        });
        await RunSeconds(3);

        await Server.WaitPost(() => SetPain(a, BodyPartType.Torso, 135));
        await RunSeconds(2);
        await Server.WaitAssertion(() =>
        {
            Assert.That(PartPain(a, BodyPartType.Torso), Is.EqualTo(135f).Within(0.01f), "the torso lost its cap.");
            Assert.That(State(a), Is.EqualTo(WolfmedConsciousness.Downed), "a torso at the cap did not Down the patient.");
        });
    }
}
