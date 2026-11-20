namespace Content.Shared._WF.Minigames;

/// <summary>
/// Lockbox, a codebreaker: guess a code of digits 0-9 (repeats allowed). Each guess reports hits (right digit, right
/// place) and near (right digit, wrong place). Open it within the tries allowed. Move: Action = the guess.
/// </summary>
public sealed class LockboxGame : MinigameGame
{
    public readonly int Code;
    public readonly List<LockboxGuess> Guesses = new();

    public override MinigameKind Kind => MinigameKind.Lockbox;

    private static int Digits => MinigameRules.LockboxDigits;

    private int Tries => MinigameRules.LockboxTries(Difficulty);

    public LockboxGame(MinigameDifficulty difficulty, System.Random random, int? code = null) : base(difficulty, random)
    {
        Code = code ?? random.Next((int) Math.Pow(10, Digits));
        Say("wf-minigame-lock-start", ("digits", Digits), ("tries", Tries));
    }

    /// <summary>Hits and near for a guess against a code, both <paramref name="digits"/> long.</summary>
    public static (int Hits, int Near) Score(int code, int guess, int digits)
    {
        var codeCounts = new int[10];
        var guessCounts = new int[10];
        var hits = 0;
        for (var i = 0; i < digits; i++)
        {
            var c = code % 10;
            var g = guess % 10;
            code /= 10;
            guess /= 10;
            if (c == g)
                hits++;

            codeCounts[c]++;
            guessCounts[g]++;
        }

        var common = 0;
        for (var d = 0; d < 10; d++)
            common += Math.Min(codeCounts[d], guessCounts[d]);

        return (hits, common - hits);
    }

    protected override bool ApplyPlayerMove(MinigameMove move)
    {
        if (move.Action < 0 || move.Action >= (int) Math.Pow(10, Digits))
            return false;

        var (hits, near) = Score(Code, move.Action, Digits);
        Guesses.Add(new LockboxGuess(move.Action, hits, near));

        if (hits == Digits)
        {
            Status = MinigameStatus.Won;
            Say("wf-minigame-lock-open", ("tries", Guesses.Count));
        }
        else if (Guesses.Count >= Tries)
        {
            Status = MinigameStatus.Lost;
            Say("wf-minigame-lock-jammed", ("code", Code.ToString("D" + Digits)));
        }
        else
        {
            Say("wf-minigame-lock-feedback", ("hits", hits), ("near", near), ("left", Tries - Guesses.Count));
        }

        return true;
    }

    public override MinigameView GetView()
    {
        return new LockboxView(Digits, Tries - Guesses.Count, new List<LockboxGuess>(Guesses), Finished ? Code : null);
    }
}
