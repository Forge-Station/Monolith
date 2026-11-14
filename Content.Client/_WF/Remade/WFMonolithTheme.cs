using Content.Client.Resources;
using Content.Shared._WF.Remade;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;

namespace Content.Client._WF.Remade;

/// <summary>Colours, panels and fonts for the Monolith's interfaces: black stone cut with red writing.</summary>
public static class WFMonolithTheme
{
    public const string GlyphFontPath = "/Fonts/_DS13/Shage/Shage.ttf";
    public const string TextFontPath = "/Fonts/Iceberg/Iceberg-Regular.ttf";

    public static readonly Color Stone = Color.FromHex("#0a0505");
    public static readonly Color Slab = Color.FromHex("#140908");
    public static readonly Color Seam = Color.FromHex("#4a1410");
    public static readonly Color Vein = Color.FromHex("#7a1c14");
    public static readonly Color Ember = Color.FromHex("#e0452c");
    public static readonly Color Glow = Color.FromHex("#ffb08a");
    public static readonly Color Bone = Color.FromHex("#d6c8b0");
    public static readonly Color Ash = Color.FromHex("#8a7a6a");
    public static readonly Color DeadGlyph = Color.FromHex("#3a120e");

    /// <summary>The stone's colour at a stage: a dull ember when quiet, white-hot when rooted.</summary>
    public static Color StageColor(WFMonolithStage stage)
    {
        return stage switch
        {
            WFMonolithStage.Quiet => Color.FromHex("#8a3324"),
            WFMonolithStage.Hymn => Color.FromHex("#a8301f"),
            WFMonolithStage.Growth => Color.FromHex("#c4341f"),
            WFMonolithStage.Harvest => Color.FromHex("#dc3f27"),
            WFMonolithStage.Rapture => Color.FromHex("#ff5a2a"),
            _ => Color.FromHex("#ff9a52"),
        };
    }

    public static StyleBoxFlat Panel(Color background, Color border, float thickness = 1f, float margin = 0f)
    {
        var box = new StyleBoxFlat
        {
            BackgroundColor = background,
            BorderColor = border,
            BorderThickness = new Thickness(thickness),
        };
        box.SetContentMarginOverride(StyleBox.Margin.All, margin);
        return box;
    }

    /// <summary>A slab with a lit seam down its left edge.</summary>
    public static StyleBoxFlat Etched(Color seam, float margin = 6f)
    {
        var box = new StyleBoxFlat
        {
            BackgroundColor = Slab,
            BorderColor = seam,
            BorderThickness = new Thickness(3, 0, 0, 0),
        };
        box.SetContentMarginOverride(StyleBox.Margin.All, margin);
        return box;
    }

    public static Font Glyphs(IResourceCache cache, int size) => cache.GetFont(GlyphFontPath, size);

    public static Font Text(IResourceCache cache, int size) => cache.GetFont(TextFontPath, size);
}
