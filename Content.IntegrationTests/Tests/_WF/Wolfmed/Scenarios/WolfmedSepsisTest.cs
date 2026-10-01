#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._WF.Wolfmed.Wounds;
using Content.Shared._WF.Wolfmed.Body;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.Consciousness;
using Content.Shared._WF.Wolfmed.Life;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Body.Organ;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Mobs.Systems;
using NUnit.Framework;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Wolfmed.Scenarios;

/// <summary>
/// Playtest 4, SEPSIS: sepsis left alone kills, through the organs. Past wolfmed.sepsis_organ_damage_from it eats the
/// torso organs, kidneys and liver first and the heart last; playtest 5 made septic shock a state the patient is out
/// in rather than a brain drain, so the heart's failure is the only arrest in it.
/// </summary>
[TestFixture]
public sealed class WolfmedSepsisTest : GameTest
{
    /// <summary>The torso's Wolfmed organs by slot, with their health.</summary>
    private Dictionary<string, (EntityUid Organ, WolfmedOrganComponent Health)> TorsoOrgans(WolfmedScenario s, EntityUid body)
    {
        var torso = s.Part(body, BodyPartType.Torso);
        return SEntMan.System<SharedBodySystem>().GetPartOrgans(torso)
            .Where(o => SEntMan.HasComponent<WolfmedOrganComponent>(o.Id))
            .ToDictionary(o => o.Component.SlotId, o => (o.Id, SEntMan.GetComponent<WolfmedOrganComponent>(o.Id)));
    }

    /// <summary>
    /// <c>SepsisKillsTest</c> (playtest 5): the shipped CVars, air, warm and nothing else wrong. Sepsis pinned at 100 is
    /// septic shock: the patient is out at once (cause SepticShock, Critical), the heart beats on and nothing drains
    /// the brain. It kills only through the organs: the lungs are impaired under half (500 s) and their drain grows
    /// with the damage, so the brain is at the arrest line about 890 s in (±10%), the arrest is by oxygen with the
    /// lungs behind it, and brain death follows on the arrest clock. Antibiotics that pull the sepsis under the line
    /// wake the patient on the spot.
    /// </summary>
    [Test]
    public async Task SepsisKillsTest()
    {
        var map = await Pair.CreateTestMap();
        var s = new WolfmedScenario(SEntMan);
        var mobState = SEntMan.System<MobStateSystem>();
        var infection = SEntMan.System<WolfmedInfectionSystem>();
        EntityUid body = default, treated = default;

        await Server.WaitPost(() =>
        {
            s.SetAir(map.MapUid, true);
            s.KeepGrid(map.Grid);
            body = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            treated = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
        });
        await RunSeconds(2);

        await Server.WaitAssertion(() =>
        {
            foreach (var patient in new[] { body, treated })
            {
                SEntMan.EnsureComponent<WolfmedSepsisComponent>(patient).Progress = 100f;
                infection.Update(5f);
                s.Advance(patient, 1);
            }

            var brain = s.Life.GetBrain(body);
            Assert.Multiple(() =>
            {
                Assert.That(s.State(body), Is.EqualTo(WolfmedConsciousness.Unconscious), "septic shock did not put the patient out.");
                Assert.That(mobState.IsCritical(body), Is.True, "septic shock is not Critical.");
                Assert.That(SEntMan.GetComponent<WolfmedConsciousnessComponent>(body).Cause, Is.EqualTo(WolfmedCause.SepticShock),
                    "the state is not named for the shock.");
                Assert.That(s.Life.InArrest(body), Is.False, "septic shock stopped the heart.");
                Assert.That(brain, Is.Not.Null);
                Assert.That(s.Life.DrainRate(body, brain!.Value), Is.EqualTo(0f), "sepsis is draining the brain.");
            });

            // Four units take 24 off: 76, under the line. The pressure lifts with it.
            Assert.That(infection.Treat(treated, 4f), Is.True);
            s.Advance(treated, 1);
            Assert.Multiple(() =>
            {
                Assert.That(infection.InSepticShock(treated), Is.False);
                Assert.That(s.State(treated), Is.Not.EqualTo(WolfmedConsciousness.Unconscious), "antibiotics under the line did not wake the patient.");
                Assert.That(mobState.IsCritical(treated), Is.False);
            });
        });

        // The organ clock, five seconds at a time with the sepsis pinned. The lungs' failure is the arrest. Brain death
        // is read off the organ here: only the organ system's own frame turns it into MobState.Dead, and no frame runs
        // inside this loop.
        int? arrest = null, brainDead = null;
        var hypoxia = WolfmedCauseSource.None;
        var log = new List<string>();
        await Server.WaitAssertion(() =>
        {
            for (var t = 5; t <= 40 * 60 && brainDead == null; t += 5)
            {
                SEntMan.EnsureComponent<WolfmedSepsisComponent>(body).Progress = 100f;
                infection.Update(5f);
                s.Advance(body, 5);

                if (arrest == null && s.Life.InArrest(body))
                {
                    // Read here: death clears the consciousness component's hypoxia source.
                    arrest = t;
                    hypoxia = SEntMan.GetComponent<WolfmedConsciousnessComponent>(body).HypoxiaSource;
                }

                if (brainDead == null && s.Life.IsBrainDead(body))
                    brainDead = t;

                if (t % 300 == 0 || t == arrest || t == brainDead)
                {
                    var organs = string.Join(", ", TorsoOrgans(s, body).Select(o => $"{o.Key} {o.Value.Health.Health}"));
                    log.Add($"SepsisKillsTest {t} s: oxygenation {s.Life.GetOxygenation(body):0.000}, brain {s.Life.GetBrainActivity(body):0.000}, " +
                            $"state {s.State(body)}, arrest {s.Life.InArrest(body)}; {organs}");
                }
            }
        });

        // One real frame for the organ system to see the brain at zero.
        await RunSeconds(1);

        await Server.WaitAssertion(() =>
        {
            foreach (var line in log)
                TestContext.Out.WriteLine(line);
            TestContext.Out.WriteLine($"SepsisKillsTest: arrest at {arrest?.ToString() ?? "never"} s, brain death at {brainDead?.ToString() ?? "never"} s.");

            Assert.Multiple(() =>
            {
                Assert.That(arrest, Is.Not.Null, "sepsis at 100 never stopped the heart in 40 minutes.");
                Assert.That(arrest, Is.InRange(800, 980), "the lungs did not stop the heart about 890 s in (±10%).");
                Assert.That(SEntMan.GetComponent<WolfmedCardiacArrestComponent>(body).Cause, Is.EqualTo("oxygen"),
                    "the arrest is not the brain running out of oxygen.");
                Assert.That(hypoxia, Is.EqualTo(WolfmedCauseSource.Lungs), "the hypoxia behind the arrest is not named for the lungs.");
                Assert.That(brainDead, Is.Not.Null, "the arrest never killed the brain in 40 minutes.");
                Assert.That(brainDead - arrest, Is.InRange(120, 600), "brain death is off the arrest clock.");
                Assert.That(mobState.IsDead(body), Is.True, "a dead brain did not read as MobState.Dead after a frame.");
            });
        });
    }

