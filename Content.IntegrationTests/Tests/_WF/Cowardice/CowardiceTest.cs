#nullable enable
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._WF.Cowardice;
using Content.Server._WF.Strangers;
using Content.Server._WF.Traders;
using Content.Shared.ActionBlocker;
using Content.Shared.Damage;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Standing;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Cowardice;

/// <summary>A drawn gun or a few hits can make a civilian cower or run; a brave one holds his ground; combat NPCs never have the trait.</summary>
[TestFixture]
[TestOf(typeof(WFCowardiceSystem))]
public sealed class CowardiceTest
{
    private static async Task<(TestMapData Map, EntityUid Player)> Rig(TestPair pair)
    {
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        EntityUid player = default;
        await server.WaitPost(() =>
        {
            var maps = server.System<SharedMapSystem>();
            var plating = server.ResolveDependency<ITileDefinitionManager>()["Plating"].TileId;
            for (var x = -12; x <= 12; x++)
            for (var y = -12; y <= 12; y++)
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), new Tile(plating));
            var gravity = server.EntMan.EnsureComponent<Content.Shared.Gravity.GravityComponent>(map.Grid.Owner);
            gravity.Enabled = true;
            gravity.Inherent = true;
            server.EntMan.Dirty(map.Grid.Owner, gravity);
            player = server.EntMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid.Owner, 0.5f, 0.5f));
            server.PlayerMan.SetAttachedEntity(pair.Player!, player);
        });
        return (map, player);
    }

    /// <summary>Spawns a civilian east of the player with his nerve set by the test, and the player holding a gun facing him.</summary>
    private static async Task<EntityUid> Confront(TestPair pair, TestMapData map, EntityUid player, string prototype,
        float cowardice, float flee, bool armed)
    {
        var server = pair.Server;
        EntityUid npc = default;
        await server.WaitPost(() =>
        {
            var entMan = server.EntMan;
            npc = entMan.SpawnEntity(prototype, new EntityCoordinates(map.Grid.Owner, 5.5f, 0.5f));
            var coward = entMan.GetComponent<WFCowardiceComponent>(npc);
            coward.Cowardice = cowardice;
            coward.Flee = flee;
            coward.MinSeconds = 3f;
            coward.MaxSeconds = 3f;
            coward.RestSeconds = 1f;
            coward.DropChance = 0f;
            server.System<SharedTransformSystem>().SetWorldRotation(player, Angle.FromDegrees(90));
            var hands = server.System<SharedHandsSystem>();
            foreach (var held in hands.EnumerateHeld(player).ToList())
                entMan.DeleteEntity(held);
            if (armed)
            {
                var gun = entMan.SpawnEntity("WeaponPistolMk58", new EntityCoordinates(map.Grid.Owner, 0.5f, 0.5f));
                Assert.That(server.System<SharedHandsSystem>().TryPickupAnyHand(player, gun), Is.True);
            }
        });
        return npc;
    }

    [Test]
    public async Task ADrawnGunMakesACowardCowerUntilHeCalmsDown()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var (map, player) = await Rig(pair);
        var npc = await Confront(pair, map, player, "WFStrangerBeggar", 1f, 0f, true);

        await pair.RunSeconds(2f);
        await server.WaitAssertion(() =>
        {
            var coward = server.EntMan.GetComponent<WFCowardiceComponent>(npc);
            Assert.That(coward.State, Is.EqualTo(WFCowardState.Cowering), "He should cower at a gun pointed at him.");
            Assert.That(coward.Threat, Is.EqualTo(player));
            Assert.That(server.System<ActionBlockerSystem>().CanMove(npc), Is.False, "A cowering man doesn't move.");
            Assert.That(server.System<StandingStateSystem>().IsDown(npc), Is.True, "He should be down on the ground.");
            Assert.That(server.EntMan.GetComponent<WFStrangerComponent>(npc).Hostile, Is.False);
        });

        await pair.RunSeconds(6f);
        await server.WaitAssertion(() =>
        {
            var coward = server.EntMan.GetComponent<WFCowardiceComponent>(npc);
            Assert.That(coward.State, Is.EqualTo(WFCowardState.None), "He should calm down in the end.");
            Assert.That(server.System<ActionBlockerSystem>().CanMove(npc), Is.True);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ABraveManIgnoresTheGunAndAnUnarmedPlayerIsNoThreat()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var (map, player) = await Rig(pair);
        var brave = await Confront(pair, map, player, "WFStrangerBeggar", 0f, 0f, true);
        await pair.RunSeconds(2f);
        await server.WaitAssertion(() =>
            Assert.That(server.EntMan.GetComponent<WFCowardiceComponent>(brave).State, Is.EqualTo(WFCowardState.None)));
        await server.WaitPost(() => server.EntMan.DeleteEntity(brave));

        // Nothing in his hands, nothing to fear.
        var timid = await Confront(pair, map, player, "WFStrangerBeggar", 1f, 0f, false);
        await pair.RunSeconds(2f);
        await server.WaitAssertion(() =>
            Assert.That(server.EntMan.GetComponent<WFCowardiceComponent>(timid).State, Is.EqualTo(WFCowardState.None)));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task EnoughHitsBreakABeggarWhoWasFightingBack()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var (map, player) = await Rig(pair);
        var npc = await Confront(pair, map, player, "WFStrangerBeggar", 1f, 0f, false);

        var damage = new DamageSpecifier();
        damage.DamageDict["Blunt"] = 2;
        var coward = server.EntMan.GetComponent<WFCowardiceComponent>(npc);
        var stranger = server.EntMan.GetComponent<WFStrangerComponent>(npc);
        Assert.That(coward.Hits, Is.EqualTo(2), "A beggar breaks on the second hit.");

        // Hits a tick apart: one blow that reaches him twice in a tick still counts once.
        await server.WaitPost(() => server.System<DamageableSystem>().TryChangeDamage(npc, damage, origin: player));
        await pair.RunTicksSync(3);
        await server.WaitAssertion(() =>
        {
            Assert.That(coward.State, Is.EqualTo(WFCowardState.None), "One hit is not enough.");
            Assert.That(stranger.Hostile, Is.True, "He fights back at first.");
        });

        await server.WaitPost(() => server.System<DamageableSystem>().TryChangeDamage(npc, damage, origin: player));
        await pair.RunTicksSync(3);
        await server.WaitAssertion(() =>
        {
            Assert.That(coward.State, Is.EqualTo(WFCowardState.Cowering), "The second hit should break him.");
            Assert.That(stranger.Hostile, Is.False, "He no longer fights.");
        });

        // Hit again while down: he stays down and doesn't turn on his attacker.
        await server.WaitPost(() => server.System<DamageableSystem>().TryChangeDamage(npc, damage, origin: player));
        await pair.RunTicksSync(3);
        await server.WaitAssertion(() =>
        {
            Assert.That(coward.State, Is.EqualTo(WFCowardState.Cowering));
            Assert.That(stranger.Hostile, Is.False);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ATraderRunsTheWayHeDoesWhenHurtAndStillTradesAfter()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var (map, player) = await Rig(pair);
        var trader = await Confront(pair, map, player, "WFTraderWanderer", 1f, 1f, true);

        await pair.RunSeconds(2f);
        await server.WaitAssertion(() =>
        {
            Assert.That(server.EntMan.GetComponent<WFCowardiceComponent>(trader).State, Is.EqualTo(WFCowardState.Fleeing));
            var flee = server.EntMan.GetComponent<TraderFleeComponent>(trader);
            Assert.That(flee.Threat, Is.EqualTo(player), "He should run from the gunman.");
            Assert.That(flee.Attackers, Is.Empty, "A gun that never fired is not a reason to refuse him custom.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task OnlyCiviliansHaveNerve()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        await server.WaitAssertion(() =>
        {
            var protos = server.ProtoMan.EnumeratePrototypes<EntityPrototype>()
                .Where(proto => !proto.Abstract && proto.Components.ContainsKey("WFCowardice"))
                .ToList();
            Assert.That(protos, Is.Not.Empty);
            foreach (var proto in protos)
            {
                Assert.That(proto.Components.ContainsKey("WFCrew"), Is.False, $"{proto.ID} is a combat NPC but has WFCowardice.");
                var civilian = proto.Components.ContainsKey("WFStranger") || proto.Components.ContainsKey("Trader");
                Assert.That(civilian, Is.True, $"{proto.ID} has WFCowardice but is neither a stranger nor a trader.");
            }
        });
        await pair.CleanReturnAsync();
    }
}
