using Robust.Shared.Prototypes;

namespace Content.Shared._WF.LegStyle;

/// <summary>
/// Which leg styles a species has, read from the leg style prototypes.
/// </summary>
public static class LegStyleRules
{
    /// <summary>
    /// The style a species wears for a stance: the legs it can switch to if that is their stance, otherwise the
    /// style of its own legs, which is null unless those need clothing fitted.
    /// </summary>
    public static LegStylePrototype? Find(string species, LegStance stance, IPrototypeManager proto)
    {
        LegStylePrototype? own = null;
        foreach (var style in proto.EnumeratePrototypes<LegStylePrototype>())
        {
            if (!style.Species.Contains(species))
                continue;

            if (style.Default)
                own = style;
            else if (style.Stance == stance)
                return style;
        }

        return own;
    }

    /// <summary>
    /// The stance a species can switch to, if it has one.
    /// </summary>
    public static bool TryGetAlternate(string species, IPrototypeManager proto, out LegStance alternate)
    {
        foreach (var style in proto.EnumeratePrototypes<LegStylePrototype>())
        {
            if (style.Default || !style.Species.Contains(species))
                continue;

            alternate = style.Stance;
            return true;
        }

        alternate = LegStance.Default;
        return false;
    }

    /// <summary>
    /// The stance to save: Default unless the species has other legs for it.
    /// </summary>
    public static LegStance Validate(string species, LegStance stance, IPrototypeManager proto)
    {
        return TryGetAlternate(species, proto, out var alternate) && alternate == stance ? stance : LegStance.Default;
    }

    /// <summary>
    /// Whether a character with this saved stance stands digitigrade.
    /// </summary>
    public static bool IsDigitigrade(string species, LegStance stance, IPrototypeManager proto)
    {
        if (!TryGetAlternate(species, proto, out var alternate))
            return false;

        // The species' own legs are the opposite of the ones it can switch to.
        return stance == alternate
            ? alternate == LegStance.Digitigrade
            : alternate != LegStance.Digitigrade;
    }
}
