using System.Linq;
using System.Numerics;
using Content.Client._WF.Wolfmed.Autodoc;
using Content.Shared._WF.Wolfmed.Hud;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Enums;

namespace Content.Client._WF.Wolfmed.Overlays;

/// <summary>Draws the synthetic helmet readout: diagnostics, warnings, standby and kernel-panic screens.</summary>
// Pure screen-space drawing with no controls, so it never takes a click, and nothing is drawn over the player.
// Values come from WolfmedSyntheticHudSystem and every box from WolfmedSyntheticHudLayout.
public sealed class WolfmedSyntheticHudOverlay : Overlay
{
    public static readonly Color Phosphor = Color.FromHex("#ff5148");
    public static readonly Color Dim = Color.FromHex("#d58b80");
    public static readonly Color Faint = Color.FromHex("#54201c");

    private static readonly Color Warn = Color.FromHex("#ffc24d");
    private static readonly Color Crit = Color.FromHex("#ff8a3c");
    private static readonly Color Fail = Color.FromHex("#ff5a5a");

    public override OverlaySpace Space => OverlaySpace.ScreenSpace;

    /// <summary>One fault as the readout draws it.</summary>
    public readonly record struct Row(string Tag, string Part, string Text, WolfmedSyntheticSeverity Severity, float Slide);

    /// <summary>One SYSTEM row: a label, an optional fixed-width bar, and a value right-aligned to the panel.</summary>
    public readonly record struct SystemRow(string Label, string? Bar, string Value);

    public float Scale = 1f;

    public float SystemAlpha;
    public float DiagnosticsAlpha;
    public float BannerAlpha;
    public float StandbyAlpha;
    public float PanicAlpha;
    public float DeathAlpha;

    public string Spinner = string.Empty;
    public string FaultsLabel = string.Empty;
    public int FaultCount;

    /// <summary>The worst fault's advice ("ADVICE: REFILL FLUID"), or the idle flavour line when nothing is wrong.</summary>
    public string Status = string.Empty;

    /// <summary>Characters of <see cref="Status"/> before the advice itself, so a wrapped row lines up under it.</summary>
    public int StatusPrefix;

    public string Banner = string.Empty;
    public WolfmedSyntheticSeverity BannerSeverity = WolfmedSyntheticSeverity.Crit;
    public bool RebootVisible;
    public string StandbyTitle = string.Empty;
    public string RebootText = string.Empty;
    public string PanicTitle = string.Empty;
    public string DeathTitle = string.Empty;
    public string DeathSub = string.Empty;

    public readonly List<SystemRow> SystemRows = new();
    public readonly List<Row> Rows = new();
    public readonly List<string> Panic = new();

    /// <summary>Sideways wobble, in pixels, applied to every block. Zero under reduced motion.</summary>
    public float Jitter;

    protected override bool BeforeDraw(in OverlayDrawArgs args) =>
        SystemAlpha > 0.004f || DiagnosticsAlpha > 0.004f ||
        BannerAlpha > 0.004f || StandbyAlpha > 0.004f || PanicAlpha > 0.004f || DeathAlpha > 0.004f;

    protected override void Draw(in OverlayDrawArgs args)
    {
        var handle = args.ScreenHandle;
        var control = args.ViewportControl as Control;
        var screen = WolfmedSyntheticHudLayout.Screen(args.ViewportBounds,
            control?.GlobalPixelPosition ?? Vector2i.Zero);
        if (screen.Width < 160f || screen.Height < 80f)
            return;

        // Small embedded viewports get one readable status strip instead of microscopic panels.
        if (screen.Width < 960f || screen.Height < 480f)
        {
            var compact = new UIBox2(screen.Left + screen.Width * 0.20f, screen.Top + screen.Height * 0.25f,
                screen.Left + screen.Width * 0.68f, screen.Top + screen.Height * 0.25f + 32f);
            var text = DeathAlpha > 0.004f ? DeathTitle : PanicAlpha > 0.004f ? PanicTitle :
                StandbyAlpha > 0.004f ? StandbyTitle : BannerAlpha > 0.004f ? Banner : Status;
            var alpha = MathF.Max(MathF.Max(DeathAlpha, PanicAlpha), MathF.Max(StandbyAlpha,
                MathF.Max(BannerAlpha, SystemAlpha)));
            Block(handle, compact, alpha);
            Text(handle, AutodocStyle.Mono(11), WolfmedSyntheticHudLayout.Row(compact, 0, 20f), text,
                Phosphor.WithAlpha(alpha));
            return;
        }

        var scale = WolfmedSyntheticHudLayout.FitScale(screen,
            WolfmedSyntheticHudLayout.Scale(Scale, control?.UIScale ?? 1f));
        var font = AutodocStyle.Mono((int) WolfmedSyntheticHudLayout.Font(scale));
        var bold = AutodocStyle.Mono((int) WolfmedSyntheticHudLayout.Font(scale), true);
        var line = WolfmedSyntheticHudLayout.LineHeight(scale);

        if (SystemAlpha > 0.004f)
            DrawSystem(handle, screen, font, bold, scale);

        if (DiagnosticsAlpha > 0.004f)
            DrawDiagnostics(handle, screen, font, bold, line, scale);

        if (BannerAlpha > 0.004f)
            DrawBanner(handle, screen, bold, scale);

        if (StandbyAlpha > 0.004f)
            DrawStandby(handle, screen, bold, font, line);

        if (PanicAlpha > 0.004f)
            DrawPanic(handle, screen, font, line);

        if (DeathAlpha > 0.004f)
            DrawDeath(handle, screen, bold, font, scale);
    }

