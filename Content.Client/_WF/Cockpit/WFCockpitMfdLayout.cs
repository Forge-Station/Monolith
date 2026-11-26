using System.Numerics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using static Content.Client._WF.CombatConsole.WFInstrumentTheme;

namespace Content.Client._WF.Cockpit;

/// <summary>Keeps interactive MFD plots outside their separate scrolling detail region.</summary>
public static class WFCockpitMfdLayout
{
    /// <summary>Allocates a fixed share of the MFD to the plot without moving it when details scroll.</summary>
    public static Control Split(Control plot, Control details, float plotShare = 0.7f)
    {
        var plotHost = new Control { HorizontalExpand = true, VerticalExpand = true, SizeFlagsStretchRatio = plotShare };
        var detailsHost = new Control { HorizontalExpand = true, VerticalExpand = true, SizeFlagsStretchRatio = 1 - plotShare };
        plotHost.AddChild(plot);
        detailsHost.AddChild(details);
        return new WFCockpitMfdSplit(plotHost, detailsHost, plotShare) { HorizontalExpand = true, VerticalExpand = true };
    }

    /// <summary>Creates the only scrolling region for a page's plain controls and lists.</summary>
    public static Control Details(Control content)
    {
        content.VerticalAlignment = Control.VAlignment.Top;
        return Scroll(content);
    }
}

/// <summary>A plot above its details that takes most of a short page, so the plot stays usable and the details scroll.</summary>
public sealed class WFCockpitMfdSplit : BoxContainer
{
    /// <summary>Pages shorter than this give the plot <see cref="ShortShare"/> of their height.</summary>
    public const float ShortHeight = 320;
    public const float ShortShare = 0.85f;

    private readonly Control _plot;
    private readonly Control _details;
    private readonly float _share;

    public WFCockpitMfdSplit(Control plot, Control details, float share)
    {
        Orientation = LayoutOrientation.Vertical;
        SeparationOverride = 6;
        _plot = plot;
        _details = details;
        _share = share;
        AddChild(plot);
        AddChild(details);
    }

    /// <summary>The share of the height the plot gets on a page this tall.</summary>
    public static float PlotShare(float share, float height) => height < ShortHeight ? Math.Max(share, ShortShare) : share;

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        var share = PlotShare(_share, finalSize.Y);
        if (_plot.SizeFlagsStretchRatio != share)
        {
            _plot.SizeFlagsStretchRatio = share;
            _details.SizeFlagsStretchRatio = 1 - share;
        }
        return base.ArrangeOverride(finalSize);
    }
}
