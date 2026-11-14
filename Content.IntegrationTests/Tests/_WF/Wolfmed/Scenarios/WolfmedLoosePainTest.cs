#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.FixedPoint;
using NUnit.Framework;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Wolfmed.Scenarios;

/// <summary>
/// Playtest 4: pain that no open wound backs fades at wolfmed.pain_loose_recovery a second (Onyx's one ninth kept
/// a treated wound hurting for minutes), while the wound floor holds for as long as the wound is open.
/// </summary>
[TestFixture]
public sealed class WolfmedLoosePainTest : GameTest
{
    private EntityUid Part(EntityUid body, BodyPartType type, BodyPartSymmetry symmetry) =>
        SEntMan.System<SharedBodySystem>().GetBodyChildren(body)
            .First(p => p.Component.PartType == type && p.Component.Symmetry == symmetry).Id;

    [Test]
    public async Task TreatedPainFadesFastTest()
    {
        await OverrideCVar(Side.Server, WolfmedCVars.PainLooseRecovery, 3f);
        var map = await Pair.CreateTestMap();
        var s = new WolfmedScenario(SEntMan);
        EntityUid a = default;
        await Server.WaitPost(() =>
        {
            s.SetAir(map.MapUid, true);
            a = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
        });
        await RunSeconds(3);

        // An open wound worth 60 pain, hit for 100: the 40 over the floor goes in seconds, the floor stays.
        await Server.WaitPost(() =>
        {
            var arm = Part(a, BodyPartType.Torso, BodyPartSymmetry.None);
            var pain = SEntMan.GetComponent<PainComponent>(arm);
            pain.WoundPain = FixedPoint2.New(60);
            SEntMan.System<PainSystem>().SetPain((arm, pain), FixedPoint2.New(100));
        });
        await RunSeconds(20);
        await Server.WaitAssertion(() =>
        {
            var value = SEntMan.GetComponent<PainComponent>(Part(a, BodyPartType.Torso, BodyPartSymmetry.None)).Value.Float();
            Assert.That(value, Is.EqualTo(60f).Within(0.5f), "pain over the wound floor did not fade to the floor in 20 s, or the floor gave way.");
        });

        // The wound is treated: the floor goes, and the rest fades within half a minute.
        await Server.WaitPost(() =>
        {
            var arm = Part(a, BodyPartType.Torso, BodyPartSymmetry.None);
            SEntMan.GetComponent<PainComponent>(arm).WoundPain = FixedPoint2.Zero;
        });
        await RunSeconds(25);
        await Server.WaitAssertion(() =>
        {
            var value = SEntMan.GetComponent<PainComponent>(Part(a, BodyPartType.Torso, BodyPartSymmetry.None)).Value.Float();
            Assert.That(value, Is.LessThan(1f), "treated pain did not fade within 25 s.");
        });
    }
}
