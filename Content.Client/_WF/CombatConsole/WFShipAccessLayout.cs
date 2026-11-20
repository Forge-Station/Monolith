using System.Numerics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using ConsoleTheme = Content.Client._WF.CombatConsole.WFInstrumentTheme;

namespace Content.Client._WF.CombatConsole;

/// <summary>Keeps the door diagram dominant beside access controls or above compact details.</summary>
public sealed class WFShipAccessLayout : Control
{
    /// <summary>The page lays out in columns from this size; below it the details scroll.</summary>
    public const float WideMinWidth = 900;
    public const float WideMinHeight = 400;
    private const float Gap = 8;
    private const float ControlColumnWidth = 248;

    /// <summary>The height a stacked diagram needs to leave a usable plot inside its frame.</summary>
    private const float PlotWanted = 206;

    /// <summary>The least a stacked page leaves for its details, which scroll.</summary>
    private const float DetailsMin = 30;
    private readonly BoxContainer _plot;
    private readonly Control _settings;
    private readonly Control _people;
    private readonly Control _doors;
    private readonly Control _legend;
    private readonly WFAccessListRegion[] _lists;
    private readonly BoxContainer _stack;
    private readonly ScrollContainer _details;
    private bool _wide;

    public WFShipAccessLayout(BoxContainer plot, Control settings, Control people, Control doors,
        Control legend, params WFAccessListRegion[] lists)
    {
        Name = "WfAccessLayout";
        HorizontalExpand = VerticalExpand = true;
        _plot = plot;
        _settings = settings;
        _people = people;
        _doors = doors;
        _people.VerticalExpand = _doors.VerticalExpand = false;
        _legend = legend;
        _lists = lists;
        _stack = ConsoleTheme.Column(settings, people, doors, legend);
        _details = ConsoleTheme.Scroll(_stack);
        _details.Name = "WfAccessDetails";
        AddChild(plot);
        AddChild(_details);
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        Reflow(availableSize.X >= WideMinWidth && availableSize.Y >= WideMinHeight);
        Layout(availableSize, false);
        return Vector2.Zero;
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        Reflow(finalSize.X >= WideMinWidth && finalSize.Y >= WideMinHeight);
        Layout(finalSize, true);
        return finalSize;
    }

    private void Reflow(bool wide)
    {
        if (_wide == wide)
            return;
        _wide = wide;
        if (_legend is GridContainer legend)
            legend.Columns = wide ? 2 : 1;
        foreach (var list in _lists)
            list.SetScrolling(wide);
        _details.Visible = !wide;
        foreach (var panel in new[] { _settings, _people, _doors })
        {
            ConsoleTheme.Detach(panel);
            if (wide) AddChild(panel);
            else _stack.AddChild(panel);
        }
        ConsoleTheme.Detach(_legend);
        if (wide) _plot.AddChild(_legend);
        else _stack.AddChild(_legend);
        _people.VerticalExpand = _doors.VerticalExpand = wide;
        InvalidateArrange();
    }

    private void Layout(Vector2 size, bool arrange)
    {
        if (!float.IsFinite(size.X) || !float.IsFinite(size.Y))
            return;
        void Place(Control control, UIBox2 bounds)
        {
            control.Measure(bounds.Size);
            if (arrange) control.Arrange(bounds);
        }
        if (_wide)
        {
            var width = Math.Max(0, size.X - 2 * Gap);
            var plotWidth = Math.Min(width * 0.48f, Math.Max(0, width - 2 * ControlColumnWidth));
            var column = (width - plotWidth) / 2;
            var middle = plotWidth + Gap;
            _settings.Measure(new Vector2(column, size.Y));
            var settingsHeight = Math.Min(size.Y, _settings.DesiredSize.Y);
            Place(_plot, UIBox2.FromDimensions(Vector2.Zero, new Vector2(plotWidth, size.Y)));
            Place(_settings, UIBox2.FromDimensions(new Vector2(middle, 0), new Vector2(column, settingsHeight)));
            Place(_people, UIBox2.FromDimensions(new Vector2(middle, settingsHeight + Gap),
                new Vector2(column, Math.Max(0, size.Y - settingsHeight - Gap))));
            Place(_doors, UIBox2.FromDimensions(new Vector2(middle + column + Gap, 0), new Vector2(column, size.Y)));
            return;
        }
        if (size.X >= 680)
        {
            var plotWidth = Math.Max(0, size.X - Gap) * 0.52f;
            Place(_plot, UIBox2.FromDimensions(Vector2.Zero, new Vector2(plotWidth, size.Y)));
            Place(_details, UIBox2.FromDimensions(new Vector2(plotWidth + Gap, 0), new Vector2(Math.Max(0, size.X - plotWidth - Gap), size.Y)));
            return;
        }
        // The diagram takes 60% of the page, or the room a usable plot needs when that is more; the details scroll in the rest.
        var room = Math.Max(0, size.Y - Gap);
        var plotHeight = Math.Clamp(Math.Max(room * 0.60f, Math.Min(PlotWanted, room - DetailsMin)), 0, room);
        Place(_plot, UIBox2.FromDimensions(Vector2.Zero, new Vector2(size.X, plotHeight)));
        Place(_details, UIBox2.FromDimensions(new Vector2(0, plotHeight + Gap), new Vector2(size.X, Math.Max(0, size.Y - plotHeight - Gap))));
    }
}

/// <summary>Scrolls lists only in their wide region, never inside another active scroller.</summary>
public sealed class WFAccessListRegion : Control
{
    private const float ListMinHeight = 48;
    private readonly Control _content;
    private readonly ScrollContainer _scroll;
    private bool _scrolling;

    public WFAccessListRegion(Control content)
    {
        _content = ConsoleTheme.Detach(content);
        _scroll = new ScrollContainer { HScrollEnabled = false, VerticalExpand = true, HorizontalExpand = true, Visible = false };
        HorizontalExpand = true;
        AddChild(_scroll);
        AddChild(_content);
    }

    /// <summary>Moves the original list between its scroller and the compact page's plain content.</summary>
    public void SetScrolling(bool scrolling)
    {
        if (_scrolling == scrolling) return;
        _scrolling = scrolling;
        ConsoleTheme.Detach(_content);
        _scroll.Visible = scrolling;
        if (scrolling) _scroll.AddChild(_content);
        else AddChild(_content);
        VerticalExpand = scrolling;
        InvalidateMeasure();
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        var size = base.MeasureOverride(availableSize);
        // A scrolling list with no rows drops its minimum height; the notice that stands in for it needs only its own.
        return _scrolling ? new Vector2(size.X, _content.ChildCount > 0 ? Math.Max(size.Y, ListMinHeight) : 0) : size;
    }
}
