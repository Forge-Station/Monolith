#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
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
/// Playtest 5: "pain should rapidly fall if the wound isn't getting worse". A wound's pain floor settles to
/// wolfmed.pain_floor_rest of itself once the wound has not worsened for wolfmed.pain_floor_settle_seconds, and the
/// loose recovery takes the pain down to it; the wound getting worse puts the whole floor back. And wolfmed.pain_scale
/// scales every gain and floor.
/// </summary>
[TestFixture]
public sealed class WolfmedPainSettleTest : GameTest
{
    private EntityUid Part(EntityUid body, BodyPartType type) =>
        SEntMan.System<SharedBodySystem>().GetBodyChildren(body).First(p => p.Component.PartType == type).Id;

    [Test]
    public async Task FloorSettlesUntilTheWoundWorsensTest()
    {
        await OverrideCVar(Side.Server, WolfmedCVars.PainLooseRecovery, 3f);
        await OverrideCVar(Side.Server, WolfmedCVars.PainScale, 1f);
        await OverrideCVar(Side.Server, WolfmedCVars.PainFloorRest, 0.6f);
        await OverrideCVar(Side.Server, WolfmedCVars.PainFloorSettleSeconds, 20f);
        var map = await Pair.CreateTestMap();
        var s = new WolfmedScenario(SEntMan);
        EntityUid a = default;
        EntityUid wound = default;
        var floor = FixedPoint2.Zero;
        await Server.WaitPost(() =>
        {
            s.SetAir(map.MapUid, true);
            a = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
        });
        await RunSeconds(3);

        await Server.WaitAssertion(() =>
        {
            var torso = Part(a, BodyPartType.Torso);
            wound = SEntMan.System<WoundSystem>().CreateOrMergeWound(torso, "SlashWound", FixedPoint2.New(40))!.Value;
            var pain = SEntMan.GetComponent<PainComponent>(torso);
            floor = pain.WoundPain;
            Assert.That(floor, Is.GreaterThan(FixedPoint2.New(20)), "a 40 slash made no pain floor.");
            var settle = SEntMan.System<WolfmedBodyPainSystem>();
            Assert.That(settle.SettledFloor(torso, floor), Is.EqualTo(floor), "a fresh floor is not whole.");
            // The hit's own pain sits at the floor; from here only the floor can hold it up.
            SEntMan.System<PainSystem>().SetPain((torso, pain), floor);
        });

        await RunSeconds(25);
        await Server.WaitAssertion(() =>
        {
            var torso = Part(a, BodyPartType.Torso);
            var value = SEntMan.GetComponent<PainComponent>(torso).Value.Float();
            Assert.That(value, Is.EqualTo(floor.Float() * 0.6f).Within(0.5f),
                "a wound that did not get worse for 25 s still holds all its pain.");

            // Worse: the floor rises and is whole again.
            Assert.That(SEntMan.System<WoundSystem>().ChangeSeverity(wound, FixedPoint2.New(10)), Is.True);
            var pain = SEntMan.GetComponent<PainComponent>(torso);
            Assert.That(pain.WoundPain, Is.GreaterThan(floor), "a worse wound did not raise the floor.");
            Assert.That(SEntMan.System<WolfmedBodyPainSystem>().SettledFloor(torso, pain.WoundPain), Is.EqualTo(pain.WoundPain),
                "a floor that just rose is not whole.");
        });
    }

    [Test]
    public async Task PainScaleScalesEveryGainTest()
    {
        var map = await Pair.CreateTestMap();
        EntityUid whole = default, half = default;
        var wholeFloor = 0f;
        await OverrideCVar(Side.Server, WolfmedCVars.PainScale, 1f);
        await Server.WaitPost(() => whole = SEntMan.SpawnEntity("MobHuman", map.GridCoords));
        await RunSeconds(1);
        await Server.WaitAssertion(() =>
        {
            var torso = Part(whole, BodyPartType.Torso);
            SEntMan.System<WoundSystem>().CreateOrMergeWound(torso, "SlashWound", FixedPoint2.New(40));
            wholeFloor = SEntMan.GetComponent<PainComponent>(torso).WoundPain.Float();
            Assert.That(wholeFloor, Is.GreaterThan(10f), "a 40 slash made no pain floor at scale 1.");
        });

        await OverrideCVar(Side.Server, WolfmedCVars.PainScale, 0.5f);
        await Server.WaitPost(() => half = SEntMan.SpawnEntity("MobHuman", map.GridCoords));
        await RunSeconds(1);
        await Server.WaitAssertion(() =>
        {
            var ev = new ModifyPainGainEvent(1f);
            SEntMan.EventBus.RaiseLocalEvent(half, ref ev);
            Assert.That(ev.Multiplier, Is.EqualTo(0.5f).Within(0.001f), "wolfmed.pain_scale does not reach the pain gain.");

            var torso = Part(half, BodyPartType.Torso);
            SEntMan.System<WoundSystem>().CreateOrMergeWound(torso, "SlashWound", FixedPoint2.New(40));
            var floor = SEntMan.GetComponent<PainComponent>(torso).WoundPain.Float();
            Assert.That(floor, Is.EqualTo(wholeFloor * 0.5f).Within(0.1f), "the floor is not scaled.");
        });
    }
}
