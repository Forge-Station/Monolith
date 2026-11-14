using System.Globalization;
using System.Numerics;
using System.Text;

namespace Content.Shared._WF.Waypoints;

/// <summary>The limits on waypoints, used by the entry form and enforced again by the server.</summary>
public static class WFWaypointRules
{
    public const int MaxWaypoints = 16;
    public const int MaxNameLength = 24;
    public const float MaxCoordinate = 1_000_000f;

    /// <summary>Whether a position is a real number on both axes and inside the coordinate limit.</summary>
    public static bool IsValidPosition(Vector2 position)
    {
        return float.IsFinite(position.X) && float.IsFinite(position.Y) &&
               MathF.Abs(position.X) <= MaxCoordinate && MathF.Abs(position.Y) <= MaxCoordinate;
    }

    /// <summary>Reads typed coordinates, always with a dot for the decimal point as the radar writes them.</summary>
    public static bool TryParsePosition(string x, string y, out Vector2 position)
    {
        position = default;
        if (!float.TryParse(x.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedX) ||
            !float.TryParse(y.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedY))
            return false;

        position = new Vector2(parsedX, parsedY);
        return IsValidPosition(position);
    }

    /// <summary>Strips control characters and surrounding spaces, and cuts the name to length.</summary>
    public static string CleanName(string name)
    {
        var clean = new StringBuilder(MaxNameLength);
        foreach (var rune in name.Trim().EnumerateRunes())
        {
            if (Rune.IsControl(rune))
                continue;
            if (clean.Length + rune.Utf16SequenceLength > MaxNameLength)
                break;
            clean.Append(rune.ToString());
        }

        return clean.ToString().Trim();
    }
}
