#nullable enable
using System.Linq;
using Content.Client.UserInterface.Systems.Chat;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._WF.Wolfmed.Consciousness;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.Consciousness;
using Content.Shared.ActionBlocker;
using Content.Shared.Actions;
using Content.Shared.Body.Part;
using Content.Shared.Chat;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Mind;
using Content.Shared.Players;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Client.UserInterface;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.Wolfmed.Scenarios;

/// <summary>
/// M1a package C (plan §5.3, §2.2): what a Downed player can still do. Pick up what is lying within reach
/// (still never fire it), and call for help: a shout, a medical-HUD flag and a cooldown, Downed only.
/// </summary>
[TestFixture]
[TestOf(typeof(WolfmedDownedSystem))]
public sealed class WolfmedCrawlingActionsTest : GameTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WolfmedCrawlTestBullet
  parent: BaseBullet

- type: entity
  id: WolfmedCrawlTestCartridge
  components:
  - type: CartridgeAmmo
    proto: WolfmedCrawlTestBullet
    deleteOnSpawn: true

- type: entity
  id: WolfmedCrawlTestGun
  components:
  - type: Sprite
    sprite: Objects/Tools/crowbar.rsi
    state: icon
  - type: Item
  - type: Gun
    fireRate: 1
    selectedMode: SemiAuto
    availableModes:
    - SemiAuto
  - type: BallisticAmmoProvider
    proto: WolfmedCrawlTestCartridge
    capacity: 10
