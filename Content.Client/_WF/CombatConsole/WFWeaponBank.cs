using System.Numerics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.CombatConsole;

/// <summary>Stacks fixed controls above and below a scrolling weapon list that takes only the height it needs.</summary>
public sealed class WFWeaponBank : Control
{
    /// <summary>Space between the head, the list and the tail.</summary>
    public const float Gap = 5;
    private readonly Control _head;
    private readonly Control _tail;
    private readonly WFWeaponGrid _grid;
    private readonly ScrollContainer _scroll;

    public WFWeaponBank(Control head, WFWeaponGrid grid, Control tail)
    {
        HorizontalExpand = VerticalExpand = true;
        _head = head;
        _grid = grid;
        _tail = tail;
        _scroll = new ScrollContainer { Name = "WfWeaponScroll", HScrollEnabled = false };
        _scroll.AddChild(WFInstrumentTheme.Detach(grid));
        grid.ContentChanged += InvalidateMeasure;
        AddChild(head);
        AddChild(_scroll);
        AddChild(tail);
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        _head.Measure(availableSize);
        _tail.Measure(availableSize);
        _scroll.Measure(availableSize);
        // Reports one entry for the list so later siblings keep their room; the stretch slot supplies the rest.
        var list = Math.Min(_grid.ContentHeight, _grid.EntryHeight);
        return new Vector2(Math.Max(_head.DesiredSize.X, _tail.DesiredSize.X), Stack(_head.DesiredSize.Y, list, _tail.DesiredSize.Y));
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        var head = _head.DesiredSize.Y;
        var tail = _tail.DesiredSize.Y;
        var room = Math.Max(0, finalSize.Y - head - tail - (head > 0 ? Gap : 0) - (tail > 0 ? Gap : 0));
        var list = Math.Min(_grid.ContentHeight, room);
        // A list that fits again starts from its top instead of keeping a stale offset.
        if (_grid.ContentHeight <= list + 0.5f && _scroll.VScroll != 0)
            _scroll.VScroll = 0;
        var y = 0f;
        _head.Arrange(UIBox2.FromDimensions(Vector2.Zero, new Vector2(finalSize.X, head)));
        y += head;
        if (list > 0)
        {
            y += head > 0 ? Gap : 0;
            _scroll.Arrange(UIBox2.FromDimensions(new Vector2(0, y), new Vector2(finalSize.X, list)));
            y += list;
        }
        else
        {
            _scroll.Arrange(UIBox2.FromDimensions(new Vector2(0, y), new Vector2(finalSize.X, 0)));
        }
        if (tail > 0)
            _tail.Arrange(UIBox2.FromDimensions(new Vector2(0, y + (y > 0 ? Gap : 0)), new Vector2(finalSize.X, tail)));
        return finalSize;
    }

    private static float Stack(float head, float list, float tail)
    {
        var total = head;
        if (list > 0)
            total += (total > 0 ? Gap : 0) + list;
        if (tail > 0)
            total += (total > 0 ? Gap : 0) + tail;
        return total;
    }
}
