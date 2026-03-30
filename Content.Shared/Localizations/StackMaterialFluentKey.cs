using System.Linq;
using System.Text.RegularExpressions;

namespace Content.Shared.Localizations;

/// <summary>
/// Forge-Change-full
/// Maps stack prototype ids to Fluent keys in Resources/Locale/*/stack/stacks.ftl (stack-*).
/// </summary>
public static class StackMaterialFluentKey
{
    private static readonly Regex KebabRegex = new("([A-Z])", RegexOptions.Compiled);

    public static string FromStackPrototypeId(string stackPrototypeId)
    {
        if (string.IsNullOrEmpty(stackPrototypeId))
            return stackPrototypeId;

        return stackPrototypeId switch
        {
            "Cable" => "stack-lv-cable",
            "CableMV" => "stack-mv-cable",
            "CableHV" => "stack-hv-cable",
            _ => $"stack-{PrototypeIdToKebabSuffix(stackPrototypeId)}"
        };
    }

    private static string PrototypeIdToKebabSuffix(string id)
    {
        if (id.Length > 0 &&
            id.All(c => char.IsAsciiLetter(c) && char.IsAsciiLetterUpper(c)))
        {
            return id.ToLowerInvariant();
        }

        if (!id.Any(char.IsAsciiLetterUpper))
            return id.ToLowerInvariant();

        return KebabRegex.Replace(id, "-$1").TrimStart('-').ToLowerInvariant();
    }
}
