using System.Numerics;
using Content.Client._WF.Wolfmed.Autodoc;
using Content.Client._WF.Wolfmed.Overlays;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Enums;

namespace Content.Client._WF.Synthetics;

/// <summary>
/// Where the bay-open overlay draws. Pure so a test can check it against the synthetic readout's panels and the
/// game HUD: the strip sits in the gap between the readout's lower band and the hotbar.
/// </summary>
public static class WFChassisBayLayout
{
    /// <summary>Top and bottom of the banner strip, as fractions of the viewport height.</summary>
    public const float StripTop = 0.795f;
    public const float StripBottom = 0.835f;

    /// <summary>The banner strip, bottom centre.</summary>
    public static UIBox2 Strip(UIBox2 screen) => new(
        screen.Left + screen.Width * 0.30f, screen.Top + screen.Height * StripTop,
        screen.Left + screen.Width * 0.70f, screen.Top + screen.Height * StripBottom);

    /// <summary>The small OVERCLOCK tag at the top, in the gap between the toolbar and the chat (review: the corner is chat's).</summary>
    public static UIBox2 Tag(UIBox2 screen, float scale)
    {
        var width = 14f * WolfmedSyntheticHudLayout.CharWidth(scale) + WolfmedSyntheticHudLayout.Padding * 2f;
        var height = WolfmedSyntheticHudLayout.LineHeight(scale) + WolfmedSyntheticHudLayout.Padding;
        var centre = screen.Left + screen.Width * 0.575f;
        var top = screen.Top + WolfmedSyntheticHudLayout.Margin;
        return new UIBox2(centre - width / 2f, top, centre + width / 2f, top + height);
    }
}

/// <summary>
/// Drawn for a synthetic player whose own chassis bay is open: an amber frame round the viewport and a pulsing
/// warning strip, and a small tag while overclocked. Screen space, no controls, so it never takes a click.
/// </summary>
public sealed class WFChassisBayOverlay : Overlay
{
    public static readonly Color Amber = AutodocStyle.Amber;

    public override OverlaySpace Space => OverlaySpace.ScreenSpace;

    public float BayAlpha;
    public float OverclockAlpha;

    /// <summary>Pulse phase in radians; the system advances it, or leaves it still under reduced motion.</summary>
    public float Pulse;

    public string BayText = string.Empty;
    public string OverclockText = string.Empty;

    protected override bool BeforeDraw(in OverlayDrawArgs args) => BayAlpha > 0.004f || OverclockAlpha > 0.004f;

    protected override void Draw(in OverlayDrawArgs args)
    {
        var handle = args.ScreenHandle;
        var control = args.ViewportControl as Control;
        var screen = WolfmedSyntheticHudLayout.Screen(args.ViewportBounds,
            control?.GlobalPixelPosition ?? Vector2i.Zero);
        if (screen.Width < 160f || screen.Height < 80f)
            return;

        var uiScale = control?.UIScale ?? 1f;
        var scale = WolfmedSyntheticHudLayout.Scale(1f, uiScale);
        var bold = AutodocStyle.Mono((int) WolfmedSyntheticHudLayout.Font(scale), true);
        var pulse = 0.65f + 0.35f * MathF.Sin(Pulse);

        if (BayAlpha > 0.004f)
        {
            DrawFrame(handle, screen, Amber.WithAlpha(0.55f * pulse * BayAlpha), MathF.Max(2f, MathF.Round(2f * uiScale)));
            DrawStrip(handle, WFChassisBayLayout.Strip(screen), bold, BayText, pulse * BayAlpha);
        }

        if (OverclockAlpha > 0.004f)
            DrawStrip(handle, WFChassisBayLayout.Tag(screen, scale), bold, OverclockText, OverclockAlpha);
    }

    private static void DrawFrame(DrawingHandleScreen handle, UIBox2 screen, Color colour, float thickness)
    {
        handle.DrawRect(new UIBox2(screen.Left, screen.Top, screen.Right, screen.Top + thickness), colour);
        handle.DrawRect(new UIBox2(screen.Left, screen.Bottom - thickness, screen.Right, screen.Bottom), colour);
        handle.DrawRect(new UIBox2(screen.Left, screen.Top, screen.Left + thickness, screen.Bottom), colour);
        handle.DrawRect(new UIBox2(screen.Right - thickness, screen.Top, screen.Right, screen.Bottom), colour);
    }

    private static void DrawStrip(DrawingHandleScreen handle, UIBox2 box, Font font, string text, float alpha)
    {
        handle.DrawRect(box, Color.FromHex("#160e02").WithAlpha(0.55f * alpha));
        handle.DrawRect(new UIBox2(box.Left, box.Top, box.Right, box.Top + 1f), Amber.WithAlpha(0.7f * alpha));
        handle.DrawRect(new UIBox2(box.Left, box.Bottom - 1f, box.Right, box.Bottom), Amber.WithAlpha(0.7f * alpha));

        if (text.Length == 0)
            return;

        // Shrink to fit rather than clip: the text is a warning and has to be readable whole.
        var size = MathF.Min(1f, (box.Height - 4f) / font.GetLineHeight(1f));
        var width = handle.GetDimensions(font, text, size).X;
        var room = box.Width - 8f;
        if (width > room)
            size *= room / width;

        var dimensions = handle.GetDimensions(font, text, size);
        handle.DrawString(font, new Vector2(box.Left + (box.Width - dimensions.X) / 2f,
            box.Top + (box.Height - dimensions.Y) / 2f), text, size, Amber.WithAlpha(alpha));
    }
}
