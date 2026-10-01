#nullable enable
using System;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._WF.Wolfmed.Life;
using Content.Shared._WF.Wolfmed.Life;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WF.Wolfmed.Scenarios;

/// <summary>
/// Playtest 5, "cardiac arrest onsets too fast still, and people dying is way more work to fix": the shipped clocks,
/// nothing pinned. A stopped heart empties the brain in 180 s and the tissue then goes at 0.05 a second, so brain
/// death is about 7.5 minutes from a full brain; a patient held just over the blood arrest line with the bleeding
/// stopped has about 9 minutes before the oxygen trigger; and chest compressions hold a living arrested brain at the
/// damage line for as long as they go on, with the analyzer's countdown gone while they do.
/// </summary>
[TestFixture]
[TestOf(typeof(WolfmedLifeSystem))]
public sealed class WolfmedArrestClockTest : GameTest
{
    private async Task<(WolfmedScenario Scenario, EntityUid Body)> Patient()
    {
        var map = await Pair.CreateTestMap();
        var s = new WolfmedScenario(SEntMan);
        EntityUid body = default;

        await Server.WaitPost(() =>
        {
            s.SetAir(map.MapUid, true);
            s.KeepGrid(map.Grid);
            body = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
        });
        await RunSeconds(2);
        return (s, body);
    }

    [Test]
    public async Task ArrestToBrainDeathTest()
    {
        var (s, body) = await Patient();

        await Server.WaitAssertion(() =>
        {
            Assert.That(s.Life.StartArrest(body, "oxygen"), Is.True);
            var empty = s.Advance(body, 600, _ => s.Life.GetOxygenation(body) <= 0f);
            var dead = s.Advance(body, 900, _ => s.Life.IsBrainDead(body));
            TestContext.Out.WriteLine($"ArrestToBrainDeathTest: brain empty at {empty} s, brain dead at {empty + dead} s.");
            Assert.Multiple(() =>
            {
                Assert.That(s.Life.GetOxygenation(body), Is.EqualTo(0f));
                Assert.That(empty, Is.InRange(170, 190), "a stopped heart did not empty the brain in about 180 s.");
                Assert.That(s.Life.IsBrainDead(body), Is.True, "an empty brain never died in 15 minutes.");
                Assert.That(empty + dead, Is.InRange(400, 490), "brain death is not about 7.5 minutes from the arrest.");
            });
        });
    }

    [Test]
    public async Task CprHoldsTheBrainTest()
    {
        var (s, body) = await Patient();
        var timing = Server.ResolveDependency<IGameTiming>();

        await Server.WaitAssertion(() =>
        {
            Assert.That(s.Life.StartArrest(body, "oxygen"), Is.True);
            s.Advance(body, 150);
            Assert.That(s.Life.GetOxygenation(body), Is.LessThan(0.4f), "150 s of arrest left the brain over the damage line.");
            var brain = s.Life.GetBrainOrgan(body);
            Assert.That(brain, Is.Not.Null);

            // Compressions: the brain comes back up to the line, stays there, and loses nothing more.
            SEntMan.EnsureComponent<WolfmedCprComponent>(body).Ends = timing.CurTime + TimeSpan.FromMinutes(30);
            s.Advance(body, 60);
            var held = brain!.Value.Comp.Health;
            Assert.Multiple(() =>
            {
                Assert.That(s.Life.InCpr(body), Is.True);
                Assert.That(s.Life.GetOxygenation(body), Is.EqualTo(0.4f).Within(0.02f), "CPR did not bring the brain back to the line.");
                Assert.That(s.Life.GetBrainDeathSeconds(body), Is.Null, "the analyzer still counts down under CPR.");
            });

            s.Advance(body, 600);
            Assert.Multiple(() =>
            {
                Assert.That(s.Life.GetOxygenation(body), Is.EqualTo(0.4f).Within(0.02f), "the hold did not last ten minutes.");
                Assert.That(brain.Value.Comp.Health, Is.EqualTo(held), "the brain lost tissue under CPR.");
                Assert.That(s.Life.IsBrainDead(body), Is.False);
                Assert.That(s.Life.InArrest(body), Is.True, "CPR restarted the heart.");
            });

            // Hands off: the clock resumes from the line.
            SEntMan.RemoveComponent<WolfmedCprComponent>(body);
            var dead = s.Advance(body, 900, _ => s.Life.IsBrainDead(body));
            TestContext.Out.WriteLine($"CprHoldsTheBrainTest: brain dead {dead} s after the compressions stopped.");
            Assert.That(s.Life.IsBrainDead(body), Is.True, "the brain never died once the compressions stopped.");
            Assert.That(dead, Is.InRange(280, 400), "brain death after CPR stopped is off the clock.");
        });
    }

    [Test]
    public async Task LowBloodHoldsForATransfusionTest()
    {
        var (s, body) = await Patient();

        await Server.WaitAssertion(() =>
        {
            // Bled to just over the blood arrest line, then stopped: only the perfusion drain is left.
            s.SetBlood(body, 0.31f);
            var arrest = s.Advance(body, 900, _ => s.Life.InArrest(body));
            TestContext.Out.WriteLine($"LowBloodHoldsForATransfusionTest: arrest at {arrest} s.");
            Assert.Multiple(() =>
            {
                Assert.That(s.Life.InArrest(body), Is.True, "31% blood never stopped the heart in 15 minutes.");
                Assert.That(arrest, Is.InRange(480, 600), "the low-blood arrest is not about 9 minutes out.");
                Assert.That(SEntMan.GetComponent<WolfmedCardiacArrestComponent>(body).Cause, Is.EqualTo("oxygen"),
                    "the arrest is not the brain running out of oxygen.");
            });
        });
    }
}