    /// <summary>
    /// The chassis block, sized from its rows: the title, one row per system with its bar at a fixed column and its
    /// value right-aligned, a blank half-row, FAULTS, and the advice on its own row (two when long).
    /// </summary>
    private void DrawSystem(DrawingHandleScreen handle, UIBox2 screen, Font font, Font bold, float scale)
    {
        var advice = WolfmedSyntheticHudLayout.WrapAdvice(Status, StatusPrefix);
        var panel = WolfmedSyntheticHudLayout.SystemPanel(screen, scale, SystemRows.Count, advice.Count);
        Block(handle, Shift(panel.Box), SystemAlpha);

        // One text size for every row, so the rows read as one table.
        var size = TextScale(font, panel.Line);
        var colour = Dim.WithAlpha(SystemAlpha);
        var column = handle.GetDimensions(font, "0", size).X;

        var title = Shift(panel.Title);
        Text(handle, bold, title, Loc.GetString("wolfmed-synthetic-system"), Phosphor.WithAlpha(SystemAlpha), size);
        TextRight(handle, bold, title, Spinner, Phosphor.WithAlpha(SystemAlpha), size);

        for (var i = 0; i < SystemRows.Count && i < panel.Gauges.Length; i++)
        {
            var row = SystemRows[i];
            var area = Shift(panel.Gauges[i]);
            Text(handle, font, area, row.Label, colour, size);
            if (row.Bar != null)
            {
                Text(handle, font, new UIBox2(area.Left + column * WolfmedSyntheticHudLayout.BarColumn, area.Top,
                    area.Right, area.Bottom), row.Bar, colour, size);
            }

            TextRight(handle, font, area, row.Value, colour, size);
        }

        var faults = Shift(panel.Faults);
        Text(handle, font, faults, FaultsLabel, colour, size);
        TextRight(handle, font, faults, FaultCount.ToString(), colour, size);

        // The advice in the accent colour; the idle flavour line in the dim one.
        var tint = (Rows.Count > 0 ? Warn : Dim).WithAlpha(SystemAlpha * 0.9f);
        for (var i = 0; i < advice.Count && i < panel.Advice.Length; i++)
            Text(handle, font, Shift(panel.Advice[i]), advice[i], tint, size);
    }

    private void DrawDiagnostics(DrawingHandleScreen handle, UIBox2 screen, Font font, Font bold, float line, float scale)
    {
        // Never more lines than fit above the game HUD: a large text scale shortens the list instead.
        var lines = WolfmedSyntheticHudLayout.MaxLines(screen, scale, Math.Max(1, Rows.Count));
        var box = Shift(WolfmedSyntheticHudLayout.Diagnostics(screen, scale, lines));
        Block(handle, box, DiagnosticsAlpha);

        Text(handle, bold, WolfmedSyntheticHudLayout.Row(box, 0, line),
            $"{Loc.GetString("wolfmed-synthetic-diagnostics")}  {Math.Min(lines, Rows.Count)}/{Rows.Count}",
            Phosphor.WithAlpha(DiagnosticsAlpha));

        var index = 1;
        foreach (var row in Rows.Take(lines))
        {
            // Reveal in place so animation cannot carry a fault outside the viewport.
            var area = WolfmedSyntheticHudLayout.Row(box, index++, line);
            var alpha = DiagnosticsAlpha * Math.Clamp(row.Slide, 0.15f, 1f);
            var colour = Tint(row.Severity);
            Text(handle, bold, Column(area, 0f, 0.15f), row.Tag, colour.WithAlpha(alpha));
            Text(handle, font, Column(area, 0.15f, 0.46f), row.Part, Dim.WithAlpha(alpha));
            Text(handle, font, Column(area, 0.46f, 1f), row.Text, colour.WithAlpha(alpha));
        }
    }

