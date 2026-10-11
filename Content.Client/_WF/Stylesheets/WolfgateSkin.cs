using System.Linq;

namespace Content.Client._WF.Stylesheets;

/// <summary>
/// Everything that differs between two Wolfgate looks: palette, texture set, HUD theme and type. The rules in
/// <see cref="StyleWolfgate"/> are shared; a skin only feeds them. Keep each palette in sync with the matching
/// entry in Tools/_WF/Stylesheets/WolfgateUiTextures/generate_textures.py.
/// </summary>
public sealed class WolfgateSkin
{
    /// <summary>Value of the wf.ui_style CVar that selects this skin.</summary>
    public required string Id { get; init; }
    /// <summary>Locale key of the name shown in the options menu.</summary>
    public required string Name { get; init; }
    /// <summary>Folder of the stylesheet textures, ending in a slash.</summary>
    public required string TexturePath { get; init; }
    /// <summary>Id of the hudTheme/uiTheme prototype that pairs with this skin.</summary>
    public required string HudTheme { get; init; }

    public required string[] DisplayFonts { get; init; }
    public required string[] MenuFonts { get; init; }
    public required string[] MonoFonts { get; init; }

    public required Color Accent { get; init; }
    public required Color AccentDim { get; init; }
    public required Color Text { get; init; }
    public required Color TextMuted { get; init; }
    public required Color TextDisabled { get; init; }
    public required Color Ink { get; init; }
    public required Color Glass { get; init; }
    public required Color GlassRaised { get; init; }
    public required Color GlassLight { get; init; }
    public required Color Edge { get; init; }
    public required Color EdgeSoft { get; init; }
    public required Color EdgeLight { get; init; }
    public required Color Good { get; init; }
    public required Color Caution { get; init; }
    public required Color Danger { get; init; }

    public required Color ButtonDefault { get; init; }
    public required Color ButtonHovered { get; init; }
    public required Color ButtonPressed { get; init; }
    public required Color ButtonDisabled { get; init; }
    public required Color ButtonCautionDefault { get; init; }
    public required Color ButtonCautionHovered { get; init; }
    public required Color ButtonCautionPressed { get; init; }
    public required Color ButtonCautionDisabled { get; init; }
    public required Color ButtonGoodDefault { get; init; }
    public required Color ButtonGoodHovered { get; init; }
    public required Color ButtonGoodDisabled { get; init; }
    public required Color ButtonDangerDefault { get; init; }
    public required Color ButtonDangerHovered { get; init; }
    public required Color TopButtonDefault { get; init; }
    public required Color ContextDefault { get; init; }
    public required Color ContextHovered { get; init; }
    public required Color ContextPressed { get; init; }
    public required Color ChatBackground { get; init; }
    public required Color Selection { get; init; }
}

public static class WolfgateSkins
{
    private static readonly string[] NotoFallback =
    {
        "/Fonts/NotoSans/NotoSans-Bold.ttf",
        "/Fonts/NotoSans/NotoSansSymbols-Bold.ttf",
        "/Fonts/NotoSans/NotoSansSymbols2-Regular.ttf",
        "/Fonts/NotoSans/NotoSansSC-Regular.ttf",
    };

    private static readonly string[] MonoStack =
    {
        "/Fonts/RobotoMono/RobotoMono-Bold.ttf",
        "/Fonts/NotoSans/NotoSansSymbols-Regular.ttf",
        "/Fonts/NotoSans/NotoSansSC-Regular.ttf",
    };

