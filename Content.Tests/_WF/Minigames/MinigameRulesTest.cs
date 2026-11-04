using System;
using System.Collections.Generic;
using System.Linq;
using Content.Shared._WF.Minigames;
using NUnit.Framework;

namespace Content.Tests._WF.Minigames;

/// <summary>
/// The games' rules engines: legal and illegal moves, wins and losses, and the house edge against the best play where
/// the game can be solved.
/// </summary>
[TestFixture]
public sealed class MinigameRulesTest
{
    private static readonly MinigameDifficulty[] Difficulties =
        { MinigameDifficulty.Easy, MinigameDifficulty.Normal, MinigameDifficulty.Hard };

    #region Ore Piles

    [Test]
    public void OrePilesRefusesIllegalTakes()
    {
        var game = new OrePilesGame(MinigameDifficulty.Hard, new Random(1), 5);
        Assert.That(game.TryPlayerMove(new MinigameMove(0)), Is.False, "Taking nothing is not a move.");
        Assert.That(game.TryPlayerMove(new MinigameMove(4)), Is.False, "At most three may be taken.");
        Assert.That(game.TryPlayerMove(new MinigameMove(2)), Is.True);
        Assert.That(game.Status, Is.EqualTo(MinigameStatus.NpcTurn));
        Assert.That(game.TryPlayerMove(new MinigameMove(1)), Is.False, "The player moved during the NPC's turn.");
        Assert.That(game.Pile, Is.EqualTo(3));
    }

    [Test]
    public void OrePilesLastOreLoses()
    {
        var game = new OrePilesGame(MinigameDifficulty.Hard, new Random(1), 2);
        Assert.That(game.TryPlayerMove(new MinigameMove(3)), Is.False, "Took more than the pile.");
        Assert.That(game.TryPlayerMove(new MinigameMove(2)), Is.True);
        Assert.That(game.Status, Is.EqualTo(MinigameStatus.Lost), "Taking the last ore should lose.");

        game = new OrePilesGame(MinigameDifficulty.Hard, new Random(1), 2);
        game.TryPlayerMove(new MinigameMove(1));
        game.NpcMove();
        Assert.That(game.Status, Is.EqualTo(MinigameStatus.Won), "The NPC was left the last ore and should lose.");
    }

    /// <summary>Every line of play from every losing deal, against the hard table: the player never wins.</summary>
    [Test]
    public void OrePilesHardIsUnbeatableFromALosingPile()
    {
        foreach (var start in MinigameRules.OrePilesLosingStarts)
        {
            Assert.That(OrePilesGame.IsLosing(start), Is.True, $"{start} is not a losing deal.");
            Assert.That(PlayerCanWin(start, new List<int>()), Is.False, $"The hard table can be beaten from {start}.");
        }
    }