    private void DrawBanner(DrawingHandleScreen handle, UIBox2 screen, Font bold, float scale)
    {
        var box = Shift(WolfmedSyntheticHudLayout.Banner(screen, scale));
        var colour = Tint(BannerSeverity);
        handle.DrawRect(box, Color.Black.WithAlpha(0.45f * BannerAlpha));
        Frame(handle, box, colour.WithAlpha(0.7f * BannerAlpha));

        Text(handle, bold, WolfmedSyntheticHudLayout.Row(box, 0, WolfmedSyntheticHudLayout.LineHeight(scale)), Banner,
            colour.WithAlpha(BannerAlpha), centred: true);
    }

    private void DrawStandby(DrawingHandleScreen handle, UIBox2 screen, Font bold, Font font, float line)
    {
        handle.DrawRect(screen, Color.FromHex("#04090c").WithAlpha(0.62f * StandbyAlpha));

        var centre = new Vector2(screen.Left + screen.Width / 2f, screen.Top + screen.Height / 2f);
        Text(handle, bold, new UIBox2(screen.Left + 16f, centre.Y - line * 3f,
            screen.Right - 16f, centre.Y), StandbyTitle, Phosphor.WithAlpha(StandbyAlpha * 0.85f), centred: true,
            maxScale: 3f);

        if (!RebootVisible)
            return;

        Text(handle, font, new UIBox2(screen.Left + 16f, centre.Y + line,
            screen.Right - 16f, centre.Y + line * 2f), RebootText, Dim.WithAlpha(StandbyAlpha), centred: true);
    }

    private void DrawPanic(DrawingHandleScreen handle, UIBox2 screen, Font font, float line)
    {
        handle.DrawRect(screen, Color.Black.WithAlpha(0.88f * PanicAlpha));

        var x = screen.Left + screen.Width * 0.08f;
        var y = screen.Top + screen.Height * 0.16f;
        Text(handle, font, new UIBox2(x, y, screen.Right - 16f, y + line), PanicTitle, Fail.WithAlpha(PanicAlpha));
        foreach (var row in Panic)
        {
            y += line;
            if (y + line > screen.Bottom - 16f)
                break;
            Text(handle, font, new UIBox2(x, y, screen.Right - 16f, y + line), row, Dim.WithAlpha(PanicAlpha * 0.85f));
        }
    }

    private void DrawDeath(DrawingHandleScreen handle, UIBox2 screen, Font bold, Font font, float scale)
    {
        var height = screen.Height * 0.16f;
        var middle = screen.Top + screen.Height / 2f;
        var core = new UIBox2(screen.Left, middle - height / 2f, screen.Right, middle + height / 2f);
        handle.DrawRect(core, Color.Black.WithAlpha(0.72f * DeathAlpha));

        Text(handle, bold, new UIBox2(core.Left + 16f, core.Top + 6f, core.Right - 16f, middle),
            DeathTitle, Phosphor.WithAlpha(DeathAlpha), centred: true, maxScale: 4f);
        Text(handle, font, new UIBox2(core.Left + 16f, middle, core.Right - 16f, core.Bottom - 6f),
            DeathSub, Dim.WithAlpha(DeathAlpha * 0.9f), centred: true);
    }

    /// <summary>A light glass backing and interrupted red brackets keep the world readable.</summary>
    private static void Block(DrawingHandleScreen handle, UIBox2 box, float alpha)
    {
        handle.DrawRect(box, Color.FromHex("#160706").WithAlpha(0.28f * alpha));
        for (var y = box.Top + 2f; y < box.Bottom; y += 3f)
            handle.DrawRect(new UIBox2(box.Left, y, box.Right, y + 1f), Faint.WithAlpha(0.18f * alpha));

        Frame(handle, box, Phosphor.WithAlpha(0.45f * alpha));
    }

