using System.Text.Json;
using System.Text.Json.Serialization;
using Content.Shared._WF.Genitals;

namespace Content.Server._WF.Genitals;

/// <summary>DB mapping for GenitalProfile, stored as versioned JSON in profile.genitals. Reading never throws.</summary>
/// <remarks>Private DTOs keep the DB format independent of DataField names. Enums are written as names and parsed per field.</remarks>
public static class GenitalProfileJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Schema of a column without "v"; stays 1 when CurrentVersion moves, so unversioned data is still upgraded.</summary>
    private const int UnversionedSchema = 1;

    /// <summary>
    /// "" for an untouched unmigrated (version 0) profile, so the column stays unmigrated; otherwise JSON with the profile's
    /// own version. An edited version-0 profile is written as the current schema, so the edit is not lost.
    /// </summary>
    public static string Serialize(GenitalProfile? profile)
    {
        if (profile == null || (profile.Version <= 0 && IsUntouched(profile)))
            return string.Empty;

        var dto = new GenitalProfileDto
        {
            V = profile.Version > 0 ? profile.Version : GenitalProfile.CurrentVersion,
            Reveal = profile.RevealMode.ToString(),
            Penis = profile.Penis is { } penis
                ? new PenisDto
                {
                    Shape = penis.Shape.Id,
                    LengthCm = penis.LengthCm,
                    Sheath = penis.Sheath.ToString(),
                    MatchSkin = penis.MatchSkin,
                    Color = ToHex(penis.Color),
                    SheathMatchSkin = penis.SheathMatchSkin,
                    SheathColor = ToHex(penis.SheathColor),
                    Visibility = penis.Visibility.ToString(),
                }
                : null,
            Testicles = profile.Testicles is { } testicles
                ? new TesticlesDto
                {
                    Type = testicles.Type.ToString(),
                    Size = testicles.Size,
                    MatchSkin = testicles.MatchSkin,
                    Color = ToHex(testicles.Color),
                    Visibility = testicles.Visibility.ToString(),
                }
                : null,
            Vagina = profile.Vagina is { } vagina
                ? new VaginaDto
                {
                    Shape = vagina.Shape.Id,
                    MatchSkin = vagina.MatchSkin,
                    Color = ToHex(vagina.Color),
                    Visibility = vagina.Visibility.ToString(),
                }
                : null,
            Womb = profile.Womb,
            Breasts = profile.Breasts is { } breasts
                ? new BreastsDto
                {
                    Shape = breasts.Shape.Id,
                    Cup = breasts.Cup,
                    Lactation = breasts.Lactation,
                    MatchSkin = breasts.MatchSkin,
                    Color = ToHex(breasts.Color),
                    Visibility = breasts.Visibility.ToString(),
                }
                : null,
            Legacy = profile.LegacyMarkings,
        };

        return JsonSerializer.Serialize(dto, Options);
    }

    /// <summary>null when the column is empty; a LoadFailed profile holding what could be read when it cannot be fully read.</summary>
    public static GenitalProfile? Deserialize(string? json, ISawmill log, int profileId)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                log.Error($"Profile {profileId}: anatomy column is not a JSON object; kept unchanged until anatomy is edited.");
                return GenitalProfile.Failed(GenitalProfile.Empty);
            }

            return Read(root, log, profileId);
        }
        catch (JsonException e)
        {
            log.Error($"Profile {profileId}: anatomy column is not valid JSON ({e.Message}); kept unchanged until anatomy is edited.");
            return GenitalProfile.Failed(GenitalProfile.Empty);
        }
        catch (Exception e)
        {
            log.Error($"Profile {profileId}: anatomy column could not be read; kept unchanged until anatomy is edited. {e}");
            return GenitalProfile.Failed(GenitalProfile.Empty);
        }
    }

    /// <summary>Reads each property on its own, so one bad organ never discards the others.</summary>
    private static GenitalProfile Read(JsonElement root, ISawmill log, int profileId)
    {
        var failures = new List<string>();

        var version = UnversionedSchema;
        if (root.TryGetProperty("v", out var v))
        {
            if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var parsed) && parsed >= 0)
                version = parsed;
            else
                failures.Add("v");
        }

        var reveal = GenitalRevealMode.UndergarmentRemoval;
        if (root.TryGetProperty("reveal", out var revealElement)
            && (revealElement.ValueKind != JsonValueKind.String || !TryParseEnum(revealElement.GetString(), GenitalRevealMode.UndergarmentRemoval, out reveal)))
        {
            reveal = GenitalRevealMode.UndergarmentRemoval;
            failures.Add("reveal");
        }

        var penis = ReadOrgan<PenisDto, PenisProfile>(root, "penis", ToPenis, failures);
        var testicles = ReadOrgan<TesticlesDto, TesticlesProfile>(root, "testicles", ToTesticles, failures);
        var vagina = ReadOrgan<VaginaDto, VaginaProfile>(root, "vagina", ToVagina, failures);
        var breasts = ReadOrgan<BreastsDto, BreastsProfile>(root, "breasts", ToBreasts, failures);

        var womb = false;
        if (root.TryGetProperty("womb", out var wombElement))
        {
            if (wombElement.ValueKind is JsonValueKind.True or JsonValueKind.False)
                womb = wombElement.GetBoolean();
            else
                failures.Add("womb");
        }

        List<string>? legacy = null;
        if (root.TryGetProperty("legacy", out var legacyElement) && legacyElement.ValueKind != JsonValueKind.Null)
        {
            try
            {
                legacy = legacyElement.Deserialize<List<string>>(Options);
            }
            catch (JsonException)
            {
                failures.Add("legacy");
            }
        }

        // With* keeps the version of the instance it starts from; testicles after the penis, the womb after the vagina.
        var profile = (version == 0 ? GenitalProfile.Unmigrated : GenitalProfile.Empty)
            .WithRevealMode(reveal)
            .WithPenis(penis)
            .WithTesticles(testicles)
            .WithVagina(vagina)
            .WithWomb(womb)
            .WithBreasts(breasts);

        if (legacy != null && version != 0)
            profile = profile.AsMigrated(legacy);

        // A newer schema may hold fields this build cannot read; rewriting it as this schema would drop them.
        if (version > GenitalProfile.CurrentVersion)
        {
            log.Warning($"Profile {profileId}: anatomy schema v{version} is newer than this build; kept unchanged until anatomy is edited.");
            return GenitalProfile.Failed(profile);
        }

        if (failures.Count == 0)
            return profile;

        log.Warning($"Profile {profileId}: anatomy column partly unreadable ({string.Join(", ", failures)}); kept unchanged until anatomy is edited.");
        return GenitalProfile.Failed(profile);
    }

    /// <summary>One organ object, or null when absent. An unreadable object is recorded as a failure and skipped.</summary>
    private static TOrgan? ReadOrgan<TDto, TOrgan>(JsonElement root, string name, Func<TDto, TOrgan?> convert, List<string> failures)
        where TDto : class
        where TOrgan : class
    {
        if (!root.TryGetProperty(name, out var element) || element.ValueKind == JsonValueKind.Null)
            return null;

        try
        {
            if (element.ValueKind == JsonValueKind.Object
                && element.Deserialize<TDto>(Options) is { } dto
                && convert(dto) is { } organ)
            {
                return organ;
            }
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or NotSupportedException)
        {
            // Wrong value types; recorded below.
        }

        failures.Add(name);
        return null;
    }

    private static PenisProfile? ToPenis(PenisDto dto)
    {
        if (string.IsNullOrEmpty(dto.Shape)
            || !TryParseEnum(dto.Sheath, SheathType.None, out var sheath)
            || !TryParseEnum(dto.Visibility, GenitalVisibility.Normal, out var visibility)
            || !TryParseColor(dto.Color, out var color)
            || !TryParseColor(dto.SheathColor, out var sheathColor))
        {
            return null;
        }

        return new PenisProfile(dto.Shape, dto.LengthCm ?? 15, sheath, dto.MatchSkin ?? true, color,
            dto.SheathMatchSkin ?? true, sheathColor, visibility);
    }

    private static TesticlesProfile? ToTesticles(TesticlesDto dto)
    {
        if (!TryParseEnum(dto.Type, TesticleType.External, out var type)
            || !TryParseEnum(dto.Visibility, GenitalVisibility.Normal, out var visibility)
            || !TryParseColor(dto.Color, out var color))
        {
            return null;
        }

        return new TesticlesProfile(type, dto.Size ?? 2, dto.MatchSkin ?? true, color, visibility);
    }

    private static VaginaProfile? ToVagina(VaginaDto dto)
    {
        if (string.IsNullOrEmpty(dto.Shape)
            || !TryParseEnum(dto.Visibility, GenitalVisibility.Normal, out var visibility)
            || !TryParseColor(dto.Color, out var color))
        {
            return null;
        }

        return new VaginaProfile(dto.Shape, dto.MatchSkin ?? true, color, visibility);
    }

    private static BreastsProfile? ToBreasts(BreastsDto dto)
    {
        if (string.IsNullOrEmpty(dto.Shape)
            || !TryParseEnum(dto.Visibility, GenitalVisibility.Normal, out var visibility)
            || !TryParseColor(dto.Color, out var color))
        {
            return null;
        }

        return new BreastsProfile(dto.Shape, dto.Cup ?? 3, dto.Lactation ?? false, dto.MatchSkin ?? true, color, visibility);
    }

    /// <summary>Holds nothing a player chose: what an empty column loads as. Only an edit changes a version-0 profile.</summary>
    private static bool IsUntouched(GenitalProfile profile)
    {
        return profile.IsEmpty && profile.RevealMode == GenitalRevealMode.UndergarmentRemoval;
    }

    /// <summary>A defined enum name; null gives the fallback. Unknown names and numbers fail.</summary>
    private static bool TryParseEnum<T>(string? name, T fallback, out T value) where T : struct, Enum
    {
        if (name == null)
        {
            value = fallback;
            return true;
        }

        if (name.Length > 0 && char.IsLetter(name[0]) && Enum.TryParse(name, false, out value) && Enum.IsDefined(value))
            return true;

        value = fallback;
        return false;
    }

    /// <summary>A #RRGGBB colour; null gives white.</summary>
    private static bool TryParseColor(string? hex, out Color color)
    {
        if (hex == null)
        {
            color = Color.White;
            return true;
        }

        if (Color.TryFromHex(hex) is { } parsed)
        {
            color = parsed;
            return true;
        }

        color = Color.White;
        return false;
    }

    /// <summary>#RRGGBB with rounded channels; alpha is always opaque for anatomy.</summary>
    private static string ToHex(Color color)
    {
        return $"#{ToByte(color.R):X2}{ToByte(color.G):X2}{ToByte(color.B):X2}";
    }

    private static byte ToByte(float channel)
    {
        return float.IsFinite(channel) ? (byte) MathF.Round(Math.Clamp(channel, 0f, 1f) * 255f) : (byte) 255;
    }

    private sealed record GenitalProfileDto
    {
        [JsonPropertyName("v")] public int V { get; init; }
        [JsonPropertyName("reveal")] public string Reveal { get; init; } = string.Empty;
        [JsonPropertyName("penis")] public PenisDto? Penis { get; init; }
        [JsonPropertyName("testicles")] public TesticlesDto? Testicles { get; init; }
        [JsonPropertyName("vagina")] public VaginaDto? Vagina { get; init; }
        [JsonPropertyName("womb")] public bool Womb { get; init; }
        [JsonPropertyName("breasts")] public BreastsDto? Breasts { get; init; }
        [JsonPropertyName("legacy")] public List<string>? Legacy { get; init; }
    }

    private sealed record PenisDto
    {
        [JsonPropertyName("shape")] public string? Shape { get; init; }
        [JsonPropertyName("lengthCm")] public int? LengthCm { get; init; }
        [JsonPropertyName("sheath")] public string? Sheath { get; init; }
        [JsonPropertyName("matchSkin")] public bool? MatchSkin { get; init; }
        [JsonPropertyName("color")] public string? Color { get; init; }
        [JsonPropertyName("sheathMatchSkin")] public bool? SheathMatchSkin { get; init; }
        [JsonPropertyName("sheathColor")] public string? SheathColor { get; init; }
        [JsonPropertyName("visibility")] public string? Visibility { get; init; }
    }

    private sealed record TesticlesDto
    {
        [JsonPropertyName("type")] public string? Type { get; init; }
        [JsonPropertyName("size")] public int? Size { get; init; }
        [JsonPropertyName("matchSkin")] public bool? MatchSkin { get; init; }
        [JsonPropertyName("color")] public string? Color { get; init; }
        [JsonPropertyName("visibility")] public string? Visibility { get; init; }
    }

    private sealed record VaginaDto
    {
        [JsonPropertyName("shape")] public string? Shape { get; init; }
        [JsonPropertyName("matchSkin")] public bool? MatchSkin { get; init; }
        [JsonPropertyName("color")] public string? Color { get; init; }
        [JsonPropertyName("visibility")] public string? Visibility { get; init; }
    }

    private sealed record BreastsDto
    {
        [JsonPropertyName("shape")] public string? Shape { get; init; }
        [JsonPropertyName("cup")] public int? Cup { get; init; }
        [JsonPropertyName("lactation")] public bool? Lactation { get; init; }
        [JsonPropertyName("matchSkin")] public bool? MatchSkin { get; init; }
        [JsonPropertyName("color")] public string? Color { get; init; }
        [JsonPropertyName("visibility")] public string? Visibility { get; init; }
    }
}
