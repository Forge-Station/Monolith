using System.Linq;
using System.Numerics;
using Content.Shared._WF.Wolfmed.Hud;

namespace Content.Client._WF.Wolfmed.Overlays;

/// <summary>
/// Where the synthetic readout is allowed to draw. The overlay takes no input, so nothing here can steal a
/// click. Readouts sit inside the playfield, below the toolbar and chat, with a clear central sightline.
/// <see cref="Reserved"/> models the surrounding game UI rather than just the viewport edges.
/// </summary>
public static class WolfmedSyntheticHudLayout
{
    /// <summary>Gap from the screen edge to any block.</summary>
    public const float Margin = 16f;

    /// <summary>Gap from a block's frame to its text.</summary>
    public const float Padding = 6f;

    /// <summary>Font size at scale 1.</summary>
    public const float BaseFont = 11f;

    /// <summary>Most gauge rows the SYSTEM panel carries: chassis, power, fluid, servo, sensor, core.</summary>
    public const int MaxSystemGauges = 6;

    /// <summary>The advice wraps to a second row at most; anything longer ends in an ellipsis.</summary>
    public const int MaxAdviceRows = 2;

    /// <summary>SYSTEM panel width in monospace columns. The longest advice wraps once at this width.</summary>
    public const int SystemColumns = 28;

    /// <summary>Column the bar starts in: past the longest label ("CHASSIS") and a gap.</summary>
    public const int BarColumn = 9;

    /// <summary>Width of the value column the percentages are right-aligned in ("100%", "1140 K").</summary>
    public const int ValueColumns = 6;

    public const float PanelTop = 0.40f;
    public const float BottomBand = 0.78f;

    /// <summary>Smallest and largest the player's own text setting may be. A zero would collapse the blocks.</summary>
    public const float MinScale = 0.6f;

    public const float MaxScale = 2.5f;

    /// <summary>The viewport in the coordinates a screen-space overlay actually draws in.</summary>
    // ViewportBounds is in global physical pixels but the draw handle is already translated to the viewport's
    // top-left, so the global origin has to come back off or every block lands a viewport away from its corner.
    public static UIBox2 Screen(UIBox2i bounds, Vector2 origin) => new(
        bounds.Left - origin.X,
        bounds.Top - origin.Y,
        bounds.Right - origin.X,
        bounds.Bottom - origin.Y);

    /// <summary>
    /// The text scale as drawn: the player's setting times the control's UI scale, because the handle works
    /// in physical pixels and everything else on screen is sized that way too.
    /// </summary>
    public static float Scale(float setting, float uiScale) =>
        Math.Clamp(setting, MinScale, MaxScale) * MathF.Max(0.1f, uiScale);

    public static float Font(float scale) => MathF.Max(6f, MathF.Round(BaseFont * scale));

    public static float LineHeight(float scale) => Font(scale) * 1.4f + 4f;

    /// <summary>
    /// Monospace advance with a margin. The engine sizes fonts in points at 96 DPI (4/3 px a point) and RobotoMono
    /// advances 0.6 em, so a glyph is 0.8 of the font size wide; 0.868 leaves room for rounding.
    /// </summary>
    public static float CharWidth(float scale) => Font(scale) * 0.62f * 1.4f;

    /// <summary>Fit the requested size into the inset panels, leaving the central sightline open.</summary>
    public static float FitScale(UIBox2 screen, float requested)
    {
        var scale = requested;
        while (scale > 0.1f &&
               (SystemHeight(scale, MaxSystemGauges, MaxAdviceRows) > screen.Height * (BottomBand - PanelTop) ||
                SystemWidth(scale) > screen.Width * 0.26f ||
                42f * CharWidth(scale) + Padding * 2f > screen.Width * 0.30f))
            scale = MathF.Max(0.1f, scale - 0.05f);
        return scale;
    }

    public static float SystemWidth(float scale) => SystemColumns * CharWidth(scale) + Padding * 2f;

    /// <summary>Title, the gauges, a blank half-row, FAULTS, then the advice rows, inside the padding.</summary>
    public static float SystemHeight(float scale, int gauges, int adviceRows) =>
        (1f + gauges + 0.5f + 1f + Math.Clamp(adviceRows, 1, MaxAdviceRows)) * LineHeight(scale) + Padding * 2f;

    /// <summary>The chassis block at its largest, inset from the left-side action buttons.</summary>
    public static UIBox2 System(UIBox2 screen, float scale) =>
        SystemPanel(screen, scale, MaxSystemGauges, MaxAdviceRows).Box;

