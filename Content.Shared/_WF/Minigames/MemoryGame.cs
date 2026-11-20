namespace Content.Shared._WF.Minigames;

/// <summary>
/// Memory: the pads light up in a sequence; press them back in order. Each time the whole sequence is right, it plays
/// again one longer. Reach the target length to win; one wrong press loses. Move: Action = the pad pressed.
/// </summary>
public sealed class MemoryGame : MinigameGame
{
    public const int StartLength = 3;

    public readonly List<int> Sequence = new();

    /// <summary>Presses of the current sequence made so far.</summary>
    public int Entered { get; private set; }

    /// <summary>Bumped each time the sequence should play again.</summary>
    public int ShowId { get; private set; }

    public override MinigameKind Kind => MinigameKind.Memory;

    private int Target => MinigameRules.MemoryLength(Difficulty);

    public MemoryGame(MinigameDifficulty difficulty, System.Random random) : base(difficulty, random)
    {
        for (var i = 0; i < StartLength; i++)
            Extend();

        Say("wf-minigame-memory-start", ("length", Target));
    }

    private void Extend()
    {
        Sequence.Add(Random.Next(MinigameRules.MemoryPads));
        Entered = 0;
        ShowId++;
    }

    protected override bool ApplyPlayerMove(MinigameMove move)
    {
        if (move.Action < 0 || move.Action >= MinigameRules.MemoryPads)
            return false;

        if (Sequence[Entered] != move.Action)
        {
            Status = MinigameStatus.Lost;
            Say("wf-minigame-memory-wrong", ("reached", Sequence.Count - 1));
            return true;
        }

        Entered++;
        if (Entered < Sequence.Count)
            return true;

        if (Sequence.Count >= Target)
        {
            Status = MinigameStatus.Won;
            Say("wf-minigame-memory-won");
            return true;
        }

        Extend();
        Say("wf-minigame-memory-next", ("length", Sequence.Count), ("target", Target));
        return true;
    }

    public override MinigameView GetView()
    {
        return new MemoryView(new List<int>(Sequence), ShowId, Entered, Target, MinigameRules.MemoryPads,
            MinigameRules.MemoryFlash(Difficulty));
    }
}
