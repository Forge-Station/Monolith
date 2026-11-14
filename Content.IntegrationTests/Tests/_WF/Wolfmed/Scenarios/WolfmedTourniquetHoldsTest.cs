#nullable enable
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Shared._Onyx.Medical.Tourniquet;
using Content.Shared._Onyx.Wounds;
using Content.Shared.Body.Part;
using Content.Shared.Damage;
using NUnit.Framework;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Wolfmed.Scenarios;

/// <summary>
/// Playtest 4: a tourniquet holds. The strap's own Blunt landed right after the clamp and, like any damage that
/// worsens a wound, cleared its treatment, so the leg kept spurting. Now a clamp under a tourniquet survives new
/// damage, a bleed that opens under one starts clamped, and the strap on a leg ties off the foot as well.
/// </summary>
[TestFixture]
public sealed class WolfmedTourniquetHoldsTest : GameTest
{
    private WoundDamageRoutingSystem Routing => SEntMan.System<WoundDamageRoutingSystem>();
    private static DamageSpecifier Spec(string type, float amount) => WolfmedScenario.Spec(type, amount);

    [Test]
    public async Task TourniquetSurvivesNewDamageAndCoversTheFootTest()
    {
        var map = await Pair.CreateTestMap();
        var s = new WolfmedScenario(SEntMan);
        EntityUid a = default, attacker = default;
        await Server.WaitPost(() =>
        {
            s.SetAir(map.MapUid, true);
            a = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            attacker = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
        });
        await RunSeconds(2);

        var bleeding = SEntMan.System<WoundBleedingSystem>();
        await Server.WaitAssertion(() =>
        {
            var leg = s.Part(a, BodyPartType.Leg, BodyPartSymmetry.Right);
            Routing.TryApplyPartDamage(a, leg, Spec("Slash", 40), attacker);
            Assert.That(bleeding.GetPartRate(leg), Is.GreaterThan(0f), "a 40 slash did not bleed.");

            Assert.That(SEntMan.System<TourniquetSystem>().Apply(a, leg), Is.True, "the tourniquet did not go on.");
            Assert.That(bleeding.GetPartRate(leg), Is.EqualTo(0f), "the tourniquet did not stop the leg.");

            // The strap's own hurt, and any later hit: the clamp stays.
            Routing.TryApplyPartDamage(a, leg, Spec("Blunt", 5), attacker);
            Routing.TryApplyPartDamage(a, leg, Spec("Slash", 20), attacker);
            Assert.That(bleeding.GetPartRate(leg), Is.EqualTo(0f), "new damage undid the tourniquet.");

            // Below the strap: a foot cut under a tied leg is tied off too.
            var foot = s.Part(a, BodyPartType.Foot, BodyPartSymmetry.Right);
            Routing.TryApplyPartDamage(a, foot, Spec("Slash", 30), attacker);
            Assert.That(bleeding.GetPartRate(foot), Is.EqualTo(0f), "the foot bled under a tied leg.");

            // The other leg is not.
            var other = s.Part(a, BodyPartType.Leg, BodyPartSymmetry.Left);
            Routing.TryApplyPartDamage(a, other, Spec("Slash", 30), attacker);
            Assert.That(bleeding.GetPartRate(other), Is.GreaterThan(0f), "the untied leg does not bleed.");
        });
    }
}
