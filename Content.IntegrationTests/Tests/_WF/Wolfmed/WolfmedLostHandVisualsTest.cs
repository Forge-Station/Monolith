#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Shared._Onyx.Wounds;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Wieldable;
using Content.Shared.Wieldable.Components;
using NUnit.Framework;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// Playtest 5: "axing self causes the axe overlay to get stuck on the mob". The arm holding the axe comes off, the
/// hand goes with it, and the client must take the axe's in-hand layers off the body sprite with the hand.
/// </summary>
[TestFixture]
public sealed class WolfmedLostHandVisualsTest : GameTest
{
    [Test]
    [TestCase(false)]
    [TestCase(true)]
    public async Task LosingTheHandTakesTheHeldItemOffTheSpriteTest(bool wielded)
    {
        var map = await Pair.CreateTestMap();
        EntityUid body = default, axe = default, arm = default;
        HandLocation location = default;
        await Server.WaitPost(() =>
        {
            body = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            axe = SEntMan.SpawnEntity("FireAxe", map.GridCoords);
            var hands = SEntMan.System<SharedHandsSystem>();
            Assert.That(hands.TryPickupAnyHand(body, axe, checkActionBlocker: false), Is.True, "the axe was not picked up.");
            var hand = SEntMan.GetComponent<HandsComponent>(body).Hands.Values.First(h => h.HeldEntity == axe);
            location = hand.Location;
            if (wielded)
            {
                var wieldable = SEntMan.GetComponent<WieldableComponent>(axe);
                Assert.That(SEntMan.System<SharedWieldableSystem>().TryWield(axe, wieldable, body), Is.True, "the axe was not wielded.");
            }

            var symmetry = location == HandLocation.Left ? BodyPartSymmetry.Left : BodyPartSymmetry.Right;
            arm = SEntMan.System<SharedBodySystem>().GetBodyChildren(body)
                .First(p => p.Component.PartType == BodyPartType.Arm && p.Component.Symmetry == symmetry).Id;
        });
        await RunTicksSync(20);

        // Whatever the axe put on the body sprite for that hand: the plain or the wielded in-hand layers.
        var shown = new List<string>();
        await Client.WaitPost(() =>
        {
            var sprites = CEntMan.System<SpriteSystem>();
            var client = ToClientUid(body);
            var sprite = CEntMan.GetComponent<SpriteComponent>(client);
            var revealed = CEntMan.GetComponent<HandsComponent>(client).RevealedLayers;
            foreach (var (where, keys) in revealed)
            {
                if (where == location)
                    shown.AddRange(keys.Where(k => sprites.LayerMapTryGet((client, sprite), k, out _, false)));
            }

            Assert.That(shown, Is.Not.Empty, $"the held axe is not drawn on the body before the arm comes off (layers: {string.Join(", ", sprite.AllLayers.Select(l => l.RsiState.Name))}).");
        });

        await Server.WaitPost(() =>
        {
            Assert.That(SEntMan.System<AmputationSystem>().TryAmputate(body, arm), Is.True, "the arm did not come off.");
        });
        await RunTicksSync(30);

        await Client.WaitPost(() =>
        {
            var sprites = CEntMan.System<SpriteSystem>();
            var client = ToClientUid(body);
            var sprite = CEntMan.GetComponent<SpriteComponent>(client);
            var hands = CEntMan.GetComponent<HandsComponent>(client);
            var revealed = hands.RevealedLayers;
            var stuck = new List<string>();
            foreach (var (where, keys) in revealed)
            {
                if (hands.Hands.Values.Any(h => h.Location == where))
                    continue;

                foreach (var layer in keys)
                {
                    if (sprites.LayerMapTryGet((client, sprite), layer, out _, false))
                        stuck.Add($"{where}: {layer}");
                }
            }

            Assert.Multiple(() =>
            {
                Assert.That(hands.Hands.Values.Any(h => h.Location == location), Is.False, "the client still has the lost hand.");
                Assert.That(shown.Where(k => sprites.LayerMapTryGet((client, sprite), k, out _, false)), Is.Empty, "the axe is still drawn in the lost hand.");
                Assert.That(stuck, Is.Empty, "layers left on the sprite for a hand that is gone:\n" + string.Join("\n", stuck));
            });
        });
    }
}
