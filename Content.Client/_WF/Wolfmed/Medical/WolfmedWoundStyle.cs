using Content.Shared._Onyx.Medical;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.Examine;
using Content.Shared._WF.Wolfmed.Wounds;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.GameObjects;
using Robust.Shared.Utility;

namespace Content.Client._WF.Wolfmed.Medical;

/// <summary>
/// UI2: every colour the analyzer's wounds tab uses, in one place so the palette can be retuned without
/// touching the layout code. Tuned against the panel's near-black #050505 background.
/// </summary>
public static class WolfmedWoundStyle
{
    // Category tints. One hue per category, all light enough to stay legible on the dark panel.
    public static readonly Color Cut = Color.FromHex("#d9534f");
    public static readonly Color Puncture = Color.FromHex("#e8683d");
    public static readonly Color Ballistic = Color.FromHex("#e0a53a");
    public static readonly Color Blunt = Color.FromHex("#7f93ad");
    public static readonly Color Burn = Color.FromHex("#f07a26");
    public static readonly Color Internal = Color.FromHex("#a077d6");
    public static readonly Color Infection = Color.FromHex("#86b23c");
    public static readonly Color Mechanical = Color.FromHex("#56b6c2");
    public static readonly Color Other = Color.FromHex("#9aa0a6");

    // Part-level condition tints.
    public static readonly Color Bleeding = Color.FromHex("#d9534f");
    public static readonly Color InternalBleeding = Color.FromHex("#a077d6");
    public static readonly Color Fracture = Color.FromHex("#cfd4da");
    public static readonly Color Embedded = Color.FromHex("#e0a53a");
    public static readonly Color Necrosis = Color.FromHex("#d63c2c");
    public static readonly Color Overheating = Color.FromHex("#f07a26");
    public static readonly Color Scar = Color.FromHex("#9aa0a6");
    public static readonly Color Pain = Color.FromHex("#c9a227");
    public static readonly Color Impaired = Color.FromHex("#7f93ad");
    public static readonly Color Clotting = Color.FromHex("#6fa8dc");

    // Chrome.
    public static readonly Color CardBackground = Color.FromHex("#121216");
    public static readonly Color CardAccentNeutral = Color.FromHex("#3a3a42");
    public static readonly Color ChipBackground = Color.FromHex("#1c1c22");
    public static readonly Color ChipSelected = Color.FromHex("#33333d");
    public static readonly Color AlertBackground = Color.FromHex("#2a1414");
    public static readonly Color StageText = Color.FromHex("#8a8f96");

    // UI3: the card of the part the medic is aiming at.
    public static readonly Color CardTargeted = Color.FromHex("#1a1a20");
    public static readonly Color TargetedBorder = Color.FromHex("#ffcf6b");

    /// <summary>
    /// LOOK2: the same colours by the palette key a finding carries, so the examine's rows and the analyzer's
    /// cards tint the same glyph the same way. The keys are <see cref="WolfmedLookPalette"/>'s; the test
    /// asserts the two lists match, which is what stops a typo becoming a grey chip.
    /// </summary>
    private static readonly Dictionary<string, Color> LookColours = new()
    {
        ["cut"] = Cut,
        ["puncture"] = Puncture,
        ["ballistic"] = Ballistic,
        ["blunt"] = Blunt,
        ["burn"] = Burn,
        ["internal"] = Internal,
        ["infection"] = Infection,
        ["mechanical"] = Mechanical,
        ["other"] = Other,
        ["bleeding"] = Bleeding,
        ["internal_bleeding"] = InternalBleeding,
        ["fracture"] = Fracture,
        ["embedded"] = Embedded,
        ["necrosis"] = Necrosis,
        ["overheating"] = Overheating,
        ["scar"] = Scar,
        ["pain"] = Pain,
        ["impaired"] = Impaired,
        ["clotting"] = Clotting,
        [WolfmedLookPalette.Neutral] = CardAccentNeutral,
    };

    /// <summary>Every key this palette answers to.</summary>
    public static IReadOnlyCollection<string> LookKeys => LookColours.Keys;

    /// <summary>The colour for a finding's palette key, grey for one nobody declared.</summary>
    public static Color Look(string key) => LookColours.TryGetValue(key, out var colour) ? colour : Other;

    public static Color Category(WolfmedWoundCategory category) => category switch
    {
        WolfmedWoundCategory.Cut => Cut,
        WolfmedWoundCategory.Puncture => Puncture,
        WolfmedWoundCategory.Ballistic => Ballistic,
        WolfmedWoundCategory.Blunt => Blunt,
        WolfmedWoundCategory.Burn => Burn,
        WolfmedWoundCategory.Internal => Internal,
        WolfmedWoundCategory.Infection => Infection,
        WolfmedWoundCategory.Mechanical => Mechanical,
        _ => Other,
    };

    /// <summary>
    /// The colour of a card's left accent bar: the worst finding on the part decides it. Dead or septic
    /// tissue outranks bleeding, bleeding outranks a broken bone, and everything else is neutral.
    /// </summary>
    public static Color Accent(HealthAnalyzerWoundDiagnostic diagnostic)
    {
        // INFECTION: the part's own infection counts as the wound's does.
        if (diagnostic.Necrotic || diagnostic.Infection == WolfmedInfectionStage.Septic ||
            diagnostic.PartInfection == WolfmedInfectionStage.Septic)
            return Necrosis;

        if (diagnostic.BleedingRate > 0f || diagnostic.InternalBleedingRate > 0f)
            return Bleeding;

        if (diagnostic.Fracture != FractureGrade.None)
            return Fracture;

        if (diagnostic.Infection != WolfmedInfectionStage.None || diagnostic.NecrosisRisk ||
            diagnostic.PartInfection != WolfmedInfectionStage.None)
            return Infection;

        return CardAccentNeutral;
    }
}

/// <summary>
/// UI2: resolves the analyzer pictograms once and hands out the cached textures. One instance per panel;
/// the panel rebuilds its rows on every scan update and must not touch the resource cache per row.
/// </summary>
public sealed class WolfmedAnalyzerIcons
{
    private static readonly ResPath Rsi = new("/Textures/_WF/Wolfmed/Interface/analyzer_icons.rsi");
    private const string Fallback = "other";

    private readonly IResourceCache _cache;
    private readonly SpriteSystem _sprites;
    private readonly Dictionary<string, Texture> _textures = new();

    public WolfmedAnalyzerIcons(IResourceCache cache, SpriteSystem sprites)
    {
        _cache = cache;
        _sprites = sprites;
    }

    public Texture Get(string state)
    {
        if (_textures.TryGetValue(state, out var cached))
            return cached;

        var rsi = _cache.GetResource<RSIResource>(Rsi).RSI;
        var name = rsi.TryGetState(state, out _) ? state : Fallback;
        var texture = _sprites.Frame0(new SpriteSpecifier.Rsi(Rsi, name));
        _textures[state] = texture;
        return texture;
    }

    public Texture Get(WolfmedWoundCategory category) => Get(WolfmedWoundCategories.IconState(category));
}
