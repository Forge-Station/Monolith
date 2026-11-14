using System.Numerics;
using Content.Shared._WF.Wolfmed.Examine;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.Wolfmed.Examine;

/// <summary>
/// LOOK2: one body part in the examine's visual inspection. A left marker in the worst finding's colour, the
/// part's name in a column the rows share, and the findings as chips that wrap onto as many lines as they
/// need.
/// </summary>
public sealed class WolfmedLookRow : PanelContainer
{
    private const float NamePadding = 12f;

    /// <summary>Floor for the name column. Wide enough for "right hand" at the examine popup's font.</summary>
    private const float MinNameWidth = 84f;

    /// <summary>The name column, widened to match its siblings once every row exists.</summary>
    public readonly Label PartLabel;

    /// <summary>The chips, in the order the server sent them.</summary>
    public readonly WolfmedLookFlow Chips;

    private readonly float _maxWidth;

    public WolfmedLookRow(string name, Color accent, float maxWidth, bool alternate)
    {
        _maxWidth = maxWidth;
        MaxWidth = maxWidth;
        HorizontalExpand = true;
        PanelOverride = new StyleBoxFlat
        {
            BackgroundColor = alternate ? Color.FromHex("#252E3A90") : Color.FromHex("#1B222B90"),
            BorderColor = accent,
            BorderThickness = new Thickness(3, 0, 0, 0),
        };

        PartLabel = new Label
        {
            Text = name,
            VerticalAlignment = VAlignment.Top,
            MinWidth = MinNameWidth,
            FontColorOverride = Color.FromHex("#D8DEE9"),
        };

        Chips = new WolfmedLookFlow { MaxWidth = maxWidth - NamePadding };

        var row = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 6,
            MaxWidth = maxWidth,
            Margin = new Thickness(7, 2, 3, 2),
        };
        row.AddChild(PartLabel);
        row.AddChild(Chips);
        AddChild(row);
    }

    /// <summary>Lines the name columns of every row up, and gives the chips the rest of the width.</summary>
    public void SetNameWidth(float width)
    {
        // A floor, never a fixed width. The rows are measured before the popup is in the UI tree, where a label
        // has no font yet and measures as nothing: fixing the column to that hid every part name, so the rows
        // showed what was wrong and not where.
        width = MathF.Max(width, MinNameWidth);
        PartLabel.MinWidth = width;
        Chips.MaxWidth = MathF.Max(60f, _maxWidth - width - NamePadding - 10f);
    }
}

/// <summary>
/// LOOK2: one finding. A tinted analyzer pictogram, the short label, and the whole sentence on hover. The
/// mouse filter is what makes the tooltip work inside the examine popup: hit testing walks to the deepest
/// control that does not ignore the mouse, and the popup's own containers ignore it.
/// </summary>
public sealed class WolfmedLookChip : PanelContainer
{
    public WolfmedLookChip(Texture icon, Color colour, WolfmedLookObservation finding, float iconSize)
    {
        ToolTip = finding.Text;
        MouseFilter = MouseFilterMode.Pass;
        VerticalAlignment = VAlignment.Center;
        PanelOverride = new StyleBoxFlat { BackgroundColor = Color.FromHex("#1c1c22C0") };

        var row = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 3,
            Margin = new Thickness(4, 1),
        };
        row.AddChild(new TextureRect
        {
            Texture = icon,
            SetSize = new Vector2(iconSize, iconSize),
            Stretch = TextureRect.StretchMode.KeepAspectCentered,
            Modulate = colour,
            VerticalAlignment = VAlignment.Center,
        });
        row.AddChild(new Label
        {
            Text = finding.Label,
            FontColorOverride = colour,
            VerticalAlignment = VAlignment.Center,
        });
        AddChild(row);
    }
}

/// <summary>
/// LOOK2: lays its children out left to right and wraps to a new line when the next one will not fit. Six
/// findings on one mauled leg are what this exists for; a BoxContainer would run them off the popup.
/// </summary>
public sealed class WolfmedLookFlow : Container
{
    /// <summary>Gap between two chips on the same line.</summary>
    public float Separation = 3f;

    /// <summary>Gap between two lines of chips.</summary>
    public float LineSeparation = 2f;

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        var limit = availableSize.X;
        var width = 0f;
        var height = 0f;
        var lineWidth = 0f;
        var lineHeight = 0f;

        foreach (var child in Children)
        {
            child.Measure(new Vector2(limit, availableSize.Y));
            var size = child.DesiredSize;

            if (lineWidth > 0f && lineWidth + Separation + size.X > limit)
            {
                width = MathF.Max(width, lineWidth);
                height += lineHeight + LineSeparation;
                lineWidth = 0f;
                lineHeight = 0f;
            }

            lineWidth += (lineWidth > 0f ? Separation : 0f) + size.X;
            lineHeight = MathF.Max(lineHeight, size.Y);
        }

        return new Vector2(MathF.Max(width, lineWidth), height + lineHeight);
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        var x = 0f;
        var y = 0f;
        var lineHeight = 0f;

        foreach (var child in Children)
        {
            var size = child.DesiredSize;
            if (x > 0f && x + Separation + size.X > finalSize.X)
            {
                x = 0f;
                y += lineHeight + LineSeparation;
                lineHeight = 0f;
            }

            if (x > 0f)
                x += Separation;

            child.Arrange(UIBox2.FromDimensions(new Vector2(x, y), size));
            x += size.X;
            lineHeight = MathF.Max(lineHeight, size.Y);
        }

        return finalSize;
    }
}