    /// <summary>
    /// The SYSTEM panel sized from what it holds: every row one line high and inside the padding, the box from the
    /// rows. Anchored at the same top-left whatever it holds, so it grows downwards.
    /// </summary>
    public static WolfmedSystemPanel SystemPanel(UIBox2 screen, float scale, int gauges, int adviceRows)
    {
        gauges = Math.Clamp(gauges, 0, MaxSystemGauges);
        adviceRows = Math.Clamp(adviceRows, 1, MaxAdviceRows);

        var line = LineHeight(scale);
        var width = MathF.Min(SystemWidth(scale), screen.Width * 0.26f);
        var left = screen.Left + screen.Width * 0.14f;
        var top = screen.Top + screen.Height * PanelTop;
        var box = new UIBox2(left, top, left + width, top + SystemHeight(scale, gauges, adviceRows));

        var y = top + Padding;
        UIBox2 Next(float rows = 1f)
        {
            var row = new UIBox2(left + Padding, y, left + width - Padding, y + line * rows);
            y += line * rows;
            return row;
        }

        var title = Next();
        var rows = new UIBox2[gauges];
        for (var i = 0; i < gauges; i++)
            rows[i] = Next();

        y += line * 0.5f;
        var faults = Next();
        var advice = new UIBox2[adviceRows];
        for (var i = 0; i < adviceRows; i++)
            advice[i] = Next();

        return new WolfmedSystemPanel(box, title, rows, faults, advice, line);
    }

    /// <summary>
    /// The advice ("ADVICE: REFILL FLUID") broken at words into rows of at most <paramref name="columns"/>
    /// characters, the continuation indented under the text after the prefix. Past <see cref="MaxAdviceRows"/> the
    /// last row ends in "...". A word longer than a row is cut.
    /// </summary>
    public static List<string> WrapAdvice(string text, int prefixLength, int columns = SystemColumns)
    {
        var rows = new List<string>();
        var indent = new string(' ', Math.Clamp(prefixLength, 0, columns / 2));
        var line = string.Empty;
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = line.Length == 0 ? (rows.Count == 0 ? word : indent + word) : line + " " + word;
            if (candidate.Length <= columns)
            {
                line = candidate;
                continue;
            }

            if (line.Length > 0)
                rows.Add(line);
            line = rows.Count == 0 ? word : indent + word;
            if (line.Length > columns)
                line = line[..columns];
        }

        if (line.Length > 0)
            rows.Add(line);
        if (rows.Count == 0)
            rows.Add(string.Empty);

        if (rows.Count <= MaxAdviceRows)
            return rows;

