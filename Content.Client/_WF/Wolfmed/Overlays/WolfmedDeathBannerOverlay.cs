using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.Enums;

namespace Content.Client._WF.Wolfmed.Overlays;

/// <summary>The band across the screen that tells a player their body has died. Values come from the dying effects system.</summary>
public sealed class WolfmedDeathBannerOverlay : Overlay
{
    [Dependency] private IResourceCache _cache = default!;

    private const int FontSize = 64;
    private static readonly Color TextColor = Color.FromHex("#b3121a");

    public override OverlaySpace Space => OverlaySpace.ScreenSpace;

    private readonly Font _font;
    private readonly Font _subFont;

    /// <summary>Opacity of the whole band, 0 to 1.</summary>
    public float Alpha;

    /// <summary>Locale key for the headline. BRAIN swaps it for the cardiac arrest wording.</summary>
    public string TitleKey = "wolfmed-death-banner";

    /// <summary>Locale key for the line under it.</summary>
    public string SubKey = "wolfmed-death-banner-sub";

    public WolfmedDeathBannerOverlay()
    {
        IoCManager.InjectDependencies(this);
        _font = new VectorFont(_cache.GetResource<FontResource>("/Fonts/NotoSans/NotoSans-Bold.ttf"), FontSize);
        _subFont = new VectorFont(_cache.GetResource<FontResource>("/Fonts/NotoSans/NotoSans-Regular.ttf"), FontSize / 4);
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args) => Alpha > 0.001f;

    protected override void Draw(in OverlayDrawArgs args)
    {
        var handle = args.ScreenHandle;
        // ViewportBounds is global physical pixels; the handle is already at the control's own top-left.
        var bounds = WolfmedSyntheticHudLayout.Screen(args.ViewportBounds,
            (args.ViewportControl as Control)?.GlobalPixelPosition ?? Vector2i.Zero);
        var height = bounds.Height * 0.16f;
        var middle = bounds.Top + bounds.Height / 2f;

        // Soft-edged band: a solid core with two fainter strips above and below it.
        var core = new UIBox2(bounds.Left, middle - height / 2f, bounds.Right, middle + height / 2f);
        handle.DrawRect(core, Color.Black.WithAlpha(0.72f * Alpha));
        var edge = height * 0.12f;
        handle.DrawRect(new UIBox2(core.Left, core.Top - edge, core.Right, core.Top), Color.Black.WithAlpha(0.35f * Alpha));
        handle.DrawRect(new UIBox2(core.Left, core.Bottom, core.Right, core.Bottom + edge), Color.Black.WithAlpha(0.35f * Alpha));

        var title = Loc.GetString(TitleKey);
        var scale = height * 0.5f / FontSize;
        var size = handle.GetDimensions(_font, title, scale);
        var centre = bounds.Left + bounds.Width / 2f;
        handle.DrawString(_font, new Vector2(centre - size.X / 2f, middle - size.Y * 0.62f), title, scale, TextColor.WithAlpha(Alpha));

        var sub = Loc.GetString(SubKey);
        var subSize = handle.GetDimensions(_subFont, sub, scale);
        handle.DrawString(_subFont, new Vector2(centre - subSize.X / 2f, middle + size.Y * 0.36f), sub, scale,
            Color.FromHex("#9a8f8f").WithAlpha(Alpha * 0.9f));
    }
}
