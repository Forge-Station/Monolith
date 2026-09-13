using System;
using System.Linq;
using Robust.Shared.IoC;
using Robust.Shared.Localization;

namespace Content.Client._WF.Humanoid;

/// <summary>
/// Display names for the individual sprite layers of a marking, shared by the creator picker and the admin
/// marking modifier. The tail split renames a state to &lt;state&gt;_FRONT and adds &lt;state&gt;_BEHIND, so a
/// layer whose string is filed under the original state name is looked up there as well before the name is
/// humanised. Only about two thirds of layers are translated at all, so an untranslated one reads as words
/// rather than as a raw locale key.
/// </summary>
public static class WolfgateMarkingNames
{
    private static readonly string[] SplitSuffixes = { "_FRONT", "_BEHIND" };

    // Populating a body part asks for hundreds of these, so the manager is resolved once rather than per lookup.
    private static ILocalizationManager? _localization;

    private static ILocalizationManager Localization =>
        _localization ??= IoCManager.Resolve<ILocalizationManager>();

    /// <summary>Translated layer name, the split half's original name, or the state read as words.</summary>
    public static string LayerName(string markingId, string state)
    {
        if (Localization.TryGetString($"marking-{markingId}-{state}", out var name))
            return name;

        // A marking whose own state already ends in a suffix matched the exact key above, if it has one.
        var unsplit = SplitBase(state);
        if (unsplit != null && Localization.TryGetString($"marking-{markingId}-{unsplit}", out name))
            return name;

        return Humanize(state);
    }

    /// <summary>The state a split half was cut from, or null when the state is not a split half.</summary>
    public static string? SplitBase(string state)
    {
        foreach (var suffix in SplitSuffixes)
        {
            if (state.Length > suffix.Length && state.EndsWith(suffix, StringComparison.Ordinal))
                return state[..^suffix.Length];
        }

        return null;
    }

    /// <summary>"belly_pregnant-1" becomes "Belly Pregnant 1", so an untranslated layer still reads as words.</summary>
    private static string Humanize(string state)
    {
        // Concatenating a char onto a string compiles to a span concat, which the content sandbox rejects,
        // so both halves stay strings.
        var separators = new[] { '_', '-', ' ' };
        var words = state.Split(separators, StringSplitOptions.RemoveEmptyEntries);
        return words.Length == 0
            ? state
            : string.Join(" ", words.Select(w => w[..1].ToUpperInvariant() + w[1..]));
    }
}
