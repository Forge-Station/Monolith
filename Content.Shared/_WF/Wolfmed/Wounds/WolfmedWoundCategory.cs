using Content.Shared._Onyx.Wounds;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Wolfmed.Wounds;

/// <summary>
/// UI2: the bucket a wound falls into on the analyzer's wounds tab. One icon and one colour per member.
/// </summary>
[Serializable, NetSerializable]
public enum WolfmedWoundCategory : byte
{
    Cut,
    Puncture,
    Ballistic,
    Blunt,
    Burn,
    Internal,
    Infection,
    Mechanical,
    Other,
}

/// <summary>
/// UI2: resolves and names wound categories. Shared so the server can stamp the payload and the client
/// can render a category strip without a second table.
/// </summary>
public static class WolfmedWoundCategories
{
    /// <summary>Display order of the category strip. Hardcoded because the sandbox forbids reflection.</summary>
    public static readonly WolfmedWoundCategory[] All =
    [
        WolfmedWoundCategory.Cut,
        WolfmedWoundCategory.Puncture,
        WolfmedWoundCategory.Ballistic,
        WolfmedWoundCategory.Blunt,
        WolfmedWoundCategory.Burn,
        WolfmedWoundCategory.Internal,
        WolfmedWoundCategory.Infection,
        WolfmedWoundCategory.Mechanical,
        WolfmedWoundCategory.Other,
    ];

    /// <summary>
    /// Damage type to category, in the order they are tested. A wound accepting several types takes the
    /// first match, so a burn that also lists Blunt still reads as a burn.
    /// </summary>
    private static readonly (string Damage, WolfmedWoundCategory Category)[] Derived =
    [
        ("Slash", WolfmedWoundCategory.Cut),
        ("Piercing", WolfmedWoundCategory.Puncture),
        ("Heat", WolfmedWoundCategory.Burn),
        ("Cold", WolfmedWoundCategory.Burn),
        ("Shock", WolfmedWoundCategory.Burn),
        ("Caustic", WolfmedWoundCategory.Burn),
        ("Blunt", WolfmedWoundCategory.Blunt),
    ];

    /// <summary>
    /// The prototype's explicit category, else one derived from its damage types, else Other. Every wound
    /// prototype therefore has a category without every prototype having to declare one.
    /// </summary>
    public static WolfmedWoundCategory Resolve(WoundPrototype prototype)
    {
        if (prototype.AnalyzerCategory is { } explicitCategory)
            return explicitCategory;

        foreach (var (damage, category) in Derived)
        {
            if (prototype.DamageTypes.ContainsKey(damage))
                return category;
        }

        return WolfmedWoundCategory.Other;
    }

    /// <summary>Locale key for the category's display name.</summary>
    public static string NameKey(WolfmedWoundCategory category) =>
        "wolfmed-wound-category-" + category.ToString().ToLowerInvariant();

    /// <summary>
    /// RSI state in analyzer_icons.rsi. Kept here rather than on the client so the coverage test can check
    /// the mapping against the generator's state list.
    /// </summary>
    public static string IconState(WolfmedWoundCategory category) => category switch
    {
        WolfmedWoundCategory.Cut => "cut",
        WolfmedWoundCategory.Puncture => "puncture",
        WolfmedWoundCategory.Ballistic => "ballistic",
        WolfmedWoundCategory.Blunt => "blunt",
        WolfmedWoundCategory.Burn => "burn",
        WolfmedWoundCategory.Internal => "internal",
        WolfmedWoundCategory.Infection => "infection",
        WolfmedWoundCategory.Mechanical => "mechanical",
        _ => "other",
    };
}
