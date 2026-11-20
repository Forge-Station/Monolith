#nullable enable
using System.Linq;
using Content.IntegrationTests.Pair;
using Content.Server._WF.Minigames;
using Content.Server._WF.Traders;
using Content.Shared._WF.Minigames;
using Content.Shared._WF.Traders;
using Content.Shared.Stacks;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._WF.Minigames;

/// <summary>
/// Game hosts take the stake like any trader, pay a win, refuse a winner until the cooldown runs out and refuse
/// everyone past the daily cap; the server refuses moves out of turn, by someone else, or against the rules.
/// </summary>
[TestFixture]
public sealed class MinigameHostTest
{
    private const string Dealer = "WFMinigameDealer";
    private const string Miner = "WFMinigameOldMiner";
    private const int CardsOffer = 0;
    private const int DiceOffer = 1;
    private const int LockOffer = 2;

    /// <summary>Lays a floor, the host facing a table to the south, a customer at the counter and cash on it.</summary>
    private static async Task<(EntityUid Host, EntityUid Customer, EntityCoordinates Counter)> SetUp(TestPair pair, TestMapData map,
        string host, int cash)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        EntityUid hostUid = default, customer = default;
        var counter = new EntityCoordinates(map.Grid.Owner, 0.5f, -0.5f);
        await server.WaitPost(() =>
        {
            var maps = server.System<SharedMapSystem>();
            for (var x = -4; x < 6; x++)
            for (var y = -4; y < 6; y++)
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), map.Tile.Tile);

