namespace Content.Client._WF.Loadouts;

/// <summary>Replays how the server pays for a loadout at spawn, so the window can warn before the player saves.</summary>
public static class LoadoutBudget
{
    /// <summary>
    /// Walks the picks in the order the server equips them and returns the indices it will not issue. A pick is
    /// issued while its price fits what is left; a negative price costs nothing.
    /// </summary>
    /// <param name="prices">Price of every pick, ordered by the role's groups and then by pick order.</param>
    /// <param name="balance">Bank balance plus savings.</param>
    /// <param name="cost">What the whole selection would cost if all of it were issued.</param>
    public static List<int> Unaffordable(IReadOnlyList<int> prices, long balance, out long cost)
    {
        var dropped = new List<int>();
        var left = balance;
        cost = 0;

        for (var i = 0; i < prices.Count; i++)
        {
            var price = prices[i];
            cost += Math.Max(0, price);

            if (price <= left)
                left -= Math.Max(0, price);
            else
                dropped.Add(i);
        }

        return dropped;
    }
}
