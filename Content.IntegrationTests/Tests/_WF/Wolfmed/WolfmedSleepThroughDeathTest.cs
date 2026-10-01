#nullable enable
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._WF.Wolfmed.Life;
using Content.Shared.Bed.Sleep;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Stunnable;
using NUnit.Framework;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// Playtest 5, "someone went to sleep while dead and can't wake up when fixed". A body in cardiac arrest looks dead and
/// may still sleep; when it then died, the sleep was taken off without waking it, so the stun and knockdown sleeping
/// puts on stayed for good and the Wake action had nothing to end. Revived, the patient lay there forever.
/// </summary>
[TestFixture]
public sealed class WolfmedSleepThroughDeathTest : GameTest
{
    [Test]
    public async Task DyingAsleepWakesTheBodyTest()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var life = SEntMan.System<WolfmedLifeSystem>();
            var body = SEntMan.SpawnEntity("MobHuman", map.GridCoords);

            Assert.That(life.StartArrest(body, "blood"), Is.True, "the fixture could not stop the heart.");
            Assert.That(SEntMan.System<SleepingSystem>().TrySleeping(body), Is.True, "a body in arrest cannot sleep.");
            Assert.That(SEntMan.HasComponent<StunnedComponent>(body), Is.True, "sleeping did not stun the body.");

            Assert.That(life.Kill(body), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.HasComponent<SleepingComponent>(body), Is.False, "a corpse is still asleep.");
                Assert.That(SEntMan.HasComponent<StunnedComponent>(body), Is.False, "death left the sleep's stun behind.");
                Assert.That(SEntMan.HasComponent<KnockedDownComponent>(body), Is.False, "death left the sleep's knockdown behind.");
            });

            SEntMan.System<WolfmedRevivalSystem>().Revive(body);
            Assert.That(SEntMan.GetComponent<MobStateComponent>(body).CurrentState, Is.Not.EqualTo(MobState.Dead));
            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.HasComponent<SleepingComponent>(body), Is.False, "the revived body is asleep.");
                Assert.That(SEntMan.HasComponent<StunnedComponent>(body), Is.False, "the revived body is still stunned by its sleep.");
            });
        });
    }
}