    /// <summary>Hyper-futurist glass and cyan: the original Wolfgate retheme.</summary>
    public static readonly WolfgateSkin Futurist = new()
    {
        Id = "Wolfgate",
        Name = "wf-ui-style-wolfgate",
        TexturePath = "/Textures/_WF/Stylesheets/Interface/Wolfgate/Style/",
        HudTheme = "WFTheme",
        DisplayFonts = new[] { "/Fonts/Iceberg/Iceberg-Regular.ttf" }.Concat(NotoFallback).ToArray(),
        MenuFonts = new[] { "/Fonts/Iceberg/Iceberg-Regular.ttf" }.Concat(NotoFallback).ToArray(),
        MonoFonts = MonoStack,

        Accent = Color.FromHex("#46D7FF"),
        AccentDim = Color.FromHex("#2A8FB0"),
        Text = Color.FromHex("#E6EDF3"),
        TextMuted = Color.FromHex("#8A9BAB"),
        TextDisabled = Color.FromHex("#8A9BAB80"),
        Ink = Color.FromHex("#0A0E13"),
        Glass = Color.FromHex("#0E141B"),
        GlassRaised = Color.FromHex("#131B24"),
        GlassLight = Color.FromHex("#1B2733"),
        Edge = Color.FromHex("#2A3B4D"),
        EdgeSoft = Color.FromHex("#23303E"),
        EdgeLight = Color.FromHex("#425A72"),
        Good = Color.FromHex("#3CE39B"),
        Caution = Color.FromHex("#FFB547"),
        Danger = Color.FromHex("#FF4D5E"),

        ButtonDefault = Color.FromHex("#182330"),
        ButtonHovered = Color.FromHex("#2A4157"),
        ButtonPressed = Color.FromHex("#1E4F63"),
        ButtonDisabled = Color.FromHex("#10161C"),
        ButtonCautionDefault = Color.FromHex("#7A1F2B"),
        ButtonCautionHovered = Color.FromHex("#A62A3A"),
        ButtonCautionPressed = Color.FromHex("#4A121A"),
        ButtonCautionDisabled = Color.FromHex("#3A1418"),
        ButtonGoodDefault = Color.FromHex("#157A56"),
        ButtonGoodHovered = Color.FromHex("#1E9E6E"),
        ButtonGoodDisabled = Color.FromHex("#0C4632"),
        ButtonDangerDefault = Color.FromHex("#C8384A"),
        ButtonDangerHovered = Color.FromHex("#E4556A"),
        TopButtonDefault = Color.FromHex("#121A23"),
        ContextDefault = Color.FromHex("#0D131A40"),
        ContextHovered = Color.FromHex("#2A4157"),
        ContextPressed = Color.FromHex("#3B5470"),
        ChatBackground = Color.FromHex("#0B1117E0"),
        Selection = Color.FromHex("#1E4A5C"),
    };

    /// <summary>
    /// Aphelion cassette futurism: amber readouts on charcoal, warm ivory type, dusty olive and faded orange,
    /// rounded painted-plastic chrome, scanlines and a riveted bezel around the lobby.
    /// </summary>
    public static readonly WolfgateSkin Retro = new()
    {
        Id = "WolfgateRetro",
        Name = "wf-ui-style-retro",
        TexturePath = "/Textures/_WF/Stylesheets/Interface/WolfgateRetro/Style/",
        HudTheme = "WFRetroTheme",
        DisplayFonts = new[] { "/Fonts/Boxfont-round/Boxfont Round.ttf" }.Concat(NotoFallback).ToArray(),
        MenuFonts = MonoStack,
        MonoFonts = MonoStack,

        Accent = Color.FromHex("#F2A54A"),
        AccentDim = Color.FromHex("#A8702E"),
        Text = Color.FromHex("#EDE6D2"),
        TextMuted = Color.FromHex("#A39E8C"),
        TextDisabled = Color.FromHex("#A39E8C80"),
        Ink = Color.FromHex("#141412"),
        Glass = Color.FromHex("#1C1B18"),
        GlassRaised = Color.FromHex("#23221E"),
        GlassLight = Color.FromHex("#2E2C27"),
        Edge = Color.FromHex("#4B4739"),
        EdgeSoft = Color.FromHex("#39362D"),
        EdgeLight = Color.FromHex("#6B6553"),
        Good = Color.FromHex("#A6C85A"),
        Caution = Color.FromHex("#F2C14E"),
        Danger = Color.FromHex("#E0645A"),

        ButtonDefault = Color.FromHex("#2A2925"),
        ButtonHovered = Color.FromHex("#3D3B34"),
        ButtonPressed = Color.FromHex("#6A4A1E"),
        ButtonDisabled = Color.FromHex("#1E1D1A"),
        ButtonCautionDefault = Color.FromHex("#7A3226"),
        ButtonCautionHovered = Color.FromHex("#A3452F"),
        ButtonCautionPressed = Color.FromHex("#4E2016"),
        ButtonCautionDisabled = Color.FromHex("#3A1F18"),
        ButtonGoodDefault = Color.FromHex("#5E7A2A"),
        ButtonGoodHovered = Color.FromHex("#78993A"),
        ButtonGoodDisabled = Color.FromHex("#354618"),
        ButtonDangerDefault = Color.FromHex("#B8463A"),
        ButtonDangerHovered = Color.FromHex("#D65A4C"),
        TopButtonDefault = Color.FromHex("#22211D"),
        ContextDefault = Color.FromHex("#1C1B1840"),
        ContextHovered = Color.FromHex("#3D3B34"),
        ContextPressed = Color.FromHex("#54514A"),
        ChatBackground = Color.FromHex("#161513E0"),
        Selection = Color.FromHex("#5A4320"),
    };

    /// <summary>The skin of a player who never picked one. Its id is the default of the <c>wf.ui_style</c> CVar.</summary>
    public static readonly WolfgateSkin Default = Retro;

    /// <summary>Every skin, the default first, in the order the options menu lists them.</summary>
    public static readonly WolfgateSkin[] All = { Retro, Futurist };

    /// <summary>The skin with the given id, or the default skin for anything unknown.</summary>
    public static WolfgateSkin Get(string id)
    {
        return All.FirstOrDefault(skin => skin.Id == id) ?? Default;
    }
}
