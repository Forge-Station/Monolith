using Robust.Client.UserInterface;

namespace Content.Client._WF.Cockpit;

/// <summary>Keeps digital and mechanical cockpit instruments equally readable.</summary>
public static class WFCockpitInstrumentSizing
{
    /// <summary>Height of the full-size dial column: caption, heading, speed and turn dials, and the hull strip.</summary>
    public const float FullColumnHeight = 410;

    /// <summary>Column height taken by the panel frame and the fixed fuel strip, which the dial scroll cannot use.</summary>
    public const float ColumnChrome = 59;

    /// <summary>Full-size height of the stacked dials within <see cref="FullColumnHeight"/>.</summary>
    public const float DialStackHeight = 176 + 160;

    /// <summary>The smallest dial scale; below it the column scrolls instead.</summary>
    public const float MinScale = 0.625f;

    /// <summary>Scale steps, so a pixel of column height doesn't relayout the dials.</summary>
    private const float ScaleStep = 0.025f;

    /// <summary>Whether a left column of this height is too short to show the full-size dials without scrolling.</summary>
    public static bool UseCompact(float columnHeight) => columnHeight - ColumnChrome < FullColumnHeight;

    /// <summary>The dial scale that fills a left column of this height, between <see cref="MinScale"/> and 1.</summary>
    public static float DialScale(float columnHeight)
    {
        if (!UseCompact(columnHeight))
            return 1f;

        var room = columnHeight - ColumnChrome - (FullColumnHeight - DialStackHeight);
        var scale = MathF.Floor(room / DialStackHeight / ScaleStep) * ScaleStep;
        return Math.Clamp(scale, MinScale, 1f);
    }

    /// <summary>A dial's face height at the given scale.</summary>
    public static float ScaledHeight(float fullHeight, float scale) => MathF.Round(fullHeight * scale);

    /// <summary>The smaller face height used for a dial when space is short.</summary>
    public static float CompactHeight(float fullHeight) => ScaledHeight(fullHeight, MinScale);

    /// <summary>Applies the shared dial height and restores borrowed controls when the cockpit closes.</summary>
    public static void Bind(Control instrument, WFCockpitLease lease, float height)
    {
        var previous = instrument.SetHeight;
        instrument.SetHeight = height;
        lease.Remember(() => instrument.SetHeight = previous);
    }
}