    /// <summary>Interrupted brackets with clean, matching chamfered corners.</summary>
    private static void Frame(DrawingHandleScreen handle, UIBox2 box, Color colour)
    {
        var tick = MathF.Min(24f, box.Width / 4f);
        handle.DrawLine(new Vector2(box.Left + tick, box.Top), new Vector2(box.Right - tick, box.Top), colour);
        handle.DrawLine(new Vector2(box.Left + tick, box.Bottom), new Vector2(box.Right - tick, box.Bottom), colour);
        foreach (var corner in new[] {
                     new Vector2(box.Left, box.Top), new Vector2(box.Right, box.Top),
                     new Vector2(box.Left, box.Bottom), new Vector2(box.Right, box.Bottom) })
        {
            var dx = corner.X == box.Left ? 1f : -1f;
            var dy = corner.Y == box.Top ? 1f : -1f;
            handle.DrawLine(corner + new Vector2(dx * 8f, 0f), corner + new Vector2(dx * tick, 0f), colour);
            handle.DrawLine(corner + new Vector2(dx * 8f, 0f), corner + new Vector2(0f, dy * 8f), colour);
            handle.DrawLine(corner + new Vector2(0f, dy * 8f), corner + new Vector2(0f, dy * tick), colour);
        }
    }

    private UIBox2 Shift(UIBox2 box) =>
        Jitter == 0f ? box : new UIBox2(box.Left + Jitter, box.Top, box.Right + Jitter, box.Bottom);

    private static UIBox2 Column(UIBox2 row, float start, float end) => new(
        row.Left + row.Width * start, row.Top, row.Left + row.Width * end - 4f, row.Bottom);

    /// <summary>
    /// The largest text scale, at most <paramref name="maxScale"/>, whose line fits a row of this height with a pixel
    /// to spare above and below. Font metrics are rounded to pixels, so the chosen size is checked, not assumed.
    /// </summary>
    private static float TextScale(Font font, float height, float maxScale = 1f)
    {
        var scale = MathF.Min(maxScale, (height - 2f) / font.GetLineHeight(1f));
        while (scale > 0.05f && font.GetLineHeight(scale) > height - 2f)
            scale -= 0.01f;
        return scale;
    }

    /// <summary>Measure actual glyphs: font sizes are not pixel heights, and DrawString uses a top-left.</summary>
    private static void Text(DrawingHandleScreen handle, Font font, UIBox2 area, string text, Color colour,
        float? scale = null, bool centred = false, float maxScale = 1f)
    {
        if (area.Width <= 0f || area.Height <= 2f || text.Length == 0)
            return;

        var size = scale ?? TextScale(font, area.Height, maxScale);
        var display = Fit(handle, font, text, area.Width, size);
        var dimensions = handle.GetDimensions(font, display, size);
        if (display.Length == 0 || dimensions.X > area.Width)
            return;

        handle.DrawString(font, new Vector2(centred ? area.Left + (area.Width - dimensions.X) / 2f : area.Left,
            area.Top + (area.Height - dimensions.Y) / 2f), display, size, colour);
    }

    /// <summary>Text flush with the right edge of the row, for the values and the spinner.</summary>
    private static void TextRight(DrawingHandleScreen handle, Font font, UIBox2 area, string text, Color colour,
        float size)
    {
        if (area.Width <= 0f || area.Height <= 2f || text.Length == 0)
            return;

        var dimensions = handle.GetDimensions(font, text, size);
        if (dimensions.X > area.Width)
            return;

        handle.DrawString(font, new Vector2(area.Right - dimensions.X, area.Top + (area.Height - dimensions.Y) / 2f),
            text, size, colour);
    }

    /// <summary>The text, or as much of it as fits followed by "...".</summary>
    private static string Fit(DrawingHandleScreen handle, Font font, string text, float width, float size)
    {
        if (handle.GetDimensions(font, text, size).X <= width)
            return text;

        for (var length = text.Length - 1; length >= 0; length--)
        {
            var display = text[..length] + "...";
            if (handle.GetDimensions(font, display, size).X <= width)
                return display;
        }

        return string.Empty;
    }

    private static Color Tint(WolfmedSyntheticSeverity severity) => severity switch
    {
        WolfmedSyntheticSeverity.Warn => Warn,
        WolfmedSyntheticSeverity.Crit => Crit,
        WolfmedSyntheticSeverity.Fail => Fail,
        _ => Phosphor,
    };
}