    /// <summary>
    /// <c>SepsisOrganDamageTest</c>: with the brain drain off, sepsis pinned at 100 fails the kidneys and liver in
    /// 25 / (1.5 x 1.5) minutes (667 s, ±10%), the lungs and stomach in 25 / 1.5 (1000 s), and the heart is the last
    /// torso organ standing. The analyzer says so. Antibiotics that pull the sepsis under the line stop it.
    /// </summary>
    [Test]
    public async Task SepsisOrganDamageTest()
    {
        await OverrideCVar(Side.Server, WolfmedCVars.BrainSepsisSeconds, 0f);
        await OverrideCVar(Side.Server, WolfmedCVars.SepsisOrganDamageFrom, 90f);
        await OverrideCVar(Side.Server, WolfmedCVars.SepsisOrganDamagePerMinute, 1.5f);
        await OverrideCVar(Side.Server, WolfmedCVars.InfectionRate, 1f);
        await OverrideCVar(Side.Server, WolfmedCVars.ArrestSepsis, 80f);

        var map = await Pair.CreateTestMap();
        var s = new WolfmedScenario(SEntMan);
        EntityUid body = default, treated = default;
        await Server.WaitPost(() =>
        {
            s.SetAir(map.MapUid, true);
            s.KeepGrid(map.Grid);
            body = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            treated = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
        });
        await RunSeconds(2);

        await Server.WaitAssertion(() =>
        {
            var infection = SEntMan.System<WolfmedInfectionSystem>();

            // Under the line: the brain route only, nothing said about organs.
            SEntMan.EnsureComponent<WolfmedSepsisComponent>(body).Progress = 85f;
            var below = s.Report(body);
            Assert.That(below.Routes & WolfmedRoutes.SepsisOrgans, Is.EqualTo(WolfmedRoutes.None),
                "sepsis at 85 already reads as damaging the organs.");

            var failed = new Dictionary<string, int>();
            var heartAtLungs = -1f;
            var othersAtLungs = new List<string>();
            var organs = TorsoOrgans(s, body);
            for (var t = 5; t <= 1500 && !(failed.ContainsKey("lungs") && failed.ContainsKey("stomach")); t += 5)
            {
                SEntMan.EnsureComponent<WolfmedSepsisComponent>(body).Progress = 100f;
                infection.Update(5f);
                foreach (var (slot, organ) in organs)
                {
                    if (!failed.ContainsKey(slot) && organ.Health.Health.Float() <= 0f)
                        failed[slot] = t;
                }

                if (t == 60)
                {
                    var report = s.Report(body);
                    var line = WolfmedVitalsText.DoFirstLine(report);
                    TestContext.Out.WriteLine($"SepsisOrganDamageTest at 60 s: {line}; organs " +
                        string.Join(", ", organs.Select(o => $"{o.Key} {o.Value.Health.Health}")));
                    Assert.That(report.Routes & WolfmedRoutes.SepsisOrgans, Is.Not.EqualTo(WolfmedRoutes.None),
                        "sepsis at 100 does not read as damaging the organs.");
                    // INFECTION: 100 is past wolfmed.septic_shock_at (80), so the organ aid names septic shock.
                    Assert.That(report.SepticShock, Is.True, "sepsis at 100 does not read as septic shock.");
                    Assert.That(line, Does.Contain(WolfmedVitalsText.Aid(WolfmedRoutes.SepsisOrgans, false, shock: true)),
                        "\"Do first\" does not mention the organ damage.");
                    // Review: in shock the plain sepsis aid is the shock one, so that is the duplicate to look for.
                    Assert.That(line, Does.Not.Contain(WolfmedVitalsText.Aid(WolfmedRoutes.Sepsis, false, shock: true) + ";"),
                        "\"Do first\" says antibiotics twice.");
                }
            }

            if (failed.ContainsKey("lungs"))
            {
                heartAtLungs = organs["heart"].Health.Health.Float();
                othersAtLungs = organs.Where(o => o.Key != "heart" && o.Value.Health.Health.Float() > 0f).Select(o => o.Key).ToList();
            }

            TestContext.Out.WriteLine("SepsisOrganDamageTest: failed at " +
                string.Join(", ", failed.OrderBy(f => f.Value).Select(f => $"{f.Key} {f.Value} s")) +
                $"; heart at {heartAtLungs:0.##} when the lungs went.");

            Assert.Multiple(() =>
            {
                Assert.That(failed.GetValueOrDefault("kidneys"), Is.InRange(600, 734), "the kidneys did not fail within 667 s ±10%.");
                Assert.That(failed.GetValueOrDefault("liver"), Is.InRange(600, 734), "the liver did not fail within 667 s ±10%.");
                Assert.That(failed.GetValueOrDefault("lungs"), Is.InRange(900, 1100), "the lungs did not fail within 1000 s ±10%.");
                Assert.That(failed.GetValueOrDefault("stomach"), Is.InRange(900, 1100), "the stomach did not fail within 1000 s ±10%.");
                Assert.That(failed.ContainsKey("heart"), Is.False, "the heart failed before the lungs and stomach.");
                Assert.That(heartAtLungs, Is.GreaterThan(0f), "the heart is not the last torso organ standing.");
                Assert.That(othersAtLungs, Is.Empty, "another torso organ outlasted the lungs beside the heart.");
            });

            // Antibiotics: two minutes at 100, then a dose under the line. The organs hold from then on.
            SEntMan.EnsureComponent<WolfmedSepsisComponent>(treated).Progress = 100f;
            for (var t = 0; t < 120; t += 5)
            {
                SEntMan.GetComponent<WolfmedSepsisComponent>(treated).Progress = 100f;
                infection.Update(5f);
            }

            var kidneys = TorsoOrgans(s, treated)["kidneys"].Health;
            var before = kidneys.Health;
            Assert.That(before.Float(), Is.LessThan(25f), "two minutes at sepsis 100 left the kidneys untouched.");
            Assert.That(infection.Treat(treated, 3f), Is.True);
            Assert.That(infection.GetSepsis(treated), Is.LessThan(90f), "three units did not pull the sepsis under the line.");
            for (var t = 0; t < 180; t += 5)
                infection.Update(5f);

            Assert.That(kidneys.Health, Is.EqualTo(before), "sepsis under the line kept damaging the kidneys.");
        });
    }
}
