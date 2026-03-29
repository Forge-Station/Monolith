using System.Linq;
using System.Text.RegularExpressions;
using Content.Shared.Construction.Prototypes;
using Robust.Shared.Localization;

namespace Content.Client.Construction;

/// <summary>
/// Forge-Change-full
/// Resolves construction-recipe-{'kebab-id'} Fluent keys under Resources/Locale/*/construction/recipes/.
/// </summary>
public static class ConstructionRecipeLocale
{
    private static readonly Regex KebabRegex = new("([A-Z])", RegexOptions.Compiled);
    private static readonly Regex LetterBeforeDigitRegex = new(@"([a-z])([0-9])", RegexOptions.Compiled);

    public static string PrototypeIdToRecipeKeySuffix(string id)
    {
        if (string.IsNullOrEmpty(id))
            return id;

        var working = id;
        if (working.EndsWith("Recipe", StringComparison.Ordinal) && working.Length > 6)
            working = working[..^6];

        // Short all-caps ids (e.g. APC, HUD)
        if (working.Length > 0 &&
            working.All(c => char.IsAsciiLetter(c) && char.IsAsciiLetterUpper(c)))
        {
            return working.ToLowerInvariant();
        }

        // Lowercase-only ids (e.g. camera)
        if (!working.Any(char.IsAsciiLetterUpper))
            return working.ToLowerInvariant();

        var kebab = KebabRegex.Replace(working, "-$1").TrimStart('-').ToLowerInvariant();
        return LetterBeforeDigitRegex.Replace(kebab, "$1-$2");
    }

    public static string GetLocalizedName(ConstructionPrototype proto)
    {
        var key = $"construction-recipe-{PrototypeIdToRecipeKeySuffix(proto.ID)}";
        if (Loc.TryGetString(key, out var localized))
            return localized;
        return proto.Name;
    }

    public static string GetLocalizedDescription(ConstructionPrototype proto)
    {
        var key = $"construction-recipe-{PrototypeIdToRecipeKeySuffix(proto.ID)}-desc";
        if (Loc.TryGetString(key, out var localized))
            return localized;
        return proto.Description;
    }
}