";

    private async Task Pin()
    {
        await OverrideCVar(Side.Server, WolfmedCVars.Consciousness, true);
        await OverrideCVar(Side.Server, WolfmedCVars.ConsciousnessPainDown, 0.95f);
        await OverrideCVar(Side.Server, WolfmedCVars.ConsciousnessPainOut, 1.4f);
        await OverrideCVar(Side.Server, WolfmedCVars.DownedReach, 1.5f);
        await OverrideCVar(Side.Server, WolfmedCVars.CallForHelpSeconds, 60f);
        await OverrideCVar(Side.Server, WolfmedCVars.CallForHelpCooldown, 30f);
    }

    /// <summary>
    /// Downed by pain alone: 129 on the torso (the Downed line is 128.25), its wound floor held there. Under
    /// the 130 pain shock, whose stun would block the very interactions under test.
    /// </summary>
    private void PutDown(EntityUid body)
    {
        var s = new WolfmedScenario(SEntMan);
        var part = s.Part(body, BodyPartType.Torso);
        var pain = SEntMan.GetComponent<PainComponent>(part);
        pain.WoundPain = FixedPoint2.New(129);
        SEntMan.System<PainSystem>().SetPain((part, pain), FixedPoint2.New(129));
    }

    /// <summary>Makes an empty hand the active one. The upstream helper never finds one (it asks IsHolding(null)).</summary>
    private void SelectEmptyHand(EntityUid body)
    {
        var hands = SEntMan.GetComponent<HandsComponent>(body);
        var empty = hands.Hands.Values.First(hand => hand.HeldEntity == null);
        SEntMan.System<SharedHandsSystem>().SetActiveHand(body, empty, hands);
    }

    private WolfmedConsciousness State(EntityUid body) =>
        SEntMan.GetComponent<WolfmedConsciousnessComponent>(body).State;

    private bool HasCallAction(EntityUid body) =>
        SEntMan.System<SharedActionsSystem>().GetActions(body).Any(action =>
            SEntMan.GetComponent<MetaDataComponent>(action.Id).EntityPrototype?.ID ==
            WolfmedCallForHelpSystem.CallAction.Id);

    /// <summary>
    /// P16, OD7 (b): Downed picks up an item on its own tile and one on the next tile, cannot reach one 3 m
    /// away, and still cannot fire the gun it picked up.
    /// </summary>
    [Test]
    public async Task DownedPickupTest()
    {
        await Pin();
        var map = await Pair.CreateTestMap();
        EntityUid body = default, underfoot = default, gun = default, far = default;

        await Server.WaitPost(() =>
        {
            // Floor under the next tiles too: an item off the grid is not "on the next tile".
            var mapSystem = SEntMan.System<SharedMapSystem>();
            for (var x = 1; x <= 3; x++)
                mapSystem.SetTile(map.Grid, new Robust.Shared.Maths.Vector2i(x, 0), map.Tile.Tile);

            body = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            underfoot = SEntMan.SpawnEntity("Crowbar", map.GridCoords);
            gun = SEntMan.SpawnEntity("WolfmedCrawlTestGun", map.GridCoords.Offset(new System.Numerics.Vector2(1f, 0f)));
            far = SEntMan.SpawnEntity("Crowbar", map.GridCoords.Offset(new System.Numerics.Vector2(3f, 0f)));
        });
        await RunTicksSync(5);
        await Server.WaitPost(() => PutDown(body));

        // Going down knocks the body over for a moment; the stun has to pass before it reaches for anything.
        await RunSeconds(4);

        await Server.WaitAssertion(() =>
        {
            var blocker = SEntMan.System<ActionBlockerSystem>();
            var hands = SEntMan.System<SharedHandsSystem>();
            var interaction = SEntMan.System<SharedInteractionSystem>();
            Assert.That(State(body), Is.EqualTo(WolfmedConsciousness.Downed), "the fixture is not Downed.");

            Assert.Multiple(() =>
            {
                Assert.That(blocker.CanInteract(body, underfoot), Is.True, "Downed cannot reach its own tile.");
                Assert.That(blocker.CanInteract(body, gun), Is.True, "Downed cannot reach the next tile.");
                Assert.That(blocker.CanInteract(body, far), Is.False, "Downed reached 3 m away.");
            });

            SelectEmptyHand(body);
            interaction.UserInteraction(body, SEntMan.GetComponent<TransformComponent>(gun).Coordinates, gun);
            Assert.That(hands.IsHolding(body, gun), Is.True, "the gun on the next tile was not picked up.");
        });

        // One reach per tick, the way a player's clicks arrive.
        await RunTicksSync(3);

        await Server.WaitAssertion(() =>
        {
            var hands = SEntMan.System<SharedHandsSystem>();
            var interaction = SEntMan.System<SharedInteractionSystem>();
            SelectEmptyHand(body);
            interaction.UserInteraction(body, SEntMan.GetComponent<TransformComponent>(underfoot).Coordinates, underfoot);
            Assert.That(hands.IsHolding(body, underfoot), Is.True, "the item underfoot was not picked up.");
        });
        await RunTicksSync(3);

        await Server.WaitAssertion(() =>
        {
            var hands = SEntMan.System<SharedHandsSystem>();
            hands.TryDrop(body, underfoot, checkActionBlocker: false);
            SelectEmptyHand(body);
            SEntMan.System<SharedInteractionSystem>()
                .UserInteraction(body, SEntMan.GetComponent<TransformComponent>(far).Coordinates, far);
            Assert.That(hands.IsHolding(body, far), Is.False, "an item 3 m away was picked up.");

            var ammo = SEntMan.GetComponent<BallisticAmmoProviderComponent>(gun);
            var before = ammo.Count;
            SEntMan.System<SharedGunSystem>().AttemptShoot(body, gun, SEntMan.GetComponent<GunComponent>(gun),
                new EntityCoordinates(body, new System.Numerics.Vector2(5f, 0f)));
            Assert.That(ammo.Count, Is.EqualTo(before), "a Downed body fired the gun it picked up.");
        });
    }

    /// <summary>
    /// Call for help: Downed only. The call shouts, flags the caller on medical HUDs for 60 s and is refused
    /// during the 30 s cooldown; the action does not exist Up or Unconscious.
    /// </summary>
    [Test]
    public async Task CallForHelpTest()
    {
        await Pin();
        var map = await Pair.CreateTestMap();
        var session = ServerSession!;
        var minds = SEntMan.System<SharedMindSystem>();
        var calls = SEntMan.System<WolfmedCallForHelpSystem>();
        EntityUid body = default;

        await Server.WaitPost(() =>
        {
            // M3: station air, so no barotrauma lands on the caller while the test waits.
            new WolfmedScenario(SEntMan).SetAir(map.MapUid, true);
            minds.WipeMind(session.ContentData()?.Mind);
            body = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            minds.TransferTo(minds.CreateMind(session.UserId).Owner, body);
        });
        await RunTicksSync(30);

        await Server.WaitAssertion(() =>
        {
            Assert.That(State(body), Is.EqualTo(WolfmedConsciousness.Up));
            Assert.That(HasCallAction(body), Is.False, "an Up body can call for help.");
            Assert.That(calls.CallForHelp(body, null, 60), Is.False, "an Up body called for help.");

            PutDown(body);
            Assert.That(State(body), Is.EqualTo(WolfmedConsciousness.Downed));
            Assert.That(HasCallAction(body), Is.True, "a Downed body has no Call for help.");
        });

        // M3: the fall's stun stutters (a Goob edit to TryStun), which mangles "airlock" at random; a call made
        // inside it came through or not depending on how the random draws before it fell. Wait it out.
        await RunSeconds(3);

        await Server.WaitAssertion(() =>
        {
            Assert.That(State(body), Is.EqualTo(WolfmedConsciousness.Downed));
            Assert.That(calls.CallForHelp(body, "Medic! Over by the airlock!", 60), Is.True);
            var comp = SEntMan.GetComponent<WolfmedCallForHelpComponent>(body);
            var now = SGameTiming.CurTime;
            Assert.Multiple(() =>
            {
                Assert.That(calls.IsFlagged(body), Is.True, "the call did not flag the caller.");
                Assert.That((comp.FlagUntil - now).TotalSeconds, Is.EqualTo(60).Within(0.5), "the flag is not 60 s.");
                Assert.That((comp.CooldownUntil - now).TotalSeconds, Is.EqualTo(30).Within(0.5), "the cooldown is not 30 s.");
                Assert.That(calls.CallForHelp(body, null, 60), Is.False, "a second call inside the cooldown went out.");
            });
        });
        await RunTicksSync(10);

        var shouted = false;
        await Client.WaitPost(() =>
        {
            var chat = Client.ResolveDependency<IUserInterfaceManager>().GetUIController<ChatUIController>();
            shouted = chat.History.Any(h => h.Msg.Channel == ChatChannel.Local && h.Msg.Message.Contains("airlock"));
        });
        Assert.That(shouted, Is.True, "the call was not said aloud.");

        // Past the cooldown the next call goes out; the flag from the first is still up until 60 s.
        await RunSeconds(31);
        await Server.WaitAssertion(() =>
        {
            Assert.That(calls.IsFlagged(body), Is.True, "the flag dropped before 60 s.");
            Assert.That(calls.CallForHelp(body, null, 60), Is.True, "the cooldown outlasted 30 s.");
        });

        await RunSeconds(65);
        await Server.WaitAssertion(() =>
        {
            Assert.That(calls.IsFlagged(body), Is.False, "the flag outlasted 60 s.");

            // Out cold: no action and no call.
            SEntMan.System<WolfmedConsciousnessSystem>().SetExternalPressure(body, "test", 1f);
            Assert.That(State(body), Is.EqualTo(WolfmedConsciousness.Unconscious));
            Assert.That(HasCallAction(body), Is.False, "an Unconscious body kept Call for help.");
            Assert.That(calls.CallForHelp(body, null, 60), Is.False, "an Unconscious body called for help.");
        });
    }
}
