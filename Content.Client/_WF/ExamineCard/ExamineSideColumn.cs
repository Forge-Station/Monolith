using System.Numerics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.ExamineCard;

/// <summary>
/// The panels beside the card, stacked. Together they never run past the card's bottom: open panels split its
/// height equally and each scrolls inside its share.
/// </summary>
public sealed class ExamineSideColumn : Control
{
    /// <summary>Smallest share a panel's scroll area gets, so a crowded column still shows some text.</summary>
    private const float MinScroll = 40f;

    public float Gap = 4;

    /// <summary>The card whose height the column keeps to. It is measured first, as the row's earlier child.</summary>
    public Control? Card;

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        var visible = new List<Control>();
        foreach (var child in Children)
        {
            if (child.Visible)
                visible.Add(child);
        }

        if (visible.Count == 0)
            return Vector2.Zero;

        var limit = Card is { } card ? card.DesiredSize.Y - Margin.SumVertical : availableSize.Y;
        var share = (limit - Gap * (visible.Count - 1)) / visible.Count;
        var unbounded = new Vector2(availableSize.X, float.PositiveInfinity);

        var width = 0f;
        var height = 0f;
        foreach (var panel in visible)
        {
            panel.Measure(unbounded);
            if (FindScroll(panel) is { } scroll)
            {
                // Everything in the panel except the scroll area, then the scroll area gets the rest of the share.
                var chrome = panel.DesiredSize.Y - scroll.DesiredSize.Y;
                var max = MathF.Max(MinScroll, share - chrome);
                if (MathF.Abs(scroll.MaxHeight - max) > 0.5f)
                {
                    scroll.MaxHeight = max;

                    // Invalidation is queued, so the cached sizes between the scroll area and the panel are cleared
                    // here, or the re-measure would return them unchanged.
                    for (Control? c = scroll; c != null && c != this; c = c.Parent)
                    {
                        c.InvalidateMeasure();
                    }
                    panel.Measure(unbounded);
                }
            }

            width = MathF.Max(width, panel.DesiredSize.X);
            height += panel.DesiredSize.Y;
        }

        return new Vector2(width, height + Gap * (visible.Count - 1));
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        var y = 0f;
        foreach (var child in Children)
        {
            if (!child.Visible)
                continue;

            child.Arrange(UIBox2.FromDimensions(new Vector2(0, y), new Vector2(finalSize.X, child.DesiredSize.Y)));
            y += child.DesiredSize.Y + Gap;
        }

        return finalSize;
    }

    private static ScrollContainer? FindScroll(Control control)
    {
        foreach (var child in control.Children)
        {
            if (child is ScrollContainer scroll)
                return scroll;

            if (FindScroll(child) is { } found)
                return found;
        }

        return null;
    }
}
