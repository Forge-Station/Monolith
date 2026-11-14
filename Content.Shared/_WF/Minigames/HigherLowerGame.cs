namespace Content.Shared._WF.Minigames;

/// <summary>
/// Higher or Lower: call whether the next card ranks higher or lower than the one showing. Each card is drawn from a
/// fresh shuffled deck, so counting doesn't help, and a tie loses. Every right call climbs the payout ladder; cash out
/// after any right call, and the top rung cashes out by itself. Move: Action 0 higher, 1 lower, 2 cash out.
/// </summary>
public sealed class HigherLowerGame : MinigameGame
{
    public const int Higher = 0;
    public const int Lower = 1;
    public const int CashOut = 2;

    public const int Ranks = 13;

    private readonly float[] _ladder;

    public int Card { get; private set; }
    public int Suit { get; private set; }
    public int Streak { get; private set; }

    public override MinigameKind Kind => MinigameKind.HigherLower;

    public override float PayoutMultiplier => Streak > 0 ? _ladder[Streak - 1] : 0f;

    public HigherLowerGame(MinigameDifficulty difficulty, System.Random random) : base(difficulty, random)
    {
        _ladder = MinigameRules.HigherLowerLadder(difficulty);
        Draw();
        Say("wf-minigame-cards-start", ("top", _ladder[_ladder.Length - 1]));
    }

    private void Draw()
    {
        Card = Random.Next(1, Ranks + 1);
        Suit = Random.Next(4);
    }

    protected override bool ApplyPlayerMove(MinigameMove move)
    {
        if (move.Action == CashOut)
        {
            if (Streak == 0)
                return false;

            Status = MinigameStatus.Won;
            Say("wf-minigame-cards-cashed", ("multiple", PayoutMultiplier));
            return true;
        }

        if (move.Action is not (Higher or Lower))
            return false;

        var old = Card;
        Draw();
        var right = move.Action == Higher ? Card > old : Card < old;
        if (!right)
        {
            Status = MinigameStatus.Lost;
            Say(Card == old ? "wf-minigame-cards-tie" : "wf-minigame-cards-wrong");
            return true;
        }

        Streak++;
        if (Streak >= _ladder.Length)
        {
            Status = MinigameStatus.Won;
            Say("wf-minigame-cards-top", ("multiple", PayoutMultiplier));
            return true;
        }

        Say("wf-minigame-cards-right", ("streak", Streak), ("multiple", PayoutMultiplier));
        return true;
    }

    public override MinigameView GetView()
    {
        var next = Streak < _ladder.Length ? _ladder[Streak] : 0f;
        return new HigherLowerView(Card, Suit, Streak, _ladder.Length, next);
    }
}
