using System.Diagnostics.CodeAnalysis;
using Content.Shared._WF.Genitals.Migration;
using Content.Shared._WF.Genitals.Prototypes;
using Content.Shared.Humanoid.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Genitals;

/// <summary>Why the validator changed or refused anatomy.</summary>
public enum GenitalIssue : byte
{
    Underage,
    SpeciesExcluded,
    UnknownShape,
    WrongSlotShape,
    SheathNotAllowed,
    TesticlesWithoutPenis,
    WombWithoutVagina,
    SizeClamped,
    CupClamped,
    LengthClamped,
    InvalidValue,
    LoadFailed,

    /// <summary>Editor-only note, never produced by EnsureValid.</summary>
    PregnancyMarkingWithoutWomb,
}

/// <summary>Anatomy rules shared by the creator and the server, so client feedback and server authority agree.</summary>
public static class GenitalProfileValidator
{
    public const int MinTesticleSize = 1;
    public const int MaxTesticleSize = 5;
    public const int MinCup = 1;
    public const int MaxCup = 19;

    /// <summary>The Default settings prototype (settings.yml).</summary>
    public static GenitalSettingsPrototype GetSettings(IPrototypeManager proto)
    {
        return proto.Index(GenitalSettingsPrototype.DefaultId);
    }

    /// <summary>Whether a character may have anatomy at all (age and species). Never alters data.</summary>
    public static bool IsEligible(int age, string species, GenitalSettingsPrototype settings, out GenitalIssue? reason)
    {
        if (age < settings.AdultAge)
        {
            reason = GenitalIssue.Underage;
            return false;
        }

        foreach (var excluded in settings.ExcludedSpecies)
        {
            if (excluded.Id != species)
                continue;

            reason = GenitalIssue.SpeciesExcluded;
            return false;
        }

        reason = null;
        return true;
    }

    /// <summary>The age HumanoidCharacterProfile.EnsureValid stores: clamped to the species' range. An unknown species keeps it.</summary>
    public static int ClampAge(int age, string species, IPrototypeManager proto)
    {
        return proto.TryIndex<SpeciesPrototype>(species, out var speciesProto)
            ? Math.Clamp(age, speciesProto.MinAge, speciesProto.MaxAge)
            : age;
    }

    /// <summary>
    /// IsEligible with the age clamped as saving clamps it. The creator gate and its preview doll use this, so a typed age
    /// below the species minimum never hides anatomy that saving would keep.
    /// </summary>
    public static bool IsEligibleClamped(int age, string species, IPrototypeManager proto)
    {
        return IsEligible(ClampAge(age, species, proto), species, GetSettings(proto), out _);
    }

    /// <summary>Whether the age, clamped as saving clamps it, reaches the adult age. Saving below it clears the anatomy.</summary>
    public static bool IsAdultClamped(int age, string species, IPrototypeManager proto)
    {
        return ClampAge(age, species, proto) >= GetSettings(proto).AdultAge;
    }

    /// <summary>Organ rules only: shapes, linked options, clamps, enum and colour normalisation.</summary>
    /// <remarks>
    /// Keeps Version (clamped to known schemas), LoadFailed and the well-formed LegacyMarkings. Returns the same instance when valid.
    /// </remarks>
    public static GenitalProfile ValidateOrgans(GenitalProfile profile, IPrototypeManager proto, List<GenitalIssue>? issues = null)
    {
        var settings = GetSettings(proto);

        if (profile.LoadFailed)
            issues?.Add(GenitalIssue.LoadFailed);

        // A newer schema is read as the current one.
        var version = Math.Clamp(profile.Version, 0, GenitalProfile.CurrentVersion);

        var reveal = profile.RevealMode;
        if (!Enum.IsDefined(reveal))
        {
            reveal = GenitalRevealMode.UndergarmentRemoval;
            issues?.Add(GenitalIssue.InvalidValue);
        }

        var penis = ValidatePenis(profile.Penis, proto, settings, issues);

        var testicles = ValidateTesticles(profile.Testicles, settings, issues);
        if (testicles != null && penis == null)
        {
            testicles = null;
            issues?.Add(GenitalIssue.TesticlesWithoutPenis);
        }

        var vagina = ValidateVagina(profile.Vagina, proto, issues);

        var womb = profile.Womb;
        if (womb && vagina == null)
        {
            womb = false;
            issues?.Add(GenitalIssue.WombWithoutVagina);
        }

        var breasts = ValidateBreasts(profile.Breasts, proto, issues);

        // Only the server migration should produce this list; a crafted client must not be able to store arbitrary text.
        var legacy = ValidateLegacy(profile.LegacyMarkings, issues);

        return profile.Normalised(version, reveal, penis, testicles, vagina, womb, breasts, legacy);
    }

    /// <summary>Longest legacy "id@colours" string kept. Real ones are far shorter.</summary>
    public const int MaxLegacyMarkingLength = 256;