            entMan.SpawnEntity("Table", counter);
            hostUid = entMan.SpawnEntity(host, new EntityCoordinates(map.Grid.Owner, 0.5f, 0.5f));
            customer = entMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid.Owner, 1.5f, -1.5f));
            var trader = entMan.GetComponent<TraderComponent>(hostUid);
            var traders = server.System<TraderSystem>();
            traders.RefreshTable((hostUid, trader));
            Assert.That(trader.Table, Is.Not.Null, $"{host} found no table.");
            Assert.That(traders.TryStartConversation((hostUid, trader), customer), Is.True, "The customer couldn't talk to the host.");

            if (cash > 0)
            {
                var stack = entMan.SpawnEntity("SpaceCash", counter);
                server.System<SharedStackSystem>().SetCount(stack, cash);
            }
        });
        await server.WaitRunTicks(2);
        return (hostUid, customer, counter);
    }

    private static int ZoneCash(IEntityManager entMan, TraderSystem traders, EntityUid host)
    {
        return traders.GetZoneItems((host, entMan.GetComponent<TraderComponent>(host)))
            .Where(item => !entMan.IsQueuedForDeletion(item) && entMan.TryGetComponent<StackComponent>(item, out var stack) && stack.StackTypeId == "Credit")
            .Sum(item => entMan.GetComponent<StackComponent>(item).Count);
    }

    [Test]
    public async Task StakeIsTakenAndAWinIsPaid()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        const int Cash = 10000;
        var (host, customer, _) = await SetUp(pair, map, Dealer, Cash);
        var games = server.System<MinigameSystem>();
        var traders = server.System<TraderSystem>();
        var stake = 0;
        var prize = 0;

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFMinigameHostComponent>(host);
            stake = comp.Offers[LockOffer].Stake;
            prize = MinigameSystem.MaxPrize(comp.Offers[LockOffer]);

            // Through the dialogue: the option asks, the customer says yes.
            var action = new TraderActionEvent(host, customer, TraderAction.PlayMinigame, LockOffer.ToString());
            entMan.EventBus.RaiseLocalEvent(host, ref action);
            Assert.That(action.Handled, Is.True, "Nobody ran the game offer.");
            Assert.That(comp.PendingOffer, Is.EqualTo(LockOffer), "The host didn't ask to confirm the stake.");

            var confirmed = new TraderConfirmedEvent(host, customer, true);
            entMan.EventBus.RaiseLocalEvent(host, ref confirmed);
            Assert.That(comp.Game, Is.TypeOf<LockboxGame>(), "No game was dealt.");
            Assert.That(comp.Player, Is.EqualTo(customer));
        });
        await server.WaitRunTicks(3);

        await server.WaitAssertion(() =>
        {
            Assert.That(ZoneCash(entMan, traders, host), Is.EqualTo(Cash - stake), "The stake wasn't taken.");

            var comp = entMan.GetComponent<WFMinigameHostComponent>(host);
            var game = (LockboxGame) comp.Game!;
            Assert.That(games.TryMove((host, comp), customer, new MinigameMove(game.Code)), Is.True, "The right code was refused.");
            Assert.That(game.Status, Is.EqualTo(MinigameStatus.Won));
        });
        await server.WaitRunTicks(3);

        await server.WaitAssertion(() =>
        {
            Assert.That(ZoneCash(entMan, traders, host), Is.EqualTo(Cash - stake + prize), "The win wasn't paid.");
            Assert.That(entMan.GetComponent<WFMinigameHostComponent>(host).PaidOut, Is.EqualTo(prize));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task WinnersWaitAndTheCapHolds()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        const int Cash = 20000;
        var (host, customer, _) = await SetUp(pair, map, Dealer, Cash);
        var games = server.System<MinigameSystem>();
        var traders = server.System<TraderSystem>();
        var stake = 0;

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFMinigameHostComponent>(host);
            stake = comp.Offers[LockOffer].Stake;
            Assert.That(games.TryStartGame((host, comp), customer, LockOffer), Is.True);
            var game = (LockboxGame) comp.Game!;
            games.TryMove((host, comp), customer, new MinigameMove(game.Code));
            Assert.That(game.Status, Is.EqualTo(MinigameStatus.Won));
        });
        await server.WaitRunTicks(3);

        var afterWin = 0;
        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFMinigameHostComponent>(host);
            afterWin = ZoneCash(entMan, traders, host);
            Assert.That(games.CanPlay((host, comp), customer, CardsOffer, out var refusal), Is.False, "A winner was played again at once.");
            Assert.That(refusal, Is.Not.Empty);
            Assert.That(games.TryStartGame((host, comp), customer, CardsOffer), Is.False);

            // The cooldown runs out; now the cap is the only limit.
            comp.Cooldowns.Clear();
            Assert.That(games.CanPlay((host, comp), customer, CardsOffer, out _), Is.True, "Refused after the cooldown ran out.");
            comp.PaidOut = comp.DailyCap - MinigameSystem.MaxPrize(comp.Offers[CardsOffer]) + 1;
            Assert.That(games.CanPlay((host, comp), customer, CardsOffer, out _), Is.False, "Played past the daily cap.");
            Assert.That(games.TryStartGame((host, comp), customer, CardsOffer), Is.False);
        });
        await server.WaitRunTicks(3);

        await server.WaitAssertion(() =>
        {
            Assert.That(ZoneCash(entMan, traders, host), Is.EqualTo(afterWin), "A refused game still took a stake.");
            Assert.That(afterWin, Is.EqualTo(Cash - stake + MinigameSystem.MaxPrize(entMan.GetComponent<WFMinigameHostComponent>(host).Offers[LockOffer])));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task BadMovesAreRefused()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var (host, customer, _) = await SetUp(pair, map, Dealer, 5000);
        var games = server.System<MinigameSystem>();
        EntityUid stranger = default;

        await server.WaitAssertion(() =>
        {
            stranger = entMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid.Owner, -0.5f, -1.5f));
            var comp = entMan.GetComponent<WFMinigameHostComponent>(host);
            Assert.That(games.TryMove((host, comp), customer, new MinigameMove(LiarsDiceGame.Bid, 2, 3)), Is.False,
                "Moved with no game dealt.");
            Assert.That(games.TryStartGame((host, comp), customer, DiceOffer), Is.True);

            Assert.That(games.TryMove((host, comp), customer, new MinigameMove(LiarsDiceGame.Call)), Is.False, "Called with no bid.");
            Assert.That(games.TryMove((host, comp), customer, new MinigameMove(LiarsDiceGame.Bid, 2, 9)), Is.False, "Bid a nine.");
            Assert.That(games.TryMove((host, comp), stranger, new MinigameMove(LiarsDiceGame.Bid, 2, 3)), Is.False,
                "Someone else played the customer's game.");
            Assert.That(games.TryMove((host, comp), customer, new MinigameMove(LiarsDiceGame.Bid, 2, 3)), Is.True);
            Assert.That(comp.Game!.Status, Is.EqualTo(MinigameStatus.NpcTurn));
            Assert.That(games.TryMove((host, comp), customer, new MinigameMove(LiarsDiceGame.Bid, 3, 3)), Is.False,
                "Moved during the host's turn.");
        });

        // The host answers after its thinking delay.
        await server.WaitRunTicks(server.Timing.TickRate * 2);

        await server.WaitAssertion(() =>
        {
            var game = entMan.GetComponent<WFMinigameHostComponent>(host).Game!;
            Assert.That(game.Status, Is.Not.EqualTo(MinigameStatus.NpcTurn), "The host never took its turn.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task WalkingAwayLosesTheStake()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        const int Cash = 5000;
        var (host, customer, _) = await SetUp(pair, map, Dealer, Cash);
        var games = server.System<MinigameSystem>();
        var traders = server.System<TraderSystem>();

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFMinigameHostComponent>(host);
            Assert.That(games.TryStartGame((host, comp), customer, CardsOffer), Is.True);
            traders.EndConversation((host, entMan.GetComponent<TraderComponent>(host)));
            Assert.That(comp.Game, Is.Null, "The game outlived the conversation.");
        });
        await server.WaitRunTicks(3);

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFMinigameHostComponent>(host);
            Assert.That(comp.PaidOut, Is.Zero, "A walk-away was paid.");
            Assert.That(ZoneCash(entMan, traders, host), Is.EqualTo(Cash - comp.Offers[CardsOffer].Stake), "The stake came back.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The Old Wise Miner pays in goods: his NPC takes the last ore after its delay, and the ore lands on the crate.</summary>
    [Test]
    public async Task OldMinerPaysInOre()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var (host, customer, _) = await SetUp(pair, map, Miner, 1000);
        var games = server.System<MinigameSystem>();
        var traders = server.System<TraderSystem>();

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFMinigameHostComponent>(host);
            Assert.That(games.TryStartGame((host, comp), customer, 0), Is.True);

            // Rig a pile of two: the player takes one, the miner must take the last.
            comp.Game = new OrePilesGame(MinigameDifficulty.Easy, new System.Random(1), 2);
            Assert.That(games.TryMove((host, comp), customer, new MinigameMove(1)), Is.True);
            Assert.That(comp.Game.Status, Is.EqualTo(MinigameStatus.NpcTurn));
        });
        await server.WaitRunTicks(server.Timing.TickRate * 2);

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFMinigameHostComponent>(host);
            Assert.That(comp.Game!.Status, Is.EqualTo(MinigameStatus.Won), "The miner didn't take the last ore.");
            var zone = traders.GetZoneItems((host, entMan.GetComponent<TraderComponent>(host)));
            foreach (var reward in comp.Offers[0].Rewards)
            {
                Assert.That(zone.Any(item => entMan.GetComponent<MetaDataComponent>(item).EntityPrototype?.ID == reward.Id), Is.True,
                    $"No {reward} was paid out.");
            }

            Assert.That(ZoneCash(entMan, traders, host), Is.EqualTo(1000 - comp.Offers[0].Stake), "The miner paid cash as well.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The mapped hosts each find a table to play over, and every riddle has its text.</summary>
    [Test]
    public async Task MappedHostsAndRiddlesAreComplete()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var loc = server.ResolveDependency<ILocalizationManager>();
            for (var i = 1; i <= MinigameRules.RiddleCount; i++)
            {
                Assert.That(loc.HasString(RiddleGame.Question(i)), Is.True, $"Riddle {i} has no question.");
                Assert.That(loc.HasString($"wf-minigame-riddle-{i}-answer"), Is.True, $"Riddle {i} has no answer.");
                for (var w = 1; w < RiddleGame.Answers; w++)
                    Assert.That(loc.HasString($"wf-minigame-riddle-{i}-wrong-{w}"), Is.True, $"Riddle {i} lacks wrong answer {w}.");
            }

            var maps = server.System<SharedMapSystem>();
            var loader = server.System<MapLoaderSystem>();
            foreach (var (path, hosts) in new[]
                     {
                         ("/Maps/_WF/PlanetPois/roadside_inn.yml", new[] { "WFMinigameGambler" }),
                         ("/Maps/_Mono/Outpost/caelestinus_central.yml", new[] { "WFMinigameDealer", "WFMinigameSage" }),
                     })
            {
                MapId mapId;
                if (path.Contains("_Mono"))
                {
                    Assert.That(loader.TryLoadMap(new ResPath(path), out var loaded, out _), Is.True, path);
                    mapId = loaded!.Value.Comp.MapId;
                    maps.InitializeMap(mapId);
                }
                else
                {
                    maps.CreateMap(out mapId);
                    Assert.That(loader.TryLoadGrid(mapId, new ResPath(path), out _), Is.True, path);
                }

                foreach (var host in hosts)
                {
                    var found = entMan.EntityQuery<TraderComponent, MetaDataComponent>()
                        .Where(t => t.Item2.EntityPrototype?.ID == host && entMan.GetComponent<TransformComponent>(t.Item2.Owner).MapID == mapId)
                        .ToList();
                    Assert.That(found, Has.Count.EqualTo(1), $"{path} should hold one {host}.");
                    Assert.That(found[0].Item1.Table, Is.Not.Null, $"{host} in {path} has no table to play over.");
                }

                maps.DeleteMap(mapId);
            }
        });

        await pair.CleanReturnAsync();
    }
}
