namespace Content.Shared._WF.Minigames;

/// <summary>
/// Every number the house plays by, per game and difficulty. The house edges these give are in the module README and
/// checked by <c>MinigameRulesTest</c>.
/// </summary>
public static class MinigameRules
{
    /// <summary>Win payout as a multiple of the stake. Higher or Lower pays its ladder instead.</summary>
    public static float Payout(MinigameKind kind, MinigameDifficulty difficulty)
    {
        var i = (int) difficulty;
        return kind switch
        {
            MinigameKind.OrePiles => new[] { 3f, 4f, 6f }[i],
            MinigameKind.LiarsDice => new[] { 3f, 3.5f, 6f }[i],
            MinigameKind.Lockbox => new[] { 2.5f, 4f, 6f }[i],
            MinigameKind.HigherLower => Top(HigherLowerLadder(difficulty)),
            MinigameKind.Memory => new[] { 2f, 3f, 4f }[i],
            MinigameKind.Riddles => new[] { 2f, 3f, 5f }[i],
            _ => 1f,
        };
    }

    private static float Top(float[] ladder) => ladder[ladder.Length - 1];

    #region Ore Piles

    public const int OrePilesMaxTake = 3;

    /// <summary>Starting piles the player, moving first, cannot win against perfect play.</summary>
    public static readonly int[] OrePilesLosingStarts = { 13, 17, 21 };

    /// <summary>Starting piles the player wins with perfect play.</summary>
    public static readonly int[] OrePilesWinningStarts = { 14, 15, 16, 18, 19, 20 };

    /// <summary>Share of deals that start on a losing pile for the player.</summary>
    public static float OrePilesLosingDeal(MinigameDifficulty difficulty) => new[] { 0.85f, 0.9f, 0.875f }[(int) difficulty];

    /// <summary>Chance per move that the NPC takes a random amount instead of the right one.</summary>
    public static float OrePilesMistake(MinigameDifficulty difficulty) => new[] { 0.06f, 0.04f, 0f }[(int) difficulty];

    #endregion

    #region Liar's Dice

    public const int LiarsDiceDice = 5;

    /// <summary>Rounds in a row the player must win; losing any round loses the game.</summary>
    public static int LiarsDiceRounds(MinigameDifficulty difficulty) => new[] { 2, 2, 3 }[(int) difficulty];

    /// <summary>Chance per turn that the NPC makes a careless bid or call.</summary>
    public static float LiarsDiceMistake(MinigameDifficulty difficulty) => new[] { 0.03f, 0f, 0f }[(int) difficulty];

    #endregion

    #region Lockbox

    public const int LockboxDigits = 4;

    public static int LockboxTries(MinigameDifficulty difficulty) => new[] { 5, 4, 3 }[(int) difficulty];

    #endregion

    #region Higher or Lower

    /// <summary>What a run pays after each right call, as a multiple of the stake. The top rung cashes out by itself.</summary>
    public static float[] HigherLowerLadder(MinigameDifficulty difficulty) => difficulty switch
    {
        MinigameDifficulty.Easy => new[] { 1.2f, 1.5f, 1.9f, 2.4f, 3f },
        MinigameDifficulty.Normal => new[] { 1.25f, 1.6f, 2f, 2.5f, 3.2f, 4f },
        _ => new[] { 1.3f, 1.7f, 2.2f, 2.9f, 3.8f, 4.8f, 6f },
    };

    #endregion

    #region Memory

    public const int MemoryPads = 4;

    public static int MemoryLength(MinigameDifficulty difficulty) => new[] { 7, 10, 13 }[(int) difficulty];

    public static float MemoryFlash(MinigameDifficulty difficulty) => new[] { 0.6f, 0.45f, 0.3f }[(int) difficulty];

    #endregion

    #region Riddles

    /// <summary>Riddles in the Fluent pool, <c>wf-minigame-riddle-N-*</c> for N from 1.</summary>
    public const int RiddleCount = 24;

    /// <summary>Riddles in a row the player must answer right.</summary>
    public static int RiddlesToWin(MinigameDifficulty difficulty) => new[] { 2, 3, 4 }[(int) difficulty];

    #endregion

    /// <summary>Deals a new game.</summary>
    public static MinigameGame Create(MinigameKind kind, MinigameDifficulty difficulty, System.Random random)
    {
        return kind switch
        {
            MinigameKind.OrePiles => new OrePilesGame(difficulty, random),
            MinigameKind.LiarsDice => new LiarsDiceGame(difficulty, random),
            MinigameKind.Lockbox => new LockboxGame(difficulty, random),
            MinigameKind.HigherLower => new HigherLowerGame(difficulty, random),
            MinigameKind.Memory => new MemoryGame(difficulty, random),
            _ => new RiddleGame(difficulty, random),
        };
    }
}
