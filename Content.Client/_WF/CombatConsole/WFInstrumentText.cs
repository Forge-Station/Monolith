using System.Numerics;
using Robust.Client.Graphics;

namespace Content.Client._WF.CombatConsole;

/// <summary>Fits instrument text with a finite set of font raster sizes.</summary>
public static class WFInstrumentText
{
    /// <summary>Figures have no ascenders or descenders, so their line box may exceed the band they fill by this factor.</summary>
    public const float DigitLine = 1.6f;

    /// <summary>Size of a unit or tag beside a figure, relative to the figure.</summary>
    public const float TagScale = 0.7f;

    /// <summary>Point size of the figures in linear gauges and the coordinates box, small enough to sit clear of their edges.</summary>
    public const int StripDigitSize = 15;

    /// <summary>Returns a bounded scale fitting the text's width and optional full height, or zero if too small.</summary>
    public static float FitScale(DrawingHandleScreen handle, Font font, string text, float maximum, float width,
        float height = float.PositiveInfinity)
    {
        var scale = WFGaugeScale.FontScale(maximum);
        if (scale == 0 || width <= 0 || height <= 0)
            return 0;
        var measured = handle.GetDimensions(font, text, scale).X;
        scale = WFGaugeScale.FontScale(scale * MathF.Min(1, MathF.Min(width / MathF.Max(1, measured),
            height / Math.Max(1, font.GetHeight(scale)))));
        // Raster rounding can add a pixel even when the proportional fit fits exactly.
        while (scale > 0 && (handle.GetDimensions(font, text, scale).X > width || font.GetHeight(scale) > height))
            scale = WFGaugeScale.FontScale(scale - 0.125f);
        return scale;
    }

    /// <summary>Fits figures to their band by glyph height rather than the whole line box, so they fill it.</summary>
    public static float FitDigits(DrawingHandleScreen handle, Font font, string text, float maximum, float width, float height) =>
        FitScale(handle, font, text, maximum, width, height * DigitLine);

    /// <summary>Draws figures centred in a box, as large as the box allows.</summary>
    public static void DrawFitted(DrawingHandleScreen handle, Font font, string text, UIBox2 box, float maximum, Color color)
    {
        var scale = FitDigits(handle, font, text, maximum, box.Width, box.Height);
        if (scale == 0)
            return;
        var width = handle.GetDimensions(font, text, scale).X;
        handle.DrawString(font, box.Center - new Vector2(width / 2, font.GetHeight(scale) / 2f), text, scale, color);
    }

    /// <summary>
    /// Draws a figure with a smaller tag beside it as one centred piece: the tag follows the figure, or leads it
    /// when <paramref name="tagFirst"/> is set. Both are scaled together to the largest size that fits the box.
    /// </summary>
    public static void DrawReading(DrawingHandleScreen handle, Font font, string digits, string tag, UIBox2 box, float maximum,
        Color color, Color tagColor, bool tagFirst = false)
    {
        var scale = FitReading(handle, font, digits, tag, maximum, box.Width, box.Height);
        if (scale == 0)
            return;
        var tagScale = TagFontScale(scale);
        var showTag = tag.Length > 0 && tagScale > 0;
        var digitWidth = handle.GetDimensions(font, digits, scale).X;
        var tagWidth = showTag ? handle.GetDimensions(font, tag, tagScale).X : 0;
        var gap = showTag ? TagGap(scale) : 0;
        var x = box.Center.X - (digitWidth + gap + tagWidth) / 2;
        var top = box.Center.Y - font.GetHeight(scale) / 2f;
        var baseline = top + font.GetAscent(scale);
        handle.DrawString(font, new Vector2(tagFirst ? x + tagWidth + gap : x, top), digits, scale, color);
        if (showTag)
            handle.DrawString(font, new Vector2(tagFirst ? x : x + digitWidth + gap, baseline - font.GetAscent(tagScale)),
                tag, tagScale, tagColor);
    }

    /// <summary>The largest scale at which a figure and its tag fit the box width and the figure fits its band.</summary>
    public static float FitReading(DrawingHandleScreen handle, Font font, string digits, string tag, float maximum, float width, float height)
    {
        var scale = WFGaugeScale.FontScale(maximum);
        if (scale == 0 || width <= 0 || height <= 0)
            return 0;
        var allowed = height * DigitLine;
        var measured = ReadingWidth(handle, font, digits, tag, scale);
        scale = WFGaugeScale.FontScale(scale * MathF.Min(1, MathF.Min(width / MathF.Max(1, measured),
            allowed / Math.Max(1, font.GetHeight(scale)))));
        while (scale > 0 && (ReadingWidth(handle, font, digits, tag, scale) > width || font.GetHeight(scale) > allowed))
            scale = WFGaugeScale.FontScale(scale - 0.125f);
        return scale;
    }

    private static float ReadingWidth(DrawingHandleScreen handle, Font font, string digits, string tag, float scale)
    {
        var tagScale = TagFontScale(scale);
        return handle.GetDimensions(font, digits, scale).X +
            (tag.Length == 0 || tagScale == 0 ? 0 : TagGap(scale) + handle.GetDimensions(font, tag, tagScale).X);
    }

    private static float TagFontScale(float scale) => WFGaugeScale.FontScale(scale * TagScale);

    private static float TagGap(float scale) => 4 * scale;
}
