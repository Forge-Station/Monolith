namespace Content.Shared._WF.Minigames;

/// <summary>
/// Ore Piles, misere Nim: take 1 to 3 ore in turn, and whoever takes the last one loses. The player moves first.
/// Piles of 4n+1 lose for whoever must move; the house deals mostly those, and plays them perfectly on hard.
/// Move: Action = how many to take.
/// </summary>
public sealed class OrePilesGame : MinigameGame
{
    public int Pile { get; private set; }

    public override MinigameKind Kind => MinigameKind.OrePiles;

    public OrePilesGame(MinigameDifficulty difficulty, System.Random random, int? pile = null) : base(difficulty, random)
    {
        if (pile is { } fixedPile)
        {
            Pile = fixedPile;
        }
        else
        {
            var starts = random.NextSingle() < MinigameRules.OrePilesLosingDeal(difficulty)
                ? MinigameRules.OrePilesLosingStarts
                : MinigameRules.OrePilesWinningStarts;
            Pile = starts[random.Next(starts.Length)];
        }

        Say("wf-minigame-ore-start", ("pile", Pile));
    }

    /// <summary>Whether the side about to move loses against perfect play.</summary>
    public static bool IsLosing(int pile) => pile % 4 == 1;

    /// <summary>The winning take from a pile, or null if every take loses.</summary>
    public static int? PerfectTake(int pile) => IsLosing(pile) ? null : (pile - 1) % 4;

    protected override bool ApplyPlayerMove(MinigameMove move)
    {
        var take = move.Action;
        if (take < 1 || take > MinigameRules.OrePilesMaxTake || take > Pile)
            return false;

        Pile -= take;
        if (Pile == 0)
        {
            Status = MinigameStatus.Lost;
            Say("wf-minigame-ore-player-last");
            return true;
        }

        Status = MinigameStatus.NpcTurn;
        Say("wf-minigame-ore-player-took", ("count", take), ("pile", Pile));
        return true;
    }

    protected override void ApplyNpcMove()
    {
        var take = ChooseTake();
        Pile -= take;
        if (Pile == 0)
        {
            Status = MinigameStatus.Won;
            Say("wf-minigame-ore-npc-last");
            return;
        }

        Status = MinigameStatus.Playing;
        Say("wf-minigame-ore-npc-took", ("count", take), ("pile", Pile));
    }

    /// <summary>The right take, except for the odd slip on the easier tables; never the last ore unless forced.</summary>
    private int ChooseTake()
    {
        if (Pile == 1)
            return 1;

        var most = Math.Min(MinigameRules.OrePilesMaxTake, Pile - 1);
        if (PerfectTake(Pile) is { } perfect && Random.NextSingle() >= MinigameRules.OrePilesMistake(Difficulty))
            return perfect;

        return Random.Next(1, most + 1);
    }

    public override MinigameView GetView() => new OrePilesView(Pile, Math.Min(MinigameRules.OrePilesMaxTake, Pile));
}