    /// <summary>Replays a line of play on a fresh hard game and searches every reply from there.</summary>
    private static bool PlayerCanWin(int start, List<int> line)
    {
        for (var take = 1; take <= 3; take++)
        {
            var game = new OrePilesGame(MinigameDifficulty.Hard, new Random(7), start);
            foreach (var earlier in line)
            {
                game.TryPlayerMove(new MinigameMove(earlier));
                game.NpcMove();
            }

            if (!game.TryPlayerMove(new MinigameMove(take)))
                continue;

            game.NpcMove();
            if (game.Status == MinigameStatus.Won)
                return true;

            if (game.Status == MinigameStatus.Playing && PlayerCanWin(start, new List<int>(line) { take }))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Exact chance a perfect player beats each table, given the deal and the NPC's slips: the payout times it stays
    /// under the stake.
    /// </summary>
    [Test]
    public void OrePilesHouseEdgeIsPositive()
    {
        foreach (var difficulty in Difficulties)
        {
            var mistake = MinigameRules.OrePilesMistake(difficulty);
            var player = new Dictionary<int, double>();
            var npc = new Dictionary<int, double>();

            double Player(int pile)
            {
                if (player.TryGetValue(pile, out var known))
                    return known;

                var best = 0.0;
                for (var take = 1; take <= Math.Min(3, pile); take++)
                    best = Math.Max(best, take == pile ? 0 : Npc(pile - take));

                return player[pile] = best;
            }

            double Npc(int pile)
            {
                if (npc.TryGetValue(pile, out var known))
                    return known;

                if (pile == 1)
                    return npc[pile] = 1;

                var most = Math.Min(3, pile - 1);
                var chance = 0.0;
                var perfect = OrePilesGame.PerfectTake(pile);
                for (var take = 1; take <= most; take++)
                {
                    var p = perfect == null ? 1.0 / most : mistake / most + (take == perfect ? 1 - mistake : 0);
                    chance += p * Player(pile - take);
                }

                return npc[pile] = chance;
            }

            var losing = MinigameRules.OrePilesLosingStarts.Average(Player);
            var winning = MinigameRules.OrePilesWinningStarts.Average(Player);
            var deal = MinigameRules.OrePilesLosingDeal(difficulty);
            var win = deal * losing + (1 - deal) * winning;
            var ev = win * MinigameRules.Payout(MinigameKind.OrePiles, difficulty);
            TestContext.Out.WriteLine($"Ore Piles {difficulty}: perfect player wins {win:P2}, returns {ev:F3} per stake");
            Assert.That(ev, Is.LessThan(1), $"Ore Piles {difficulty} pays a perfect player more than the stake.");
        }
    }

    #endregion

    #region Higher or Lower

    /// <summary>Best stopping play, by exact search over the ladder: it still returns less than the stake.</summary>
    [Test]
    public void HigherLowerHouseEdgeIsPositive()
    {
        foreach (var difficulty in Difficulties)
        {
            var ladder = MinigameRules.HigherLowerLadder(difficulty);
            var memo = new Dictionary<(int, int), double>();

            double Value(int streak, int card)
            {
                if (memo.TryGetValue((streak, card), out var known))
                    return known;

                var cash = streak > 0 ? ladder[streak - 1] : 0;
                if (streak == ladder.Length)
                    return memo[(streak, card)] = cash;

                double higher = 0, lower = 0;
                for (var next = 1; next <= HigherLowerGame.Ranks; next++)
                {
                    if (next > card)
                        higher += Value(streak + 1, next) / HigherLowerGame.Ranks;
                    else if (next < card)
                        lower += Value(streak + 1, next) / HigherLowerGame.Ranks;
                }

                var go = Math.Max(higher, lower);
                return memo[(streak, card)] = streak > 0 ? Math.Max(cash, go) : go;
            }

            var ev = Enumerable.Range(1, HigherLowerGame.Ranks).Average(card => Value(0, card));
            TestContext.Out.WriteLine($"Higher or Lower {difficulty}: best play returns {ev:F3} per stake");
            Assert.That(ev, Is.LessThan(1), $"Higher or Lower {difficulty} pays the best play more than the stake.");
        }
    }

    [Test]
    public void HigherLowerCashOutAndTies()
    {
        var game = new HigherLowerGame(MinigameDifficulty.Normal, new Random(3));
        Assert.That(game.TryPlayerMove(new MinigameMove(HigherLowerGame.CashOut)), Is.False, "Cashed out before a right call.");
        Assert.That(game.TryPlayerMove(new MinigameMove(5)), Is.False, "Not a move.");

        // Play until a right call, then cash out at the first rung.
        for (var seed = 0; seed < 200; seed++)
        {
            game = new HigherLowerGame(MinigameDifficulty.Normal, new Random(seed));
            var call = game.Card <= 7 ? HigherLowerGame.Higher : HigherLowerGame.Lower;
            var before = game.Card;
            game.TryPlayerMove(new MinigameMove(call));
            if (game.Card == before)
            {
                Assert.That(game.Status, Is.EqualTo(MinigameStatus.Lost), "A tie should lose.");
                continue;
            }

            if (game.Status != MinigameStatus.Playing)
                continue;

            Assert.That(game.PayoutMultiplier, Is.EqualTo(MinigameRules.HigherLowerLadder(MinigameDifficulty.Normal)[0]));
            Assert.That(game.TryPlayerMove(new MinigameMove(HigherLowerGame.CashOut)), Is.True);
            Assert.That(game.Status, Is.EqualTo(MinigameStatus.Won));
            return;
        }

        Assert.Fail("No right call in 200 deals.");
    }

    #endregion

    #region Lockbox

    [TestCase(1234, 1234, 4, 0)]
    [TestCase(1234, 1243, 2, 2)]
    [TestCase(1122, 2211, 0, 4)]
    [TestCase(1111, 1222, 1, 0)]
    [TestCase(1234, 5678, 0, 0)]
    [TestCase(12, 1200, 0, 4)]
    public void LockboxScores(int code, int guess, int hits, int near)
    {
        Assert.That(LockboxGame.Score(code, guess, 4), Is.EqualTo((hits, near)));
    }

    [Test]
    public void LockboxOpensAndJams()
    {
        var game = new LockboxGame(MinigameDifficulty.Normal, new Random(1), 4821);
        Assert.That(game.TryPlayerMove(new MinigameMove(10000)), Is.False, "Five digits is not a code.");
        Assert.That(game.TryPlayerMove(new MinigameMove(-1)), Is.False);
        Assert.That(game.TryPlayerMove(new MinigameMove(1234)), Is.True);
        Assert.That(game.Guesses[0], Is.EqualTo(new LockboxGuess(1234, 0, 3)));
        Assert.That(game.TryPlayerMove(new MinigameMove(4821)), Is.True);
        Assert.That(game.Status, Is.EqualTo(MinigameStatus.Won));

        game = new LockboxGame(MinigameDifficulty.Normal, new Random(1), 4821);
        for (var i = 0; i < MinigameRules.LockboxTries(MinigameDifficulty.Normal); i++)
            game.TryPlayerMove(new MinigameMove(0));

        Assert.That(game.Status, Is.EqualTo(MinigameStatus.Lost), "Out of tries should jam the lock.");
        Assert.That(game.TryPlayerMove(new MinigameMove(4821)), Is.False, "Moved after the game ended.");
    }

    /// <summary>
    /// The tables with few tries are provably in the house's favour: each guess splits the codes left into at most 13
    /// answers that aren't a win, so no strategy opens more than 1 + 13 + 13^2 + ... codes within the tries.
    /// </summary>
    [Test]
    public void LockboxShortTablesFavourTheHouse()
    {
        var codes = Math.Pow(10, MinigameRules.LockboxDigits);
        foreach (var difficulty in new[] { MinigameDifficulty.Normal, MinigameDifficulty.Hard })
        {
            var reachable = 0.0;
            for (var i = 0; i < MinigameRules.LockboxTries(difficulty); i++)
                reachable += Math.Pow(13, i);

            var bound = Math.Min(1, reachable / codes) * MinigameRules.Payout(MinigameKind.Lockbox, difficulty);
            TestContext.Out.WriteLine($"Lockbox {difficulty}: at most {bound:F3} back per stake");
            Assert.That(bound, Is.LessThan(1), $"Lockbox {difficulty} could pay a perfect player more than the stake.");
        }
    }

    #endregion

    #region Liar's Dice

    [Test]
    public void LiarsDiceBidsMustRaise()
    {
        var game = new LiarsDiceGame(MinigameDifficulty.Normal, new Random(2));
        Assert.That(game.TryPlayerMove(new MinigameMove(LiarsDiceGame.Call)), Is.False, "Called with no bid.");
        Assert.That(game.TryPlayerMove(new MinigameMove(LiarsDiceGame.Bid, 0, 3)), Is.False, "Bid no dice.");
        Assert.That(game.TryPlayerMove(new MinigameMove(LiarsDiceGame.Bid, 2, 7)), Is.False, "Bid a seven.");
        Assert.That(game.TryPlayerMove(new MinigameMove(LiarsDiceGame.Bid, 11, 2)), Is.False, "Bid more dice than the table has.");
        Assert.That(game.TryPlayerMove(new MinigameMove(LiarsDiceGame.Bid, 2, 3)), Is.True);
        Assert.That(game.Status, Is.EqualTo(MinigameStatus.NpcTurn));
        Assert.That(game.TryPlayerMove(new MinigameMove(LiarsDiceGame.Bid, 3, 3)), Is.False, "Bid during the NPC's turn.");

        game.NpcMove();
        if (game.Status != MinigameStatus.Playing)
            return;

        Assert.That(game.BidByPlayer, Is.False);
        Assert.That(game.TryPlayerMove(new MinigameMove(LiarsDiceGame.Bid, game.BidQuantity, game.BidFace)), Is.False,
            "Matched the standing bid instead of raising it.");
    }

    [Test]
    public void LiarsDiceCallSettlesOnTheDice()
    {
        // Rig the cup: both hold five fours, so the NPC believes the opening bid and raises.
        var game = new LiarsDiceGame(MinigameDifficulty.Hard, new Random(5));
        Rig(game, 4, 4);
        game.TryPlayerMove(new MinigameMove(LiarsDiceGame.Bid, 5, 4));
        game.NpcMove();
        Assert.That(game.Status, Is.EqualTo(MinigameStatus.Playing), "The NPC holds five fours itself; it shouldn't call.");
        Assert.That(game.BidByPlayer, Is.False);

        // Whatever it bid, calling it settles on the real count.
        var count = game.PlayerDice.Count(d => d == game.BidFace) + game.NpcDice.Count(d => d == game.BidFace);
        var lie = count < game.BidQuantity;
        game.TryPlayerMove(new MinigameMove(LiarsDiceGame.Call));
        Assert.That(game.RoundsWon, Is.EqualTo(lie ? 1 : 0), "The call settled against the dice.");
        Assert.That(game.Status, Is.EqualTo(lie ? MinigameStatus.Playing : MinigameStatus.Lost));
    }

    private static void Rig(LiarsDiceGame game, int player, int npc)
    {
        for (var i = 0; i < game.PlayerDice.Count; i++)
            game.PlayerDice[i] = player;

        for (var i = 0; i < game.NpcDice.Count; i++)
            game.NpcDice[i] = npc;
    }

    /// <summary>
    /// Not solvable, so estimated: a sensible player bot (the NPC's own reasoning, from the other side) over many
    /// seeded games wins too rarely for the payout to beat the stake.
    /// </summary>
    [Test]
    public void LiarsDiceFavoursTheHouseAgainstASensiblePlayer()
    {
        const int games = 3000;
        foreach (var difficulty in Difficulties)
        {
            var random = new Random(11);
            var wins = 0;
            for (var i = 0; i < games; i++)
            {
                var game = new LiarsDiceGame(difficulty, new Random(random.Next()));
                while (!game.Finished)
                {
                    if (game.Status == MinigameStatus.NpcTurn)
                    {
                        game.NpcMove();
                        continue;
                    }

                    Assert.That(game.TryPlayerMove(PlayerBot(game)), Is.True, "The bot made an illegal move.");
                }

                if (game.Status == MinigameStatus.Won)
                    wins++;
            }

            var rate = (double) wins / games;
            var ev = rate * MinigameRules.Payout(MinigameKind.LiarsDice, difficulty);
            TestContext.Out.WriteLine($"Liar's Dice {difficulty}: sensible player wins {rate:P1}, returns {ev:F3} per stake");
            Assert.That(ev, Is.LessThan(1), $"Liar's Dice {difficulty} pays a sensible player more than the stake.");
        }
    }

    /// <summary>Calls a bid it thinks is less likely than not; otherwise makes the raise it thinks likeliest.</summary>
    private static MinigameMove PlayerBot(LiarsDiceGame game)
    {
        var unseen = game.NpcDice.Count;
        double Belief(int quantity, int face)
        {
            var have = game.PlayerDice.Count(d => d == face);
            var read = game.BidQuantity > 0 && !game.BidByPlayer && face == game.BidFace ? 1 : 0;
            return LiarsDiceGame.Tail(unseen, quantity - have - read);
        }

        if (game.BidQuantity > 0 && !game.BidByPlayer && Belief(game.BidQuantity, game.BidFace) < 0.5)
            return new MinigameMove(LiarsDiceGame.Call);

        var best = (Quantity: 0, Face: 0);
        var bestBelief = -1.0;
        for (var quantity = Math.Max(1, game.BidQuantity); quantity <= game.PlayerDice.Count + unseen; quantity++)
        {
            for (var face = 1; face <= 6; face++)
            {
                if (!game.IsRaise(quantity, face))
                    continue;

                var belief = Math.Round(Belief(quantity, face), 6);
                if (belief > bestBelief)
                {
                    best = (quantity, face);
                    bestBelief = belief;
                }
            }
        }

        return best.Quantity == 0
            ? new MinigameMove(LiarsDiceGame.Call)
            : new MinigameMove(LiarsDiceGame.Bid, best.Quantity, best.Face);
    }

    #endregion

    #region Memory and riddles

    [Test]
    public void MemoryRepeatsToTheTarget()
    {
        var game = new MemoryGame(MinigameDifficulty.Easy, new Random(4));
        Assert.That(game.TryPlayerMove(new MinigameMove(MinigameRules.MemoryPads)), Is.False, "No such pad.");
        while (game.Status == MinigameStatus.Playing)
        {
            var shown = game.ShowId;
            foreach (var pad in game.Sequence.ToList())
                Assert.That(game.TryPlayerMove(new MinigameMove(pad)), Is.True);

            Assert.That(game.Status == MinigameStatus.Won || game.ShowId != shown, Is.True, "A right sequence should play again longer.");
        }

        Assert.That(game.Status, Is.EqualTo(MinigameStatus.Won));
        Assert.That(game.Sequence, Has.Count.EqualTo(MinigameRules.MemoryLength(MinigameDifficulty.Easy)));

        game = new MemoryGame(MinigameDifficulty.Easy, new Random(4));
        game.TryPlayerMove(new MinigameMove((game.Sequence[0] + 1) % MinigameRules.MemoryPads));
        Assert.That(game.Status, Is.EqualTo(MinigameStatus.Lost), "A wrong pad should lose.");
    }

    [Test]
    public void RiddlesNeedEveryAnswerRight()
    {
        var game = new RiddleGame(MinigameDifficulty.Hard, new Random(9));
        Assert.That(game.Riddles.Distinct().Count(), Is.EqualTo(MinigameRules.RiddlesToWin(MinigameDifficulty.Hard)), "A riddle came up twice.");
        Assert.That(game.TryPlayerMove(new MinigameMove(RiddleGame.Answers)), Is.False, "No such answer.");
        while (game.Status == MinigameStatus.Playing)
        {
            Assert.That(game.Shown[game.Correct], Does.EndWith("-answer"));
            game.TryPlayerMove(new MinigameMove(game.Correct));
        }

        Assert.That(game.Status, Is.EqualTo(MinigameStatus.Won));

        game = new RiddleGame(MinigameDifficulty.Hard, new Random(9));
        game.TryPlayerMove(new MinigameMove((game.Correct + 1) % RiddleGame.Answers));
        Assert.That(game.Status, Is.EqualTo(MinigameStatus.Lost), "A wrong answer should lose.");
    }

    #endregion
}
