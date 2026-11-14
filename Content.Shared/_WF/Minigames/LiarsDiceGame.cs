namespace Content.Shared._WF.Minigames;

/// <summary>
/// Liar's Dice against one NPC, five dice each, no wild ones. The player opens each round. A bid says at least that
/// many dice on the table show that face; the next bid must be more dice, or as many of a higher face. Calling "liar"
/// shows the dice: a true bid loses the caller the round, a false one loses the bidder. Win the set number of rounds
/// in a row to win. Move: Action 0 bids A dice of face B, Action 1 calls.
/// </summary>
public sealed class LiarsDiceGame : MinigameGame
{
    public const int Bid = 0;
    public const int Call = 1;

    public readonly List<int> PlayerDice = new();
    public readonly List<int> NpcDice = new();
    private List<int>? _revealed;

    public int BidQuantity { get; private set; }
    public int BidFace { get; private set; }
    public bool BidByPlayer { get; private set; }

    /// <summary>Rounds won so far.</summary>
    public int RoundsWon { get; private set; }

    public override MinigameKind Kind => MinigameKind.LiarsDice;

    private int RoundsToWin => MinigameRules.LiarsDiceRounds(Difficulty);

    private int Total => PlayerDice.Count + NpcDice.Count;

    public LiarsDiceGame(MinigameDifficulty difficulty, System.Random random) : base(difficulty, random)
    {
        Roll();
        Say("wf-minigame-dice-start", ("rounds", RoundsToWin));
    }

    private void Roll()
    {
        PlayerDice.Clear();
        NpcDice.Clear();
        for (var i = 0; i < MinigameRules.LiarsDiceDice; i++)
        {
            PlayerDice.Add(Random.Next(1, 7));
            NpcDice.Add(Random.Next(1, 7));
        }

        BidQuantity = 0;
        BidFace = 0;
        BidByPlayer = false;
    }

    /// <summary>Whether a bid beats the standing one.</summary>
    public bool IsRaise(int quantity, int face)
    {
        if (quantity < 1 || quantity > Total || face < 1 || face > 6)
            return false;

        return quantity > BidQuantity || quantity == BidQuantity && face > BidFace;
    }

    protected override bool ApplyPlayerMove(MinigameMove move)
    {
        switch (move.Action)
        {
            case Bid:
                if (!IsRaise(move.A, move.B))
                    return false;

                BidQuantity = move.A;
                BidFace = move.B;
                BidByPlayer = true;
                Status = MinigameStatus.NpcTurn;
                Say("wf-minigame-dice-player-bid", ("count", BidQuantity), ("face", BidFace));
                return true;
            case Call:
                if (BidQuantity == 0 || BidByPlayer)
                    return false;

                Resolve(playerCalled: true);
                return true;
            default:
                return false;
        }
    }

    protected override void ApplyNpcMove()
    {
        if (ChooseNpcBid() is not { } bid)
        {
            Resolve(playerCalled: false);
            return;
        }

        BidQuantity = bid.Quantity;
        BidFace = bid.Face;
        BidByPlayer = false;
        Status = MinigameStatus.Playing;
        Say("wf-minigame-dice-npc-bid", ("count", BidQuantity), ("face", BidFace));
    }

    /// <summary>Shows the dice and settles the round.</summary>
    private void Resolve(bool playerCalled)
    {
        var count = 0;
        foreach (var die in PlayerDice)
        {
            if (die == BidFace)
                count++;
        }

        foreach (var die in NpcDice)
        {
            if (die == BidFace)
                count++;
        }

        var bidStands = count >= BidQuantity;
        var playerLost = playerCalled == bidStands;
        _revealed = new List<int>(NpcDice);

        if (playerLost)
        {
            Status = MinigameStatus.Lost;
            Say(playerCalled ? "wf-minigame-dice-player-called-wrong" : "wf-minigame-dice-npc-called-right",
                ("count", count), ("face", BidFace));
            return;
        }

        RoundsWon++;
        if (RoundsWon >= RoundsToWin)
        {
            Status = MinigameStatus.Won;
            Say(playerCalled ? "wf-minigame-dice-player-called-right" : "wf-minigame-dice-npc-called-wrong",
                ("count", count), ("face", BidFace));
            return;
        }

        Roll();
        Status = MinigameStatus.Playing;
        Say("wf-minigame-dice-round-won", ("count", count), ("face", BidFace), ("won", RoundsWon), ("rounds", RoundsToWin));
    }

    /// <summary>Chance at least <paramref name="needed"/> of <paramref name="unseen"/> dice show one face.</summary>
    public static double Tail(int unseen, int needed)
    {
        if (needed <= 0)
            return 1;

        if (needed > unseen)
            return 0;

        var sum = 0.0;
        for (var i = needed; i <= unseen; i++)
            sum += Choose(unseen, i) * Math.Pow(1 / 6.0, i) * Math.Pow(5 / 6.0, unseen - i);

        return sum;
    }

    private static double Choose(int n, int k)
    {
        var result = 1.0;
        for (var i = 1; i <= k; i++)
            result = result * (n - k + i) / i;

        return result;
    }

    /// <summary>
    /// How likely the NPC thinks a bid is, from its own dice and the player's hidden ones. It reads a bid on the same
    /// face as the player's standing bid as one more of that face in the player's hand.
    /// </summary>
    private double Belief(int quantity, int face)
    {
        var have = 0;
        foreach (var die in NpcDice)
        {
            if (die == face)
                have++;
        }

        var read = BidByPlayer && BidQuantity > 0 && face == BidFace ? 1 : 0;
        return Tail(PlayerDice.Count, quantity - have - read);
    }

    /// <summary>The NPC's raise, or null to call.</summary>
    private (int Quantity, int Face)? ChooseNpcBid()
    {
        var mistake = MinigameRules.LiarsDiceMistake(Difficulty);
        if (BidQuantity > 0 && Belief(BidQuantity, BidFace) < 0.5 && Random.NextSingle() >= mistake)
            return null;

        var options = new List<(int Quantity, int Face)>();
        for (var quantity = Math.Max(1, BidQuantity); quantity <= Total; quantity++)
        {
            for (var face = 1; face <= 6; face++)
            {
                if (IsRaise(quantity, face))
                    options.Add((quantity, face));
            }
        }

        if (options.Count == 0)
            return null;

        if (Random.NextSingle() < mistake)
            return options[Math.Min(options.Count - 1, Random.Next(4))];

        var best = options[0];
        var bestBelief = Math.Round(Belief(best.Quantity, best.Face), 6);
        foreach (var option in options)
        {
            // Likeliest first, then the smallest raise.
            var belief = Math.Round(Belief(option.Quantity, option.Face), 6);
            if (belief > bestBelief)
            {
                best = option;
                bestBelief = belief;
            }
        }

        return best;
    }

    public override MinigameView GetView()
    {
        return new LiarsDiceView(new List<int>(PlayerDice), NpcDice.Count, Finished ? new List<int>(NpcDice) : _revealed,
            BidQuantity, BidFace, BidByPlayer, RoundsWon + 1, RoundsToWin);
    }
}
