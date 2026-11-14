using System.Numerics;
using Robust.Client.UserInterface;

namespace Content.Client._WF.ExamineCard;

/// <summary>Lays children out left to right, wrapping onto a new row when one doesn't fit.</summary>
public sealed class ExamineChipFlow : Control
{
    public float Gap = 4;

    /// <summary>
    /// The row width to wrap at. A parent may measure with unlimited width and then arrange narrower, which would
    /// wrap rows the measure never counted and pile them onto whatever sits below.
    /// </summary>
    public float MaxRowWidth = float.PositiveInfinity;

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        availableSize.X = MathF.Min(availableSize.X, MaxRowWidth);
        var x = 0f;
        var y = 0f;
        var row = 0f;
        var width = 0f;
        foreach (var child in Children)
        {
            child.Measure(availableSize);
            var size = child.DesiredSize;
            if (x > 0 && x + size.X > availableSize.X)
            {
                y += row + Gap;
                x = 0;
                row = 0;
            }

            x += size.X + Gap;
            row = MathF.Max(row, size.Y);
            width = MathF.Max(width, x - Gap);
        }

        return new Vector2(width, y + row);
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        var width = MathF.Min(finalSize.X, MaxRowWidth);
        var x = 0f;
        var y = 0f;
        var row = 0f;
        foreach (var child in Children)
        {
            var size = child.DesiredSize;
            if (x > 0 && x + size.X > width)
            {
                y += row + Gap;
                x = 0;
                row = 0;
            }

            child.Arrange(UIBox2.FromDimensions(new Vector2(x, y), size));
            x += size.X + Gap;
            row = MathF.Max(row, size.Y);
        }

        return finalSize;
    }
}
