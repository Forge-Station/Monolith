namespace Content.Server._WF.Defcon;

/// <summary>The DEFCON level and its timers. Rises wait for confirmation and may skip levels; falls go one step at a time.</summary>
public sealed class WFDefconTracker
{
    public const int Calmest = 5;
    public const int Severest = 1;

    public int Level = Calmest;
    public TimeSpan LevelSince;

    /// <summary>When the target first went above the level, while it stays there.</summary>
    public TimeSpan? RiseSince;

    /// <summary>When the target first went below the level, while it stays there.</summary>
    public TimeSpan? FallSince;

    /// <summary>The level a score asks for, never calmer than <paramref name="floor"/>.</summary>
    public static int Target(float score, IReadOnlyList<float> thresholds, int floor = Calmest)
    {
        var level = Calmest;
        for (var i = 0; i < thresholds.Count && i < Calmest - Severest; i++)
        {
            if (score >= thresholds[i])
                level = Calmest - 1 - i;
        }

        return Math.Clamp(Math.Min(level, floor), Severest, Calmest);
    }

    /// <summary>Moves toward a target level. Returns true when the level changed.</summary>
    public bool Step(int target, TimeSpan now, TimeSpan riseConfirm, TimeSpan fallDelay, TimeSpan minDwell)
    {
        if (target < Level)
        {
            FallSince = null;
            RiseSince ??= now;
            if (now - RiseSince < riseConfirm)
                return false;

            Set(target, now);
            return true;
        }

        if (target > Level)
        {
            RiseSince = null;
            FallSince ??= now;
            if (now - FallSince < fallDelay || now - LevelSince < minDwell)
                return false;

            Set(Level + 1, now);
            return true;
        }

        RiseSince = null;
        FallSince = null;
        return false;
    }

    /// <summary>Sets the level at once and clears the timers.</summary>
    public void Set(int level, TimeSpan now)
    {
        Level = Math.Clamp(level, Severest, Calmest);
        LevelSince = now;
        RiseSince = null;
        FallSince = null;
    }
}
