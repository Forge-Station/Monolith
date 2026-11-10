using System.Linq;
using System.Text.RegularExpressions;

namespace Content.Shared._WF.Gags;

/// <summary>Reading, writing and checking the colours a loadout gives a recolourable item.</summary>
public static class WFGagsColors
{
    /// <summary>The most colour slots an item may have.</summary>
    public const int MaxColors = 16;

    private static readonly Regex SlotSuffix = new(@"-c(\d+)$", RegexOptions.Compiled);

    /// <summary>The colour slot (0-based) a sprite state takes, or null for a state that keeps its own colours.</summary>
    public static int? SlotOf(string? state)
    {
        if (state == null)
            return null;

        var match = SlotSuffix.Match(state);
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out var slot) || slot < 1 || slot > MaxColors)
            return null;

        return slot - 1;
    }

    /// <summary>Colours as they are saved: hex, comma-separated; empty for none.</summary>
    public static string Format(List<Color>? colors)
    {
        return colors == null || colors.Count == 0 ? string.Empty : string.Join(',', colors.Select(c => c.ToHexNoAlpha()));
    }

    /// <summary>Saved colours back, or null for none. Unreadable entries are dropped.</summary>
    public static List<Color>? Parse(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return null;

        var colors = new List<Color>();
        foreach (var part in text.Split(','))
        {
            if (colors.Count >= MaxColors)
                break;

            if (Color.TryFromHex(part.Trim(), out var color))
                colors.Add(color.WithAlpha(1f));
        }

        return colors.Count == 0 ? null : colors;
    }

    /// <summary>Colours a client sent, made safe: at most <see cref="MaxColors"/>, all opaque.</summary>
    public static List<Color>? Sanitize(List<Color>? colors)
    {
        if (colors == null || colors.Count == 0)
            return null;

        return colors.Take(MaxColors).Select(c => c.WithAlpha(1f)).ToList();
    }

    public static bool Same(List<Color>? a, List<Color>? b)
    {
        if (a == null || a.Count == 0)
            return b == null || b.Count == 0;

        return b != null && a.SequenceEqual(b);
    }
}
