using Robust.Shared.Serialization;

namespace Content.Shared._WF.Minigames;

/// <summary>The one window every hosted game uses.</summary>
[Serializable, NetSerializable]
public enum WFMinigameUiKey : byte
{
    Key,
}

/// <summary>The player made a move.</summary>
[Serializable, NetSerializable]
public sealed class MinigameMoveMessage : BoundUserInterfaceMessage
{
    public MinigameMove Move;

    public MinigameMoveMessage(MinigameMove move)
    {
        Move = move;
    }
}

/// <summary>Everything the game window shows.</summary>
[Serializable, NetSerializable]
public sealed class MinigameState : BoundUserInterfaceState
{
    public MinigameKind Kind;
    public MinigameDifficulty Difficulty;
    public MinigameStatus Status;

    /// <summary>Stake paid for this game.</summary>
    public int Stake;

    /// <summary>What a win pays, or what cashing out pays now in Higher or Lower.</summary>
    public string Prize;

    /// <summary>Last thing that happened, already localized.</summary>
    public string Line;

    public MinigameView View;

    public MinigameState(MinigameKind kind, MinigameDifficulty difficulty, MinigameStatus status, int stake,
        string prize, string line, MinigameView view)
    {
        Kind = kind;
        Difficulty = difficulty;
        Status = status;
        Stake = stake;
        Prize = prize;
        Line = line;
        View = view;
    }
}

/// <summary>What the player may see of a game.</summary>
[Serializable, NetSerializable]
public abstract class MinigameView;

/// <summary>Ore Piles: the pile and how much may be taken.</summary>
[Serializable, NetSerializable]
public sealed class OrePilesView : MinigameView
{
    public int Pile;
    public int MaxTake;

    public OrePilesView(int pile, int maxTake)
    {
        Pile = pile;
        MaxTake = maxTake;
    }
}

/// <summary>Liar's Dice: your dice, the standing bid and, once called, the NPC's dice.</summary>
[Serializable, NetSerializable]
public sealed class LiarsDiceView : MinigameView
{
    public List<int> PlayerDice;
    public int NpcDiceCount;

    /// <summary>The NPC's dice, shown only once a bid is called.</summary>
    public List<int>? NpcDice;
    public int BidQuantity;
    public int BidFace;
    public bool BidByPlayer;
    public int Round;
    public int RoundsToWin;

    public LiarsDiceView(List<int> playerDice, int npcDiceCount, List<int>? npcDice, int bidQuantity, int bidFace,
        bool bidByPlayer, int round, int roundsToWin)
    {
        PlayerDice = playerDice;
        NpcDiceCount = npcDiceCount;
        NpcDice = npcDice;
        BidQuantity = bidQuantity;
        BidFace = bidFace;
        BidByPlayer = bidByPlayer;
        Round = round;
        RoundsToWin = roundsToWin;
    }
}

/// <summary>One Lockbox guess and its feedback.</summary>
[Serializable, NetSerializable]
public record struct LockboxGuess(int Code, int Hits, int Near);

/// <summary>Lockbox: guesses so far and tries left. The code is shown once the game ends.</summary>
[Serializable, NetSerializable]
public sealed class LockboxView : MinigameView
{
    public int Digits;
    public int TriesLeft;
    public List<LockboxGuess> Guesses;
    public int? Code;

    public LockboxView(int digits, int triesLeft, List<LockboxGuess> guesses, int? code)
    {
        Digits = digits;
        TriesLeft = triesLeft;
        Guesses = guesses;
        Code = code;
    }
}

/// <summary>Higher or Lower: the face-up card and the run.</summary>
[Serializable, NetSerializable]
public sealed class HigherLowerView : MinigameView
{
    /// <summary>Rank 1 (two) to 13 (ace).</summary>
    public int Card;
    public int Suit;
    public int Streak;
    public int MaxStreak;

    /// <summary>Multiple paid on the next right call, or 0 at the top of the ladder.</summary>
    public float NextMultiplier;

    public HigherLowerView(int card, int suit, int streak, int maxStreak, float nextMultiplier)
    {
        Card = card;
        Suit = suit;
        Streak = streak;
        MaxStreak = maxStreak;
        NextMultiplier = nextMultiplier;
    }
}

/// <summary>Memory: the sequence to show, and how far the player has repeated it.</summary>
[Serializable, NetSerializable]
public sealed class MemoryView : MinigameView
{
    /// <summary>The sequence so far, shown again each time <see cref="ShowId"/> changes.</summary>
    public List<int> Sequence;

    public int ShowId;
    public int Entered;
    public int TargetLength;
    public int Pads;

    /// <summary>Seconds each pad stays lit when the sequence plays.</summary>
    public float Flash;

    public MemoryView(List<int> sequence, int showId, int entered, int targetLength, int pads, float flash)
    {
        Sequence = sequence;
        ShowId = showId;
        Entered = entered;
        TargetLength = targetLength;
        Pads = pads;
        Flash = flash;
    }
}

/// <summary>Riddles: the riddle and its four answers, already localized.</summary>
[Serializable, NetSerializable]
public sealed class RiddleView : MinigameView
{
    public string Question;
    public List<string> Answers;
    public int Number;
    public int Total;

    public RiddleView(string question, List<string> answers, int number, int total)
    {
        Question = question;
        Answers = answers;
        Number = number;
        Total = total;
    }
}
