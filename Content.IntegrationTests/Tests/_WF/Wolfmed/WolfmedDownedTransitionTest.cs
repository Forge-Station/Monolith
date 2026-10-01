#nullable enable
using System;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._WF.Wolfmed.Consciousness;
using Content.Shared._WF.Wolfmed.Consciousness;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Standing;
using Content.Shared.Stunnable;
using NUnit.Framework;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// AUTODOC5: going Downed is one transition, not a state the body can flicker in and out of.
/// </summary>
/// <remarks>
/// The owner stun-batoned himself, got up, and the body-fall sound repeated for about a second: the stun's
/// knockdown and consciousness both had opinions about the standing state, and every flip out of Downed and
/// back dropped the body again with another sound. The fall now only ever happens on a real transition, and
/// Downed lasts a minimum time so the inputs cannot flip it a dozen times a second.
/// </remarks>
[TestFixture]
[TestOf(typeof(WolfmedConsciousnessSystem))]
public sealed class WolfmedDownedTransitionTest : GameTest
{
    /// <summary>
    /// Inputs swung across the Downed threshold, hard, twenty times, with a stun holding the body down.
    /// The component is added once, which is the one thing that can play the fall sound.
    /// </summary>
    [Test]
    public async Task StunOnTheDownedEdgeFallsOnceTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var consciousness = entities.System<WolfmedConsciousnessSystem>();
            var standing = entities.System<StandingStateSystem>();
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);

            // A stun baton: the knockdown puts them on the floor on its own.
            entities.System<SharedStunSystem>().TryKnockdown(body, TimeSpan.FromSeconds(4), true);
            Assert.That(standing.IsDown(body), Is.True, "the stun did not knock the body down.");

            // 0.71 of an outside pressure is just past the Downed threshold (0.7 of unconscious), 0.5 is
            // well under the hysteresis band: without the dwell this flips the state on every call, and
            // every flip back into Downed is another body-fall sound.
            consciousness.SetExternalPressure(body, "wolfmed-test", 0.71f);
            Assert.That(entities.HasComponent<WolfmedDownedComponent>(body), Is.True,
                "the pressure did not put the body on the floor.");

            var lifts = 0;
            var stands = 0;
            for (var i = 0; i < 20; i++)
            {
                consciousness.SetExternalPressure(body, "wolfmed-test", i % 2 == 0 ? 0.5f : 0.71f);
                if (!entities.HasComponent<WolfmedDownedComponent>(body))
                    lifts++;

                if (!standing.IsDown(body))
                    stands++;
            }

            Assert.Multiple(() =>
            {
                Assert.That(lifts, Is.Zero, "Downed was dropped and retaken on the threshold.");
                Assert.That(stands, Is.Zero, "the body got up and fell over again mid-stun.");
                Assert.That(entities.HasComponent<WolfmedDownedComponent>(body), Is.True,
                    "the body left Downed while the stun still held it.");
            });

            // M1a: the Downed alert is the condition alert system's, in the Health slot, and it replaces the
            // stock health doll rather than sitting beside it. An unclaimed pressure has no alert of its own.
            var alerts = entities.System<WolfmedConditionAlertSystem>();
            Assert.That(alerts.GetShownHealthAlert(body)?.Id, Is.EqualTo("WFWolfmedDowned"),
                "the health slot does not show the Downed alert.");

            // A named cause shows its own: pain on the torso past the Downed line.
            consciousness.SetExternalPressure(body, "wolfmed-test", 0f);
            var torso = System.Linq.Enumerable.Single(
                entities.System<Content.Shared.Body.Systems.SharedBodySystem>().GetBodyChildren(body),
                part => part.Component.PartType == Content.Shared.Body.Part.BodyPartType.Torso).Id;
            entities.System<Content.Shared._Onyx.Wounds.PainSystem>().SetPain(torso,
                Content.Shared.FixedPoint.FixedPoint2.New(129));
            Assert.That(alerts.GetShownHealthAlert(body)?.Id, Is.EqualTo("WFWolfmedDownedPain"),
                "no Downed: pain alert.");
        });
    }

    /// <summary>A body that goes Downed lets go of what it was holding, the way a knockdown does.</summary>
    [Test]
    public async Task GoingDownedDropsHeldItemsTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        EntityUid body = default;
        EntityUid pen = default;

        await server.WaitAssertion(() =>
        {
            var consciousness = entities.System<WolfmedConsciousnessSystem>();
            var hands = entities.System<SharedHandsSystem>();
            body = entities.SpawnEntity("MobHuman", map.GridCoords);
            pen = entities.SpawnEntity("EmergencyMedipen", map.GridCoords);

            Assert.That(hands.TryPickupAnyHand(body, pen), Is.True, "the fixture could not put a pen in a hand.");
            Assert.That(entities.GetComponent<TransformComponent>(pen).ParentUid, Is.EqualTo(body));

            consciousness.SetExternalPressure(body, "wolfmed-test", 0.71f);
            Assert.That(entities.HasComponent<WolfmedDownedComponent>(body), Is.True,
                "the pressure did not put the body on the floor.");
        });

        await Pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var hands = entities.System<SharedHandsSystem>();
            Assert.Multiple(() =>
            {
                Assert.That(hands.TryGetActiveItem(body, out _), Is.False, "the pen is still in the active hand.");
                Assert.That(entities.GetComponent<TransformComponent>(pen).ParentUid, Is.Not.EqualTo(body),
                    "the pen stayed in the hand of a body that had just gone down.");
            });
        });
    }
}
