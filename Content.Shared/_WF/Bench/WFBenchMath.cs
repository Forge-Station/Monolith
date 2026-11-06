namespace Content.Shared._WF.Bench;

/// <summary>The arithmetic of tickets and Bench debt, kept apart from the systems so it can be tested on its own.</summary>
public static class WFBenchMath
{
    /// <summary>Most a custom charge or posted ordinance may fine.</summary>
    public const int MaxCustomFine = 100_000;

    /// <summary>Most charges one ticket may carry.</summary>
    public const int MaxCharges = 8;

    /// <summary>A ticket's total fine and penalty level: the sum of its fines and the highest of its levels.</summary>
    public static (int Total, WFPenaltyLevel Level) Sum(IEnumerable<(int Fine, WFPenaltyLevel Level)> charges)
    {
        long total = 0;
        var level = WFPenaltyLevel.Citation;
        foreach (var (fine, charge) in charges)
        {
            total += Math.Max(0, fine);
            if (charge > level)
                level = charge;
        }

        return ((int) Math.Min(total, int.MaxValue), level);
    }

    /// <summary>
    /// A ticket's fine after the soft limits: no more than the per-ticket cap, nor than what is left of the offender's
    /// cap for the round given what they were already fined.
    /// </summary>
    public static int Cap(int total, int ticketCap, int alreadyFined, int roundCap)
    {
        var left = Math.Max(0, roundCap - Math.Max(0, alreadyFined));
        return Math.Max(0, Math.Min(total, Math.Min(Math.Max(0, ticketCap), left)));
    }

    /// <summary>A law's fine as set by a judge, kept within the law's range.</summary>
    public static int Clamp(int fine, int min, int max)
    {
        if (max < min)
            max = min;
        return Math.Clamp(fine, Math.Max(0, min), max);
    }

    /// <summary>How much of a fine is taken straight away from a balance.</summary>
    public static int Collect(int balance, int fine)
    {
        return Math.Clamp(balance, 0, Math.Max(0, fine));
    }

    /// <summary>How much of a gain goes to Bench debt: its share, rounded down but at least 1, and never more than is owed.</summary>
    public static int Garnish(int gained, float share, int owed)
    {
        if (gained <= 0 || owed <= 0 || share <= 0f)
            return 0;

        var cut = Math.Max(1, (int) (gained * Math.Clamp(share, 0f, 1f)));
        return Math.Min(cut, owed);
    }
}