        var last = rows[MaxAdviceRows - 1];
        rows.RemoveRange(MaxAdviceRows - 1, rows.Count - MaxAdviceRows + 1);
        rows.Add((last.Length + 3 > columns ? last[..(columns - 3)] : last) + "...");
        return rows;
    }

    /// <summary>
    /// Which blocks a chassis's readout shows. Nothing at all while it is healthy: the idle "::" glyph drawn there
    /// before read as four stray dots in the upper left of the screen.
    /// </summary>
    public static (bool System, bool Diagnostics) Visible(WolfmedSyntheticTier tier, bool standby, bool dead, int faults) =>
        (tier >= WolfmedSyntheticTier.Light && !standby && !dead,
            tier >= WolfmedSyntheticTier.Moderate && faults > 0 && !standby && !dead);

    /// <summary>The fault list, below chat and inset from the right-side alerts.</summary>
    public static UIBox2 Diagnostics(UIBox2 screen, float scale, int lines)
    {
        var width = MathF.Min(42f * CharWidth(scale) + Padding * 2f,
            screen.Width * 0.30f);
        var height = (1 + Math.Max(lines, 1)) * LineHeight(scale) + Padding * 2f;
        var top = screen.Top + screen.Height * PanelTop;
        return new UIBox2(
            screen.Left + screen.Width * 0.90f - width,
            top,
            screen.Left + screen.Width * 0.90f,
            top + height);
    }

    /// <summary>Row <paramref name="row"/> of a block laid out one line per row inside the padding.</summary>
    public static UIBox2 Row(UIBox2 block, int row, float line) => new(
        block.Left + Padding,
        block.Top + Padding + row * line,
        block.Right - Padding,
        block.Top + Padding + (row + 1) * line);

    /// <summary>Fault lines that fit above the game HUD, never more than the wire carries.</summary>
    public static int MaxLines(UIBox2 screen, float scale, int cap)
    {
        var room = screen.Height * (BottomBand - PanelTop) - Padding * 2f - LineHeight(scale);
        return Math.Clamp((int) MathF.Floor(room / LineHeight(scale)), 1, cap);
    }

    /// <summary>
    /// A dedicated warning strip above both blocks; it cannot collapse into the gap between them.
    /// </summary>
    public static UIBox2 Banner(UIBox2 screen, float scale)
    {
        var height = LineHeight(scale) + Padding * 2f;
        var top = screen.Top + screen.Height * 0.25f;
        return new UIBox2(screen.Left + screen.Width * 0.32f, top,
            screen.Left + screen.Width * 0.68f, top + height);
    }

    /// <summary>
    /// Every box the readout draws in, by name, for the largest state it can be in: the SYSTEM panel and its rows,
    /// the fault list and its rows, and the warning banner. The standby, panic and death screens cover the whole
    /// viewport and are not listed.
    /// </summary>
    public static IEnumerable<(string Name, UIBox2 Box)> DrawnBoxes(UIBox2 screen, float scale, int gauges,
        int adviceRows, int faults)
    {
        var panel = SystemPanel(screen, scale, gauges, adviceRows);
        yield return ("system", panel.Box);
        foreach (var (name, row) in panel.Rows())
            yield return ($"system {name}", row);

        var lines = MaxLines(screen, scale, Math.Max(1, faults));
        var diagnostics = Diagnostics(screen, scale, lines);
        yield return ("diagnostics", diagnostics);
        for (var i = 0; i <= Math.Min(lines, faults); i++)
            yield return ($"diagnostics row {i}", Row(diagnostics, i, LineHeight(scale)));

        yield return ("banner", Banner(screen, scale));
    }

    /// <summary>
    /// The parts of the screen the game HUD owns. Approximate but deliberately generous: the hotbar and the
    /// held-item row along the bottom, the alerts column and the targeting doll on the right, and the chat
    /// pane beside them.
    /// </summary>
    public static IEnumerable<(string Name, UIBox2 Box)> Reserved(UIBox2 screen)
    {
        var w = screen.Width;
        var h = screen.Height;
        yield return ("toolbar", new UIBox2(screen.Left, screen.Top,
            screen.Left + w * 0.45f, screen.Top + h * 0.16f));
        yield return ("action buttons", new UIBox2(screen.Left, screen.Top + h * 0.16f,
            screen.Left + w * 0.12f, screen.Top + h * 0.82f));
        yield return ("hotbar", new UIBox2(
            screen.Left + w * 0.18f, screen.Top + h * 0.84f, screen.Left + w * 0.82f, screen.Bottom));
        yield return ("alerts", new UIBox2(
            screen.Left + w * 0.94f, screen.Top + h * 0.34f, screen.Right, screen.Top + h * 0.86f));
        yield return ("chat", new UIBox2(
            screen.Left + w * 0.70f, screen.Top, screen.Right, screen.Top + h * 0.38f));
        yield return ("doll", new UIBox2(
            screen.Left + w * 0.92f, screen.Top + h * 0.84f, screen.Right, screen.Bottom));
        yield return ("central sightline", new UIBox2(screen.Left + w * 0.42f, screen.Top + h * 0.42f,
            screen.Left + w * 0.58f, screen.Top + h * 0.64f));
    }

    /// <summary>Whether two rectangles share any area at all.</summary>
    public static bool Overlaps(UIBox2 left, UIBox2 right) =>
        left.Left < right.Right && right.Left < left.Right &&
        left.Top < right.Bottom && right.Top < left.Bottom;

    /// <summary>Whether <paramref name="inner"/> lies wholly inside <paramref name="outer"/>, to a hundredth of a pixel.</summary>
    public static bool Contains(UIBox2 outer, UIBox2 inner, float slack = 0.01f) =>
        inner.Left >= outer.Left - slack && inner.Right <= outer.Right + slack &&
        inner.Top >= outer.Top - slack && inner.Bottom <= outer.Bottom + slack;

    /// <summary>Top-left corner of a block's first text row.</summary>
    public static Vector2 TextOrigin(UIBox2 block, float scale) =>
        new(block.Left + Padding, block.Top + Padding);
}

/// <summary>The SYSTEM panel's box and the rectangle of every row in it, top to bottom.</summary>
public readonly record struct WolfmedSystemPanel(
    UIBox2 Box,
    UIBox2 Title,
    UIBox2[] Gauges,
    UIBox2 Faults,
    UIBox2[] Advice,
    float Line)
{
    public IEnumerable<(string Name, UIBox2 Box)> Rows()
    {
        yield return ("title", Title);
        for (var i = 0; i < Gauges.Length; i++)
            yield return ($"gauge {i}", Gauges[i]);
        yield return ("faults", Faults);
        for (var i = 0; i < Advice.Length; i++)
            yield return ($"advice {i}", Advice[i]);
    }
}
