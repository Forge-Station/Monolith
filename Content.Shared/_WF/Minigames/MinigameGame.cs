using Robust.Shared.Serialization;

namespace Content.Shared._WF.Minigames;

/// <summary>A game an NPC can host.</summary>
[Serializable, NetSerializable]
public enum MinigameKind : byte
{
    OrePiles,
    LiarsDice,
    Lockbox,
    HigherLower,
    Memory,
    Riddles,
}

/// <summary>How hard the house makes it, and how much it pays.</summary>
[Serializable, NetSerializable]
public enum MinigameDifficulty : byte
{
    Easy,
    Normal,
    Hard,
}

/// <summary>Where a game stands.</summary>
[Serializable, NetSerializable]
public enum MinigameStatus : byte
{
    /// <summary>Waiting on the player.</summary>
    Playing,

    /// <summary>The NPC is taking its turn; player moves are refused.</summary>
    NpcTurn,
    Won,
    Lost,
}

/// <summary>A player's move. What the numbers mean depends on the game.</summary>
[Serializable, NetSerializable]
public record struct MinigameMove(int Action, int A = 0, int B = 0);

/// <summary>
/// Server-side rules of one game in progress. The server owns it and checks every move; the client only sees
/// <see cref="GetView"/>.
/// </summary>
public abstract class MinigameGame
{
    protected readonly System.Random Random;

    public readonly MinigameDifficulty Difficulty;

    public MinigameStatus Status { get; protected set; } = MinigameStatus.Playing;

    /// <summary>Fluent id of the last thing that happened, with its arguments.</summary>
    public string Line { get; protected set; } = "wf-minigame-line-start";

    public (string, object)[] LineArgs { get; protected set; } = Array.Empty<(string, object)>();

    protected MinigameGame(MinigameDifficulty difficulty, System.Random random)
    {
        Difficulty = difficulty;
        Random = random;
    }

    public abstract MinigameKind Kind { get; }

    public bool Finished => Status is MinigameStatus.Won or MinigameStatus.Lost;

    /// <summary>
    /// Payout as a multiple of the stake if the player stopped now and was paid. Fixed for most games; Higher or
    /// Lower grows it with the run.
    /// </summary>
    public virtual float PayoutMultiplier => MinigameRules.Payout(Kind, Difficulty);

    /// <summary>Applies a player move. False, with nothing changed, when it is not their turn or not legal.</summary>
    public bool TryPlayerMove(MinigameMove move)
    {
        if (Status != MinigameStatus.Playing)
            return false;

        return ApplyPlayerMove(move);
    }

    /// <summary>Takes the NPC's turn if it has one waiting.</summary>
    public void NpcMove()
    {
        if (Status != MinigameStatus.NpcTurn)
            return;

        ApplyNpcMove();
    }

    protected abstract bool ApplyPlayerMove(MinigameMove move);

    protected virtual void ApplyNpcMove()
    {
        Status = MinigameStatus.Playing;
    }

    /// <summary>What the player may see.</summary>
    public abstract MinigameView GetView();

    protected void Say(string line, params (string, object)[] args)
    {
        Line = line;
        LineArgs = args;
    }
}
