using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;

namespace Content.Shared.Chemistry.Components;

/// <summary>
/// Purity grades for the reagents in a solution, keyed by reagent prototype. A reagent with no entry is standard grade.
/// </summary>
public sealed partial class Solution
{
    /// <summary>Grades at or above this are standard and are not stored.</summary>
    public const float StandardPurityThreshold = 0.999f;

    /// <summary>Grade of each graded reagent, 0-1. Null when every reagent is standard grade.</summary>
    [DataField]
    public Dictionary<string, float>? Purity;

    /// <summary>The grade of a reagent prototype, 1.0 when it has none.</summary>
    public float GetPurity(string prototype)
    {
        return Purity != null && Purity.TryGetValue(prototype, out var value) ? value : 1f;
    }

    /// <summary>Sets a reagent's grade; a standard grade removes the entry.</summary>
    public void SetPurity(string prototype, float value)
    {
        value = Math.Clamp(value, 0f, 1f);
        if (value >= StandardPurityThreshold)
        {
            if (Purity != null && Purity.Remove(prototype) && Purity.Count == 0)
                Purity = null;
            return;
        }

        Purity ??= new Dictionary<string, float>();
        Purity[prototype] = value;
    }

    /// <summary>Sets a reagent's grade to the volume-weighted mean of its existing stock and an added amount.</summary>
    public void BlendPurity(string prototype, FixedPoint2 existingQuantity, FixedPoint2 addedQuantity, float addedPurity)
    {
        if (addedQuantity <= FixedPoint2.Zero)
            return;

        var existing = existingQuantity.Float();
        if (existing <= 0f)
        {
            SetPurity(prototype, addedPurity);
            return;
        }

        var added = addedQuantity.Float();
        SetPurity(prototype, (existing * GetPurity(prototype) + added * addedPurity) / (existing + added));
    }

    /// <summary>A reagent's quantity multiplied by its grade.</summary>
    public FixedPoint2 GetEffectiveQuantity(string prototype)
    {
        var quantity = GetTotalPrototypeQuantity(prototype);
        return Purity == null ? quantity : quantity * GetPurity(prototype);
    }

    /// <summary>The share of its list price a reagent sells for: its grade, so cutting neither earns nor loses money.</summary>
    public float GetPriceFactor(string prototype)
    {
        return GetPurity(prototype);
    }

    /// <summary>Adds a reagent at the given grade.</summary>
    public void AddReagent(string prototype, FixedPoint2 quantity, float purity)
        => AddReagent(new ReagentId(prototype, null), quantity, purity);

    /// <summary>Adds a reagent at the given grade.</summary>
    public void AddReagent(ReagentId id, FixedPoint2 quantity, float purity)
    {
        if (quantity <= FixedPoint2.Zero)
            return;

        var existing = GetTotalPrototypeQuantity(id.Prototype);
        var existingPurity = GetPurity(id.Prototype);
        AddReagent(id, quantity);
        SetPurity(id.Prototype, existing <= FixedPoint2.Zero
            ? purity
            : (existing.Float() * existingPurity + quantity.Float() * purity) / (existing + quantity).Float());
    }

    /// <summary>Re-grades the last <paramref name="addedQuantity"/> of a reagent, which was added at standard grade.</summary>
    public void RegradeAdded(string prototype, FixedPoint2 addedQuantity, float purity)
    {
        var total = GetTotalPrototypeQuantity(prototype).Float();
        if (total <= 0f || addedQuantity <= FixedPoint2.Zero)
            return;

        SetPurity(prototype, GetPurity(prototype) - addedQuantity.Float() * (1f - purity) / total);
    }

    private void WFCopyPurityFrom(Solution other)
    {
        Purity = other.Purity == null ? null : new Dictionary<string, float>(other.Purity);
    }

    /// <summary>Blends in a standard-grade addition, before the reagent is added.</summary>
    private void WFBlendAddedPurity(string prototype, FixedPoint2 quantity)
    {
        if (Purity == null || !Purity.ContainsKey(prototype))
            return;

        BlendPurity(prototype, GetTotalPrototypeQuantity(prototype), quantity, 1f);
    }

    /// <summary>Blends in another solution's grades, before its reagents are added.</summary>
    private void WFBlendPurityFrom(Solution other)
    {
        if (Purity == null && other.Purity == null)
            return;

        foreach (var (reagent, quantity) in other.Contents)
        {
            var added = other.GetPurity(reagent.Prototype);
            if (added >= StandardPurityThreshold && (Purity == null || !Purity.ContainsKey(reagent.Prototype)))
                continue;

            BlendPurity(reagent.Prototype, GetTotalPrototypeQuantity(reagent.Prototype), quantity, added);
        }
    }

    /// <summary>A copy of the grade table to restore after an operation that removes and re-adds reagents.</summary>
    private Dictionary<string, float>? WFSnapshotPurity()
    {
        return Purity == null ? null : new Dictionary<string, float>(Purity);
    }

    /// <summary>Gives this solution and a split-off solution the grades in <paramref name="source"/> for what each holds.</summary>
    private void WFRestorePurity(Dictionary<string, float>? source, Solution received)
    {
        if (source == null)
            return;

        received.Purity = WFFilterPurity(source, received);
        Purity = WFFilterPurity(source, this);
    }

    private void WFSplitPurity(Solution received)
    {
        WFRestorePurity(Purity, received);
    }

    private static Dictionary<string, float>? WFFilterPurity(Dictionary<string, float> source, Solution solution)
    {
        Dictionary<string, float>? result = null;
        foreach (var (prototype, value) in source)
        {
            if (!solution.ContainsPrototype(prototype))
                continue;

            result ??= new Dictionary<string, float>();
            result[prototype] = value;
        }

        return result;
    }

    /// <summary>Drops grades of reagents no longer in the solution.</summary>
    private void WFPrunePurity()
    {
        if (Purity == null)
            return;

        foreach (var prototype in Purity.Keys)
        {
            if (!ContainsPrototype(prototype))
                Purity.Remove(prototype);
        }

        if (Purity.Count == 0)
            Purity = null;
    }
}
