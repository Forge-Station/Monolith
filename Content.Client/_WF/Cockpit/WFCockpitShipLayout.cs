using System.Numerics;
using Robust.Client.UserInterface;

namespace Content.Client._WF.Cockpit;

/// <summary>Shares wide MFD space equally between the hull plot and its instruments.</summary>
public sealed class WFCockpitShipLayout : Control
{
    private const float WideWidth = 680;
    private const float Gap = 6;
    private readonly Control _plot;
    private readonly Control _details;

    /// <summary>Owns the temporary layout around the existing leased plot, gauges and overlay controls.</summary>
    public WFCockpitShipLayout(Control plot, Control details)
    {
        Name = "CockpitShipLayout";
        HorizontalExpand = VerticalExpand = true;
        _plot = plot;
        _details = details;
        AddChild(plot);
        AddChild(details);
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        var (plot, details) = Regions(availableSize);
        _plot.Measure(plot.Size);
        _details.Measure(details.Size);
        return Vector2.Zero;
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        var (plot, details) = Regions(finalSize);
        _plot.Measure(plot.Size);
        _details.Measure(details.Size);
        _plot.Arrange(plot);
        _details.Arrange(details);
        return finalSize;
    }

    private static (UIBox2 Plot, UIBox2 Details) Regions(Vector2 size)
    {
        if (size.X >= WideWidth)
        {
            var width = Math.Max(0, size.X - Gap);
            var plotWidth = width * 0.5f;
            return (UIBox2.FromDimensions(Vector2.Zero, new Vector2(plotWidth, size.Y)),
                UIBox2.FromDimensions(new Vector2(plotWidth + Gap, 0), new Vector2(width - plotWidth, size.Y)));
        }
        var height = Math.Max(0, size.Y - Gap);
        // A short MFD keeps the plot usable and lets the details scroll beneath it.
        var plotHeight = height * WFCockpitMfdSplit.PlotShare(0.7f, size.Y);
        return (UIBox2.FromDimensions(Vector2.Zero, new Vector2(size.X, plotHeight)),
            UIBox2.FromDimensions(new Vector2(0, plotHeight + Gap), new Vector2(size.X, height - plotHeight)));
    }
}
