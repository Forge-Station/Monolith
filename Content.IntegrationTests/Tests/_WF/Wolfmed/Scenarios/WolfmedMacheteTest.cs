#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._WF.Wolfmed.Consciousness;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.Body;
using Content.Shared._WF.Wolfmed.Consciousness;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using NUnit.Framework;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Wolfmed.Scenarios;

/// <summary>
/// Playtest 4: a machete (Slash 32, 36.8 on a Skrell) reaching the chest. Before, organs held 15 under a cap of 5,
/// so any organ a heavy hit reached died on the third, and a Skrell, whose torso carries Wolfmed data on the heart and
/// lungs only, went into arrest on three hits while a human was merely Downed. With organs at 25 under a cap of 3
/// the heart survives three hits on both bodies and goes between the seventh and twelfth.
/// </summary>
[TestFixture]
public sealed class WolfmedMacheteTest : GameTest
{
    private WoundDamageRoutingSystem Routing => SEntMan.System<WoundDamageRoutingSystem>();
    private static DamageSpecifier Spec(string type, float amount) => WolfmedScenario.Spec(type, amount);

    [TestCase("MobHuman", 36.8f)]
    [TestCase("RMCMobSkrell", 32f)]
    public async Task MacheteHitsToTheChestTest(string proto, float slash)
    {
        var map = await Pair.CreateTestMap();
        var s = new WolfmedScenario(SEntMan);
        EntityUid a = default, attacker = default;
        await Server.WaitPost(() =>
        {
            s.SetAir(map.MapUid, true);
            a = SEntMan.SpawnEntity(proto, map.GridCoords);
            attacker = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
        });
        await RunSeconds(3);

        var body = SEntMan.System<SharedBodySystem>();
        var consc = SEntMan.System<WolfmedConsciousnessSystem>();
        int? heartFailed = null;
        for (var hit = 1; hit <= 16 && heartFailed == null; hit++)
        {
            await Server.WaitPost(() =>
            {
                var torso = s.Part(a, BodyPartType.Torso);
                Routing.TryApplyPartDamage(a, torso, Spec("Slash", slash), attacker);
            });
            await RunSeconds(1);
            var current = hit;
            await Server.WaitAssertion(() =>
            {
                var torso = s.Part(a, BodyPartType.Torso);
                var organs = body.GetPartOrgans(torso)
                    .Where(o => SEntMan.HasComponent<WolfmedOrganComponent>(o.Id))
                    .Select(o => (Id: SEntMan.GetComponent<MetaDataComponent>(o.Id).EntityPrototype?.ID ?? "?",
                        Health: SEntMan.GetComponent<WolfmedOrganComponent>(o.Id).Health.Float()))
                    .ToList();
                var heart = organs.FirstOrDefault(o => o.Id.EndsWith("Heart"));
                var c = SEntMan.GetComponent<WolfmedConsciousnessComponent>(a);
                TestContext.Out.WriteLine($"{proto} hit {current}: [{string.Join(", ", organs.Select(o => $"{o.Id}={o.Health:0.#}"))}] " +
                    $"summed pain {consc.GetUncappedPain(a):0.#} state {c.State} cause {c.Cause}");
                if (heart.Id == null || heart.Health <= 0f)
                    heartFailed = current;

                if (current == 3)
                {
                    Assert.That(heart.Health, Is.GreaterThan(0f), "three machete hits stopped the heart.");
                    Assert.That(c.State, Is.Not.EqualTo(WolfmedConsciousness.Unconscious), "three machete hits knocked the patient out.");
                }
            });
        }

        // The human heart shares the reach damage with four other organs by a roll, so its hit varies; the Skrell's is 9.
        Assert.That(heartFailed, Is.InRange(7, 12), $"{proto}: the heart did not fail within 7-12 machete hits.");
    }
}
