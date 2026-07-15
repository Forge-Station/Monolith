using System.Text.RegularExpressions;
using Robust.Shared.Maths;

namespace Content.Shared._Forge.Silicons.StationAi;

public static partial class StationAiCustomizationValidator
{
    public const int MaxNameLength = 32;

    public static bool TryNormalizeName(
        string input,
        bool restrictedNames,
        bool icNameCase,
        out string name)
    {
        name = input.Trim();
        if (name.Length is 0 or > MaxNameLength)
            return false;

        if (restrictedNames)
        {
            var restricted = RestrictedNameRegex().Replace(name, string.Empty).Trim();
            if (restricted != name)
                return false;
        }

        if (icNameCase)
            name = NameCaseRegex().Replace(name, match => match.Groups["word"].Value.ToUpperInvariant());

        return name.Length is > 0 and <= MaxNameLength;
    }

    public static bool TryNormalizeColor(Color input, out Color color)
    {
        color = Color.White;
        if (!float.IsFinite(input.R) ||
            !float.IsFinite(input.G) ||
            !float.IsFinite(input.B) ||
            !float.IsFinite(input.A))
        {
            return false;
        }

        color = new Color(
            Math.Clamp(input.R, 0f, 1f),
            Math.Clamp(input.G, 0f, 1f),
            Math.Clamp(input.B, 0f, 1f),
            1f);
        return true;
    }

    [GeneratedRegex("[^\\u0400-\\u04FFa-zA-Z0-9' -]")]
    private static partial Regex RestrictedNameRegex();

    [GeneratedRegex(@"^(?<word>\w)|\b(?<word>\w)(?=\w*$)")]
    private static partial Regex NameCaseRegex();
}