    /// <summary>Keeps well-formed legacy marking strings, at most one per table entry. Returns the input when it is already clean.</summary>
    private static List<string>? ValidateLegacy(List<string>? legacy, List<GenitalIssue>? issues)
    {
        if (legacy == null)
            return null;

        var max = LegacyGenitalMarkings.Entries.Count;
        var clean = legacy.Count > 0 && legacy.Count <= max;
        for (var i = 0; clean && i < legacy.Count; i++)
        {
            clean = IsLegacyMarkingString(legacy[i]);
        }

        if (clean)
            return legacy;

        var kept = new List<string>();
        foreach (var entry in legacy)
        {
            if (kept.Count >= max)
                break;

            if (IsLegacyMarkingString(entry))
                kept.Add(entry);
        }

        if (kept.Count < legacy.Count)
            issues?.Add(GenitalIssue.InvalidValue);

        return kept.Count > 0 ? kept : null;
    }

    /// <summary>"id@colours" as Marking.ToString writes it: a legacy table id, then comma-separated hex colours.</summary>
    private static bool IsLegacyMarkingString(string? entry)
    {
        if (entry == null || entry.Length > MaxLegacyMarkingLength)
            return false;

        var at = entry.IndexOf('@');
        if (at <= 0 || !LegacyGenitalMarkings.IsLegacyId(entry.Substring(0, at)))
            return false;

        for (var i = at + 1; i < entry.Length; i++)
        {
            var c = entry[i];
            if (c != '#' && c != ',' && !(c >= '0' && c <= '9') && !(c >= 'A' && c <= 'F') && !(c >= 'a' && c <= 'f'))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Server authority: ValidateOrgans, then the adult gate. Below the adult age only the reveal mode and the legacy
    /// marking backup are kept. An excluded species keeps its anatomy, which nothing builds. Never throws.
    /// </summary>
    /// <remarks>
    /// An organ dropped because its stored shape no longer exists or fits marks the result LoadFailed, so saves keep the
    /// stored column until the player edits anatomy.
    /// </remarks>
    public static GenitalProfile EnsureValid(GenitalProfile profile, int age, string species, IPrototypeManager proto,
        List<GenitalIssue>? issues = null)
    {
        try
        {
            // Crafted network data can deliver null.
            if (profile == null)
                return GenitalProfile.Empty;

            var found = new List<GenitalIssue>();
            var organs = ValidateOrgans(profile, proto, found);
            issues?.AddRange(found);

            // A renamed or removed shape is not the player's choice, so the stored data waits for an anatomy edit.
            if (found.Contains(GenitalIssue.UnknownShape) || found.Contains(GenitalIssue.WrongSlotShape))
                organs = GenitalProfile.Failed(organs);

            if (IsEligible(age, species, GetSettings(proto), out var reason))
                return organs;

            if (reason != null)
                issues?.Add(reason.Value);

            // The organ builder checks eligibility, so nothing is built; excluding a species never deletes stored data.
            if (reason == GenitalIssue.SpeciesExcluded)
                return organs;

            // The legacy backup stays, so a revert can restore the old markings.
            return GenitalProfile.Empty.WithRevealMode(organs.RevealMode).AsMigrated(organs.LegacyMarkings);
        }
        catch (Exception e)
        {
            Logger.GetSawmill("genitals").Error($"Anatomy validation failed, anatomy cleared: {e}");
            return GenitalProfile.Empty;
        }
    }

    /// <summary>Opaque colour quantised to 8 bits per channel. NaN, infinite or out-of-range channels become white.</summary>
    public static Color NormaliseColor(Color color, List<GenitalIssue>? issues = null)
    {
        if (!IsChannelValid(color.R) || !IsChannelValid(color.G) || !IsChannelValid(color.B) || !float.IsFinite(color.A))
        {
            issues?.Add(GenitalIssue.InvalidValue);
            return Color.White;
        }

        return new Color(ToByte(color.R), ToByte(color.G), ToByte(color.B), (byte) 255);
    }

    private static PenisProfile? ValidatePenis(PenisProfile? penis, IPrototypeManager proto, GenitalSettingsPrototype settings,
        List<GenitalIssue>? issues)
    {
        if (penis == null)
            return null;

        if (!TryGetShape(penis.Shape, GenitalSlot.Penis, proto, issues, out var shape))
            return null;

        var min = settings.MinLengthCm;
        var max = Math.Max(min, settings.MaxLengthCm);
        var length = penis.LengthCm;
        if (length < min || length > max)
        {
            length = Math.Clamp(length, min, max);
            issues?.Add(GenitalIssue.LengthClamped);
        }

        var sheath = penis.Sheath;
        if (!Enum.IsDefined(sheath))
        {
            sheath = SheathType.None;
            issues?.Add(GenitalIssue.InvalidValue);
        }
        else if (sheath != SheathType.None && !shape.AllowedSheaths.Contains(sheath))
        {
            sheath = SheathType.None;
            issues?.Add(GenitalIssue.SheathNotAllowed);
        }

        var visibility = NormaliseVisibility(penis.Visibility, issues);
        var color = NormaliseColor(penis.Color, issues);
        var sheathColor = NormaliseColor(penis.SheathColor, issues);

        if (length == penis.LengthCm
            && sheath == penis.Sheath
            && visibility == penis.Visibility
            && SameColor(color, penis.Color)
            && SameColor(sheathColor, penis.SheathColor))
            return penis;

        return penis.With(lengthCm: length, sheath: sheath, color: color, sheathColor: sheathColor, visibility: visibility);
    }

    private static TesticlesProfile? ValidateTesticles(TesticlesProfile? testicles, GenitalSettingsPrototype settings,
        List<GenitalIssue>? issues)
    {
        if (testicles == null)
            return null;

        if (testicles.Type != TesticleType.External && testicles.Type != TesticleType.Internal)
        {
            issues?.Add(GenitalIssue.InvalidValue);
            return null;
        }

        var size = testicles.Size;
        if (testicles.Type == TesticleType.Internal)
        {
            // Internal testicles are neither drawn nor described.
            size = Math.Clamp(settings.DefaultTesticleSize, MinTesticleSize, MaxTesticleSize);
        }
        else if (size < MinTesticleSize || size > MaxTesticleSize)
        {
            size = Math.Clamp(size, MinTesticleSize, MaxTesticleSize);
            issues?.Add(GenitalIssue.SizeClamped);
        }

        var visibility = NormaliseVisibility(testicles.Visibility, issues);
        var color = NormaliseColor(testicles.Color, issues);

        if (size == testicles.Size && visibility == testicles.Visibility && SameColor(color, testicles.Color))
            return testicles;

        return testicles.With(size: size, color: color, visibility: visibility);
    }

    private static VaginaProfile? ValidateVagina(VaginaProfile? vagina, IPrototypeManager proto, List<GenitalIssue>? issues)
    {
        if (vagina == null)
            return null;

        if (!TryGetShape(vagina.Shape, GenitalSlot.Vagina, proto, issues, out _))
            return null;

        var visibility = NormaliseVisibility(vagina.Visibility, issues);
        var color = NormaliseColor(vagina.Color, issues);

        if (visibility == vagina.Visibility && SameColor(color, vagina.Color))
            return vagina;

        return vagina.With(color: color, visibility: visibility);
    }

    private static BreastsProfile? ValidateBreasts(BreastsProfile? breasts, IPrototypeManager proto, List<GenitalIssue>? issues)
    {
        if (breasts == null)
            return null;

        if (!TryGetShape(breasts.Shape, GenitalSlot.Breasts, proto, issues, out _))
            return null;

        var cup = breasts.Cup;
        if (cup < MinCup || cup > MaxCup)
        {
            cup = Math.Clamp(cup, MinCup, MaxCup);
            issues?.Add(GenitalIssue.CupClamped);
        }

        var visibility = NormaliseVisibility(breasts.Visibility, issues);
        var color = NormaliseColor(breasts.Color, issues);

        if (cup == breasts.Cup && visibility == breasts.Visibility && SameColor(color, breasts.Color))
            return breasts;

        return breasts.With(cup: cup, color: color, visibility: visibility);
    }

    /// <summary>The shape must exist and belong to the slot.</summary>
    private static bool TryGetShape(ProtoId<GenitalShapePrototype> id, GenitalSlot slot, IPrototypeManager proto,
        List<GenitalIssue>? issues, [NotNullWhen(true)] out GenitalShapePrototype? shape)
    {
        shape = null;
        if (string.IsNullOrEmpty(id.Id) || !proto.TryIndex(id, out var found))
        {
            issues?.Add(GenitalIssue.UnknownShape);
            return false;
        }

        if (found.Slot != slot)
        {
            issues?.Add(GenitalIssue.WrongSlotShape);
            return false;
        }

        shape = found;
        return true;
    }

    private static GenitalVisibility NormaliseVisibility(GenitalVisibility visibility, List<GenitalIssue>? issues)
    {
        if (Enum.IsDefined(visibility))
            return visibility;

        issues?.Add(GenitalIssue.InvalidValue);
        return GenitalVisibility.Normal;
    }

    private static bool IsChannelValid(float channel)
    {
        return float.IsFinite(channel) && channel >= 0f && channel <= 1f;
    }

    private static byte ToByte(float channel)
    {
        return (byte) MathF.Round(channel * 255f);
    }

    /// <summary>Exact channel equality, so a colour that still needs quantising never counts as unchanged.</summary>
    private static bool SameColor(Color a, Color b)
    {
        return a.R == b.R && a.G == b.G && a.B == b.B && a.A == b.A;
    }
}
