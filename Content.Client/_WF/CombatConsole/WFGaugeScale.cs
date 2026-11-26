using System.Numerics;

namespace Content.Client._WF.CombatConsole;

/// <summary>Maps finite telemetry onto an instrument scale without turning missing readings into zero.</summary>
public static class WFGaugeScale
{
    /// <summary>Returns a bounded needle position, or no needle for missing data and invalid scales.</summary>
    public static float? Fraction(double? value, double minimum, double maximum)
    {
        if (value is not { } number || !double.IsFinite(number) || !double.IsFinite(minimum) ||
            !double.IsFinite(maximum) || !double.IsFinite(maximum - minimum) || maximum <= minimum)
            return null;
        return (float) Math.Clamp((number - minimum) / (maximum - minimum), 0, 1);
    }

    /// <summary>Bounds font atlas sizes and rounds downward so fitted text never grows past its allowance.</summary>
    public static float FontScale(float scale) => float.IsFinite(scale) && scale >= 0.125f
        ? MathF.Floor(MathF.Min(scale, 4) * 8) / 8
        : 0;

    /// <summary>Reserves separate endpoint labels entirely inside the lower dial face.</summary>
    public static UIBox2 RoundLabelBounds(float radius, bool maximum) => UIBox2.FromDimensions(
        new Vector2((maximum ? 0.06f : -0.62f) * radius, 0.50f * radius), new Vector2(0.56f * radius, 0.24f * radius));

    /// <summary>Formats a reading with fixed decimals, so a value that rounds to zero is never shown as negative zero.</summary>
    public static string Fixed(double value, int decimals)
    {
        var rounded = Math.Round(value, decimals);
        return (rounded == 0 ? 0d : value).ToString(decimals == 0 ? "0" : decimals == 1 ? "0.0" : "0.00");
    }

    /// <summary>Pads a reading to the width of its widest scale label, so the digits and decimal point never shift.</summary>
    public static string Digits(double value, double minimum, double maximum, int decimals) =>
        Fixed(value, decimals).PadLeft(Math.Max(Fixed(minimum, decimals).Length, Fixed(maximum, decimals).Length));

    /// <summary>Where a round dial and its caption sit inside a control.</summary>
    public readonly record struct DialLayout(float Radius, Vector2 Center, float CaptionBaseline);

    /// <summary>Distance, in UI units, from a dial's lower edge to its caption's baseline; leaves a clear gap above the letters.</summary>
    public const float CaptionDrop = 16;

    /// <summary>Space, in UI units, kept clear beneath a dial caption's baseline for descenders and the control's edge.</summary>
    public const float CaptionMargin = 5;

    /// <summary>Gap, in UI units, kept above a dial.</summary>
    public const float DialTop = 2;

    /// <summary>
    /// Lays out a dial with its caption directly beneath it, so a control stretched taller than the dial
    /// keeps them together.
    /// </summary>
    public static DialLayout RoundDial(float width, float height, float scale)
    {
        var top = DialTop * scale;
        var below = (CaptionDrop + CaptionMargin) * scale;
        var radius = MathF.Max(1, MathF.Min(width / 2 - 3 * scale, (height - top - below) / 2));
        var offset = MathF.Max(0, (height - top - 2 * radius - below) / 2);
        var center = new Vector2(width / 2, offset + top + radius);
        return new DialLayout(radius, center, center.Y + radius + CaptionDrop * scale);
    }

    /// <summary>The figure and unit areas inside a ring dial, clear of the ring itself.</summary>
    public static (UIBox2 Digits, UIBox2 Unit) RingReadout(Vector2 center, float radius, float scale)
    {
        var inner = MathF.Max(1, radius - 12 * scale);
        return (new UIBox2(center + new Vector2(-0.84f, -0.52f) * inner, center + new Vector2(0.84f, 0.12f) * inner),
            new UIBox2(center + new Vector2(-0.70f, 0.18f) * inner, center + new Vector2(0.70f, 0.52f) * inner));
    }

    /// <summary>Places a compass panel and the text band beneath it as one block, centred in a control of this size.</summary>
    public static (UIBox2 Panel, UIBox2 Text) HeadingPanel(float width, float height, float scale)
    {
        var band = 26 * scale;
        var side = MathF.Max(1, MathF.Min(width, height - band));
        var offset = MathF.Max(0, (height - side - band) / 2);
        var panel = UIBox2.FromDimensions(new Vector2((width - side) / 2, offset), new Vector2(side));
        return (panel, UIBox2.FromDimensions(new Vector2(panel.Left, panel.Bottom + 2 * scale), new Vector2(side, band - 4 * scale)));
    }

    /// <summary>Centres a linear instrument's fixed-height content in a control that may be taller than it.</summary>
    public static float StripOffset(float height, float content) => MathF.Max(0, (height - content) / 2);

    /// <summary>Chooses a labelled 1/2/5 scale for quantities whose capacity is not supplied by telemetry.</summary>
    public static double Ceiling(double value, double floor = 10)
    {
        if (!double.IsFinite(value) || value <= floor)
            return floor;
        var decade = Math.Pow(10, Math.Floor(Math.Log10(value)));
        var steps = value / decade;
        return (steps <= 1 ? 1 : steps <= 2 ? 2 : steps <= 5 ? 5 : 10) * decade;
    }
}
