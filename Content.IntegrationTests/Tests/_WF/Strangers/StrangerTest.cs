#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._WF.Strangers;
using Content.Server.NPC.HTN;
using Content.Server.Shuttles.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Components;
using Content.Shared.Stacks;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.Strangers;

/// <summary>Strangers walk up and hail, sell, get mad when refused, camps send someone out to meet you, and the stalker turns up aboard your ship.</summary>
[TestFixture]
[TestOf(typeof(WFStrangerSystem))]
public sealed class StrangerTest
{
    private static async Task<(TestMapData Map, EntityUid Player)> Rig(TestPair pair, int size = 12)
    {
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        EntityUid player = default;
        await server.WaitPost(() =>
        {
            var maps = server.System<SharedMapSystem>();
            var plating = server.ResolveDependency<ITileDefinitionManager>()["Plating"].TileId;
            for (var x = -size; x <= size; x++)
            for (var y = -size; y <= size; y++)
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), new Tile(plating));
            // Ground, as on a planet: without gravity everyone drifts on after stopping.
            var gravity = server.EntMan.EnsureComponent<Content.Shared.Gravity.GravityComponent>(map.Grid.Owner);
            gravity.Enabled = true;
            gravity.Inherent = true;
            server.EntMan.Dirty(map.Grid.Owner, gravity);
            player = server.EntMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid.Owner, 0.5f, 0.5f));
            server.PlayerMan.SetAttachedEntity(pair.Player!, player);
        });
        return (map, player);
    }

    /// <summary>
    /// Runs the pair until <paramref name="done"/> holds or <paramref name="seconds"/> pass. Ticks run as fast as the box allows,
    /// but the pathfinder answers on a worker thread in real time, so each poll also yields a little wall-clock time to it.
    /// </summary>
    internal static async Task RunUntil(TestPair pair, Func<bool> done, float seconds, int delayMs = 25)
    {
        for (var waited = 0f; waited < seconds; waited += 0.5f)
        {
            var finished = false;
            await pair.Server.WaitPost(() => finished = done());
            if (finished)
                return;
            await pair.RunSeconds(0.5f);
            await Task.Delay(delayMs);
        }
    }

    /// <summary>Why a stranger may not have reached someone: his steering state and how far off he still is.</summary>
    private static string Stuck(TestPair pair, EntityUid npc, EntityUid target)
    {
        var entMan = pair.Server.EntMan;
        var xforms = pair.Server.System<SharedTransformSystem>();
        var status = entMan.TryGetComponent(npc, out Content.Server.NPC.Components.NPCSteeringComponent? steering) ? steering.Status.ToString() : "unregistered";
        var mover = entMan.TryGetComponent(npc, out Content.Shared.Movement.Components.InputMoverComponent? input) && input.CanMove;
        return $"steering {status}, can move {mover}, awake {entMan.HasComponent<Content.Shared.NPC.ActiveNPCComponent>(npc)}, "
               + $"{System.Numerics.Vector2.Distance(xforms.GetWorldPosition(npc), xforms.GetWorldPosition(target)):0.0} tiles off";
    }

    [Test]
    public async Task TheDealerHailsAndTurnsOnYouWhenThreatened()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var (map, player) = await Rig(pair);
        EntityUid dealer = default;
        await server.WaitPost(() => dealer = entMan.SpawnEntity("WFStrangerDealer", new EntityCoordinates(map.Grid.Owner, 6.5f, 0.5f)));
        await RunUntil(pair, () => entMan.GetComponent<WFStrangerComponent>(dealer).Hailed, 45);

        var strangers = server.System<WFStrangerSystem>();
        await server.WaitAssertion(() =>
        {
            var stranger = entMan.GetComponent<WFStrangerComponent>(dealer);
            Assert.That(stranger.Target, Is.EqualTo(player), "The dealer never went for the player.");
            Assert.That(stranger.Hailed, Is.True,
                $"The dealer walked up but never hailed: awake {entMan.HasComponent<Content.Shared.NPC.ActiveNPCComponent>(dealer)}, "
                + $"hostile {stranger.Hostile}, {System.Numerics.Vector2.Distance(server.System<SharedTransformSystem>().GetWorldPosition(dealer), server.System<SharedTransformSystem>().GetWorldPosition(player)):0.0} tiles off.");

            strangers.StartConversation((dealer, stranger), player);
            Assert.That(stranger.Node, Is.EqualTo("pitch"));
            // "Get lost before I call security."
            strangers.Choose((dealer, stranger), player, 2);
            Assert.That(entMan.GetComponent<FactionExceptionComponent>(dealer).Hostiles, Does.Contain(player), "Threatened, he should go for you.");
            Assert.That(entMan.GetComponent<HTNComponent>(dealer).Enabled, Is.True, "His own brain should take over to fight.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TheDealerSellsForCashInHand()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var (map, player) = await Rig(pair);
        await server.WaitAssertion(() =>
        {
            var hands = server.System<SharedHandsSystem>();
            var cash = entMan.SpawnEntity("SpaceCash500", entMan.GetComponent<TransformComponent>(player).Coordinates);
            Assert.That(hands.TryPickupAnyHand(player, cash), Is.True);
            var dealer = entMan.SpawnEntity("WFStrangerDealer", new EntityCoordinates(map.Grid.Owner, 1.5f, 0.5f));
            var stranger = entMan.GetComponent<WFStrangerComponent>(dealer);
            var strangers = server.System<WFStrangerSystem>();

            strangers.StartConversation((dealer, stranger), player);
            strangers.Choose((dealer, stranger), player, 0);
            Assert.That(stranger.Node, Is.EqualTo("menu"));
            strangers.Choose((dealer, stranger), player, 0);

            Assert.That(entMan.GetComponent<StackComponent>(cash).Count, Is.EqualTo(300), "Two hundred should come out of the cash in hand.");
            var held = hands.EnumerateHeld(player).Select(item => entMan.GetComponent<MetaDataComponent>(item).EntityPrototype?.ID).ToList();
            Assert.That(held.Any(id => id is "CigaretteSpaceDrugs" or "JointRainbow" or "BluntRainbow"), Is.True, "No drugs were handed over.");
            var angry = entMan.TryGetComponent<FactionExceptionComponent>(dealer, out var exceptions) ? exceptions.Hostiles : new HashSet<EntityUid>();
            Assert.That(angry, Does.Not.Contain(player), "A sale shouldn't anger him.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ShotWithAGunHeTurnsAndRemembers()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var (map, player) = await Rig(pair);
        await server.WaitAssertion(() =>
        {
            var gun = entMan.SpawnEntity("WeaponPistolMk58", entMan.GetComponent<TransformComponent>(player).Coordinates);
            Assert.That(server.System<SharedHandsSystem>().TryPickupAnyHand(player, gun), Is.True);
            var nut = entMan.SpawnEntity("WFStrangerConspiracist", new EntityCoordinates(map.Grid.Owner, 3.5f, 0.5f));
            var stranger = entMan.GetComponent<WFStrangerComponent>(nut);
            var damage = new DamageSpecifier(server.ProtoMan.Index<DamageTypePrototype>("Piercing"), 5);
            server.System<DamageableSystem>().TryChangeDamage(nut, damage, origin: gun);

            Assert.That(entMan.GetComponent<FactionExceptionComponent>(nut).Hostiles, Does.Contain(player), "Shot, he should go for the shooter.");
            Assert.That(stranger.Hostile, Is.True);
            Assert.That(entMan.GetComponent<HTNComponent>(nut).Enabled, Is.True);

            var strangers = server.System<WFStrangerSystem>();
            strangers.StartConversation((nut, stranger), player);
            Assert.That(stranger.Node, Is.Null, "In his bad books, there's no talking to him.");
        });
        await pair.CleanReturnAsync();
    }

    private static bool Greeted(WFStrangerComponent stranger, EntityUid player)
    {
        var greeted = stranger.Greeted;
        return greeted.ContainsKey(player);
    }

    /// <summary>A camp at the origin with a stranger of each prototype in <paramref name="members"/> at the given x, and the player at <paramref name="playerX"/>.</summary>
    private static async Task<EntityUid[]> Camp(TestPair pair, TestMapData map, EntityUid player, float playerX, params (string Proto, float X)[] members)
    {
        var server = pair.Server;
        var spawned = new EntityUid[members.Length];
        await server.WaitPost(() =>
        {
            var xforms = server.System<SharedTransformSystem>();
            xforms.SetCoordinates(player, new EntityCoordinates(map.Grid.Owner, playerX, 0.5f));
            server.EntMan.SpawnEntity("WFStrangerCampHome", new EntityCoordinates(map.Grid.Owner, 0.5f, 0.5f));
            for (var i = 0; i < members.Length; i++)
                spawned[i] = server.EntMan.SpawnEntity(members[i].Proto, new EntityCoordinates(map.Grid.Owner, members[i].X, 0.5f));
        });
        return spawned;
    }

    [Test]
    public async Task ACamperSetsOffToMeetAVisitorAndTheRestStayPut()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var (map, player) = await Rig(pair, 20);
        // Both see the player at the camp's edge; the mugger is nearer.
        var camp = await Camp(pair, map, player, 13.5f, ("WFStrangerMugger", 4.5f), ("WFStrangerBeggar", 0.5f));
        var mugger = camp[0];
        var beggar = camp[1];
        await RunUntil(pair, () => entMan.GetComponent<WFStrangerComponent>(mugger).Hailed, 45);
        await server.WaitAssertion(() =>
        {
            var stranger = entMan.GetComponent<WFStrangerComponent>(mugger);
            var other = entMan.GetComponent<WFStrangerComponent>(beggar);
            Assert.That(stranger.Home, Is.Not.Null, "Spawned in the camp, it should be his home.");
            Assert.That(stranger.Target, Is.EqualTo(player), "The nearest camper should go out to meet the visitor.");
            Assert.That(stranger.Hailed, Is.True, $"He should walk up and hail them: {Stuck(pair, mugger, player)}.");
            Assert.That(stranger.Hostile, Is.False, "Meeting a visitor isn't a fight.");
            Assert.That(Greeted(stranger, player), Is.True, "He should greet them on his way over.");
            Assert.That(other.Target, Is.Null, "Only one camper takes a visitor at a time.");
            Assert.That(other.Hailed, Is.False);
            Assert.That(other.Hostile, Is.False);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task WalkingIntoTheCampIsNotHostile()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var (map, player) = await Rig(pair, 20);
        var camp = await Camp(pair, map, player, 5.5f, ("WFStrangerMugger", 1.5f), ("WFStrangerBeggar", -1.5f));
        bool Angry() => camp.Any(member => entMan.GetComponent<WFStrangerComponent>(member).Hostile);
        bool Hailing() => camp.Any(member => entMan.GetComponent<WFStrangerComponent>(member).Hailed);
        await RunUntil(pair, () => Hailing() || Angry(), 30);
        await pair.RunSeconds(2);
        await server.WaitAssertion(() =>
        {
            Assert.That(Angry(), Is.False, "Walking into the camp shouldn't turn anyone on you.");
            Assert.That(Hailing(), Is.True, "Someone should have come over to talk.");
        });
        await pair.CleanReturnAsync();
    }

    [TestCase("WFStrangerMugger", 1)]
    [TestCase("WFStrangerDealer", 2)]
    public async Task RefusingACamperTurnsTheWholeCampHostile(string proto, int refuse)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var (map, player) = await Rig(pair, 20);
        var camp = await Camp(pair, map, player, 13.5f, (proto, 4.5f), ("WFStrangerBeggar", 0.5f));
        var talker = camp[0];
        var mate = camp[1];
        await RunUntil(pair, () => entMan.GetComponent<WFStrangerComponent>(talker).Hailed, 45);
        var strangers = server.System<WFStrangerSystem>();
        await server.WaitAssertion(() =>
        {
            var stranger = entMan.GetComponent<WFStrangerComponent>(talker);
            Assert.That(stranger.Hailed, Is.True, $"He never hailed the visitor: {Stuck(pair, talker, player)}.");
            Assert.That(entMan.GetComponent<WFStrangerComponent>(mate).Hostile, Is.False, "Not before the talk goes wrong.");

            strangers.StartConversation((talker, stranger), player);
            strangers.Choose((talker, stranger), player, refuse);

            Assert.That(stranger.Hostile, Is.True, "Refused, he should turn on the visitor.");
            Assert.That(entMan.GetComponent<FactionExceptionComponent>(talker).Hostiles, Does.Contain(player));
            Assert.That(entMan.GetComponent<WFStrangerComponent>(mate).Hostile, Is.True, "...and the rest of the camp with him.");
            Assert.That(entMan.GetComponent<FactionExceptionComponent>(mate).Hostiles, Does.Contain(player));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TheCamperWalksHomeAfterTheTalk()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var (map, player) = await Rig(pair, 20);
        var camp = await Camp(pair, map, player, 13.5f, ("WFStrangerBeggar", 4.5f));
        var beggar = camp[0];
        var home = new Vector2(0.5f, 0.5f);
        float Distance() => Vector2.Distance(entMan.GetComponent<TransformComponent>(beggar).LocalPosition, home);
        await RunUntil(pair, () => entMan.GetComponent<WFStrangerComponent>(beggar).Hailed, 45);

        var strangers = server.System<WFStrangerSystem>();
        var radius = 0f;
        await server.WaitAssertion(() =>
        {
            var stranger = entMan.GetComponent<WFStrangerComponent>(beggar);
            Assert.That(stranger.Hailed, Is.True, $"He never hailed the visitor: {Stuck(pair, beggar, player)}.");
            radius = entMan.GetComponent<WFStrangerHomeComponent>(stranger.Home!.Value).Radius;
            Assert.That(Distance(), Is.GreaterThan(radius), "He should have come out of the camp to meet them.");

            strangers.StartConversation((beggar, stranger), player);
            // "Not today."
            strangers.Choose((beggar, stranger), player, 1);
            Assert.That(stranger.Target, Is.Null, "The talk is over.");
            Assert.That(stranger.Hostile, Is.False);
        });
        await RunUntil(pair, () => Distance() < radius, 90);
        await server.WaitAssertion(() =>
            Assert.That(Distance(), Is.LessThan(radius), "Afterwards he should make his way back to the camp."));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ARoamerWandersOff()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var (map, player) = await Rig(pair, 30);
        EntityUid roamer = default;
        await server.WaitPost(() =>
        {
            // Out of his sight, close enough to keep him awake.
            server.System<SharedTransformSystem>().SetCoordinates(player, new EntityCoordinates(map.Grid.Owner, -25.5f, 0.5f));
            roamer = entMan.SpawnEntity("WFStrangerBeggar", new EntityCoordinates(map.Grid.Owner, 0.5f, 0.5f));
        });
        await RunUntil(pair, () => Vector2.Distance(entMan.GetComponent<TransformComponent>(roamer).LocalPosition, new Vector2(0.5f, 0.5f)) > 2f, 120, delayMs: 100);
        await server.WaitAssertion(() =>
        {
            var position = entMan.GetComponent<TransformComponent>(roamer).LocalPosition;
            Assert.That(Vector2.Distance(position, new Vector2(0.5f, 0.5f)), Is.GreaterThan(2f), "With nobody about, he should wander.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TheSquatterAttacksOnSightAndDropsHisKey()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var (map, player) = await Rig(pair);
        EntityUid squatter = default, key = default;
        await server.WaitPost(() =>
        {
            squatter = entMan.SpawnEntity("WFStrangerSquatter", new EntityCoordinates(map.Grid.Owner, 5.5f, 0.5f));
            key = entMan.SpawnEntity("RMCVehicleKeyBlank", new EntityCoordinates(map.Grid.Owner, 5.5f, 0.5f));
            Assert.That(server.System<SharedHandsSystem>().TryPickupAnyHand(squatter, key), Is.True);
        });
        await RunUntil(pair, () => entMan.GetComponent<WFStrangerComponent>(squatter).Hostile, 15);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<WFStrangerComponent>(squatter).Hostile, Is.True, "He should go for the player on sight.");
            Assert.That(entMan.GetComponent<FactionExceptionComponent>(squatter).Hostiles, Does.Contain(player));

            server.System<MobStateSystem>().ChangeMobState(squatter, Content.Shared.Mobs.MobState.Dead);
        });
        await server.WaitRunTicks(5);
        await server.WaitAssertion(() =>
        {
            Assert.That(server.System<MobStateSystem>().IsDead(squatter), Is.True);
            Assert.That(server.System<SharedHandsSystem>().EnumerateHeld(squatter), Does.Not.Contain(key), "Dead, he should drop the key.");
            Assert.That(entMan.GetComponent<TransformComponent>(key).ParentUid, Is.Not.EqualTo(squatter));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TheStalkerTurnsUpAboardYourShip()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var (map, player) = await Rig(pair);
        EntityUid stalker = default, ship = default;
        await server.WaitPost(() =>
        {
            stalker = entMan.SpawnEntity("WFStrangerStalker", new EntityCoordinates(map.Grid.Owner, 7.5f, 0.5f));
#pragma warning disable RA0002 // The test shortens his patience.
            entMan.GetComponent<WFStrangerComponent>(stalker).BoardDelay = TimeSpan.FromSeconds(2);
#pragma warning restore RA0002
        });
        await RunUntil(pair, () => entMan.GetComponent<WFStrangerComponent>(stalker).Target == player, 15);
        await server.WaitAssertion(() =>
            Assert.That(entMan.GetComponent<WFStrangerComponent>(stalker).Target, Is.EqualTo(player), "The stalker never picked his mark."));

        await server.WaitPost(() =>
        {
            var maps = server.System<SharedMapSystem>();
            var grid = maps.CreateGridEntity(map.MapId);
            ship = grid.Owner;
            entMan.GetComponent<TransformComponent>(ship).LocalPosition = new Vector2(40, 0);
            var plating = server.ResolveDependency<ITileDefinitionManager>()["Plating"].TileId;
            for (var x = 0; x < 8; x++)
            for (var y = 0; y < 4; y++)
                maps.SetTile(ship, grid.Comp, new Vector2i(x, y), new Tile(plating));
            entMan.EnsureComponent<ShuttleComponent>(ship);
            var xforms = server.System<SharedTransformSystem>();
            xforms.SetCoordinates(player, new EntityCoordinates(ship, 0.5f, 0.5f));
        });
        await RunUntil(pair, () => entMan.GetComponent<TransformComponent>(stalker).GridUid == ship, 20);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<TransformComponent>(stalker).GridUid, Is.EqualTo(ship), "The stalker should turn up aboard.");
            var xforms = server.System<SharedTransformSystem>();
            Assert.That(Vector2.Distance(xforms.GetWorldPosition(stalker), xforms.GetWorldPosition(player)), Is.GreaterThan(3f),
                "...somewhere out of the way, not on top of his mark.");
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>Saying no nicely, or being too broke to buy, isn't worth a fight.</summary>
    [TestCase(new[] { 1 }, TestName = "APoliteNoLeavesTheDealerCalm(NotInterested)")]
    [TestCase(new[] { 0, 1 }, TestName = "APoliteNoLeavesTheDealerCalm(TooRichForMe)")]
    public async Task APoliteNoLeavesTheDealerCalm(int[] answers)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var (map, player) = await Rig(pair);
        EntityUid dealer = default;
        await server.WaitPost(() => dealer = entMan.SpawnEntity("WFStrangerDealer", new EntityCoordinates(map.Grid.Owner, 2.5f, 0.5f)));
        await server.WaitRunTicks(2);
        var strangers = server.System<WFStrangerSystem>();
        await server.WaitAssertion(() =>
        {
            var stranger = entMan.GetComponent<WFStrangerComponent>(dealer);
            strangers.StartConversation((dealer, stranger), player);
            foreach (var answer in answers)
                strangers.Choose((dealer, stranger), player, answer);
            Assert.That(stranger.Node, Is.Null, "The talk should be over.");
            Assert.That(stranger.Hostile, Is.False, "A polite no shouldn't turn him on you.");
            Assert.That(strangers.HoldsGrudge((dealer, stranger), player), Is.False);
            Assert.That(entMan.GetComponent<HTNComponent>(dealer).Enabled, Is.False, "He shouldn't be fighting.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task IgnoringTheDealerLeavesHimCalm()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var strangers = server.System<WFStrangerSystem>();
        var (map, player) = await Rig(pair);
        EntityUid dealer = default;
        await server.WaitPost(() => dealer = entMan.SpawnEntity("WFStrangerDealer", new EntityCoordinates(map.Grid.Owner, 4.5f, 0.5f)));
        await RunUntil(pair, () => entMan.GetComponent<WFStrangerComponent>(dealer).Hailed, 45);
        await server.WaitAssertion(() =>
            Assert.That(entMan.GetComponent<WFStrangerComponent>(dealer).Hailed, Is.True, $"He never hailed: {Stuck(pair, dealer, player)}."));
        // No answer: the hail times out as ignored.
        await pair.RunSeconds(20);
        await server.WaitAssertion(() =>
        {
            var stranger = entMan.GetComponent<WFStrangerComponent>(dealer);
            Assert.That(stranger.Hailed, Is.False, "The hail should have timed out.");
            Assert.That(stranger.Hostile, Is.False, "Being ignored shouldn't turn him on you.");
            Assert.That(strangers.HoldsGrudge((dealer, stranger), player), Is.False);
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>A roamer walks a leg, then stands about a good while before the next instead of turning straight round.</summary>
    [Test]
    public async Task ARoamerLingersBetweenLegs()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var (map, player) = await Rig(pair, 30);
        EntityUid roamer = default;
        await server.WaitPost(() =>
        {
            // Out of his sight, close enough to keep him awake.
            server.System<SharedTransformSystem>().SetCoordinates(player, new EntityCoordinates(map.Grid.Owner, -25.5f, 0.5f));
            roamer = entMan.SpawnEntity("WFStrangerBeggar", new EntityCoordinates(map.Grid.Owner, 0.5f, 0.5f));
        });
        WFStrangerComponent stranger = default!;
        await server.WaitPost(() =>
        {
            stranger = entMan.GetComponent<WFStrangerComponent>(roamer);
            // Short legs, so he never wanders into sight of the player and goes to talk instead.
#pragma warning disable RA0002
            stranger.WanderRange = 6f;
#pragma warning restore RA0002
        });
        // A leg the pathfinder couldn't route ends in a short retry, not a linger; on a slow runner that happens, so the
        // first leg he actually walks is the one judged.
        for (var leg = 0; leg < 4; leg++)
        {
            await RunUntil(pair, () => stranger.WanderUntil != null, 10);
            await server.WaitAssertion(() => Assert.That(stranger.WanderUntil, Is.Not.Null, "He should set off on a leg."));
            await RunUntil(pair, () => stranger.WanderUntil == null, 90, delayMs: 100);
            if (stranger.NextWander - server.Timing.CurTime > TimeSpan.FromSeconds(6))
                break;
        }

        Vector2 stopped = default;
        await server.WaitAssertion(() =>
        {
            Assert.That(stranger.WanderUntil, Is.Null, "The leg should end.");
            Assert.That(stranger.NextWander - server.Timing.CurTime, Is.GreaterThanOrEqualTo(TimeSpan.FromSeconds(25)),
                "He should linger at least half a minute where the leg ended.");
            stopped = entMan.GetComponent<TransformComponent>(roamer).LocalPosition;
        });
        await pair.RunSeconds(20);
        await server.WaitAssertion(() =>
        {
            Assert.That(stranger.WanderUntil, Is.Null, "He set off again too soon.");
            Assert.That(Vector2.Distance(entMan.GetComponent<TransformComponent>(roamer).LocalPosition, stopped), Is.LessThan(1f),
                "Lingering, he should stay put.");
        });
        await pair.CleanReturnAsync();
    }
}
