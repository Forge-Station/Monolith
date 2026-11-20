namespace Content.Shared._WF.BloodColour;

/// <summary>Validation and storage of a profile's blood colour.</summary>
public static class WFBloodColourRules
{
    /// <summary>The colour made opaque; blood can't be see-through.</summary>
    public static Color? Validate(Color? color)
    {
        return color?.WithAlpha(1f);
    }

    /// <summary>The colour as stored in the database, null for the species' own.</summary>
    public static string? ToStored(Color? color)
    {
        return Validate(color)?.ToHexNoAlpha();
    }

    /// <summary>The colour read back from the database; an empty or malformed value is the species' own.</summary>
    public static Color? FromStored(string? stored)
    {
        return !string.IsNullOrEmpty(stored) && Color.TryFromHex(stored, out var color) ? Validate(color) : null;
    }
}
