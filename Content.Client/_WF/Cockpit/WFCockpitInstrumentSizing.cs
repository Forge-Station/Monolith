using Robust.Client.UserInterface;

namespace Content.Client._WF.Cockpit;

/// <summary>Keeps digital and mechanical cockpit instruments equally readable.</summary>
public static class WFCockpitInstrumentSizing
{
    /// <summary>Height of the full-size dial column: caption, heading, speed and turn dials, and the hull strip.</summary>
    public const float FullColumnHeight = 410;

    /// <summary>Column height taken by the panel frame and the fixed fuel strip, which the dial scroll cannot use.</summary>
    public const float ColumnChrome = 59;

    /// <summary>Whether a left column of this height is too short to show the full-size dials without scrolling.</summary>
    public static bool UseCompact(float columnHeight) => columnHeight - ColumnChrome < FullColumnHeight;

    /// <summary>The smaller face height used for a dial when space is short.</summary>
    public static float CompactHeight(float fullHeight) => MathF.Round(fullHeight * 0.625f);

    /// <summary>Applies the shared dial height and restores borrowed controls when the cockpit closes.</summary>
    public static void Bind(Control instrument, WFCockpitLease lease, float height)
    {
        var previous = instrument.SetHeight;
        instrument.SetHeight = height;
        lease.Remember(() => instrument.SetHeight = previous);
    }
}
