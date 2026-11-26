#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using NUnit.Framework;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Wolfmed.Scenarios;

/// <summary>
/// Playtest 4: a fresh stump hurts. The lost part took its pain with it, so the part that keeps the stump gets a
/// one-time spike from the open stump (half its severity) and a floor from the untreated amputation (its severity).
/// </summary>
[TestFixture]
public sealed class WolfmedStumpPainTest : GameTest
{
    [Test]
    public async Task LosingAnArmHurtsTest()
    {
        await OverrideCVar(Side.Server, WolfmedCVars.PainScale, 1f); // playtest 5: this test pins Onyx's figures
        await OverrideCVar(Side.Server, WolfmedCVars.PainFloorRest, 1f); // playtest 5: and the floor holding whole

        var map = await Pair.CreateTestMap();
        var s = new WolfmedScenario(SEntMan);
        EntityUid a = default;
        await Server.WaitPost(() =>
        {
            s.SetAir(map.MapUid, true);
            a = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
        });
        await RunSeconds(3);

        await Server.WaitAssertion(() =>
        {
            var arm = s.Part(a, BodyPartType.Arm, BodyPartSymmetry.Left);
            Assert.That(SEntMan.System<AmputationSystem>().TryAmputate(a, arm), Is.True, "the arm did not come off.");
        });
        await RunSeconds(1);

        float spike = 0f;
        await Server.WaitAssertion(() =>
        {
            var torso = s.Part(a, BodyPartType.Torso);
            var pain = SEntMan.GetComponent<PainComponent>(torso);
            spike = pain.Value.Float();
            // The stump spike is its severity, 120, capped at the torso's 135; the loose rate has had a second at it.
            Assert.That(spike, Is.GreaterThan(100f), "a fresh arm stump did not hurt.");
            Assert.That(pain.WoundPain.Float(), Is.EqualTo(35f).Within(0.5f), "the untreated amputation left no pain floor.");
        });

        await RunSeconds(50);
        await Server.WaitAssertion(() =>
        {
            var torso = s.Part(a, BodyPartType.Torso);
            var pain = SEntMan.GetComponent<PainComponent>(torso);
            Assert.That(pain.Value.Float(), Is.EqualTo(35f).Within(0.5f), "the spike did not fade to the stump's floor.");
        });
    }
}
