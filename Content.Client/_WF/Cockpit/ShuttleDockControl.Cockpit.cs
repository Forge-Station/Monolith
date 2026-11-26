using System.Numerics;
using Content.Client._WF.Cockpit;
using Content.Client._WF.CombatConsole;
using Content.Shared.Shuttles.BUIStates;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;

namespace Content.Client.Shuttles.UI;

public sealed partial class ShuttleDockControl
{
    /// <summary>Smallest click radius around a port, in UI units, so a port stays clickable when the plot is zoomed out.</summary>
    private const float WfPortReachMinimum = 14f;

    private BoxContainer? _wfCockpitDockActions;
    private readonly Dictionary<DockingPortState, int> _wfCockpitDockNumbers = new();
    private readonly List<UIBox2> _wfCockpitDockLabels = new();
    private readonly Dictionary<NetEntity, UIBox2> _wfCockpitDockCallouts = new();
    private readonly List<WFDockTarget> _wfPortTargets = new();
    private readonly List<DockingPortState> _wfPortStates = new();
    private NetEntity? _wfPressedPort;
    private NetEntity? _wfHoveredPort;

    /// <summary>Translates the dock drawing while retaining its real docking coordinates.</summary>
    private Vector2 WfCockpitDockPan => WfCockpitControls
        ? new Vector2(Offset.X, -Offset.Y) * MinimapScale
        : Vector2.Zero;

    /// <summary>Places existing port actions below the plot, retaining their docking checks and events.</summary>
    public Control WfCockpitDockActions(WFCockpitLease lease)
    {
        var actions = new BoxContainer
        {
            Name = "CockpitDockPortActions",
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SeparationOverride = 4,
        };
        _wfCockpitDockActions = actions;
        BuildDocks(GridEntity);
        lease.Remember(() =>
        {
            _wfCockpitDockActions = null;
            Offset = Vector2.Zero;
            BuildDocks(GridEntity);
        });
        return actions;
    }

    private void WfBeginCockpitDockDraw()
    {
        _wfCockpitDockLabels.Clear();
        _wfCockpitDockCallouts.Clear();
    }

    private bool WfBuildCockpitDockRow(DockingPortState dock, Button action, PanelContainer original)
    {
        if (_wfCockpitDockActions == null)
            return false;

        action.Parent!.RemoveChild(action);
        original.Dispose();
        var number = _wfCockpitDockNumbers.Count + 1;
        _wfCockpitDockNumbers.Add(dock, number);
        var name = dock.LabelName ?? dock.Name;
        var caption = new Label
        {
            Text = Loc.GetString("wf-cockpit-dock-port", ("number", number), ("name", name)),
            ClipText = true,
            HorizontalExpand = true,
            VerticalAlignment = VAlignment.Center,
        };
        action.Name = "CockpitDockAction";
        action.SetWidth = action.MinWidth = 110;
        action.HorizontalExpand = false;
        action.HorizontalAlignment = HAlignment.Stretch;
        action.AddStyleClass("WfCompact");
        var row = WFInstrumentTheme.Row(caption, action);
        // Labels and containers ignore the mouse by default, so the row takes it for the tooltip and hover.
        row.MouseFilter = MouseFilterMode.Pass;
        row.ToolTip = name;
        row.Visible = false;
        Action<GUIMouseHoverEventArgs> highlight = _ => HighlightedDock = dock.Entity;
        Action<GUIMouseHoverEventArgs> clear = _ => HighlightedDock = null;
        row.OnMouseEntered += highlight;
        row.OnMouseExited += clear;
        action.OnMouseEntered += highlight;
        action.OnMouseExited += clear;
        _wfCockpitDockActions.AddChild(row);
        _dockContainers[dock] = row;
        WFInstrumentTheme.Apply(row);
        return true;
    }

    private bool WfDrawCockpitDockMarker(DrawingHandleScreen handle, DockingPortState dock,
        bool visible, Matrix3x2 gridToView)
    {
        if (_wfCockpitDockActions == null)
            return false;

        _dockContainers[dock].Visible = visible;
        if (!visible)
            return true;

        var position = Vector2.Transform(dock.Coordinates.Position, gridToView);
        var text = _wfCockpitDockNumbers[dock].ToString();
        var scale = UIScale * 0.85f;
        var dimensions = handle.GetDimensions(Font, text, scale);
        var markerSize = Vector2.Max(new Vector2(20, 18) * UIScale, dimensions + new Vector2(6, 4) * UIScale);
        var marker = WFDockMarkerLayout.Place(position, markerSize, PixelSize, _wfCockpitDockLabels);
        if (marker is not { } bounds)
            return true;
        _wfCockpitDockLabels.Add(bounds);
        _wfCockpitDockCallouts[dock.Entity] = bounds;
        var color = HighlightedDock == dock.Entity ? WFInstrumentTheme.Cream : WFInstrumentTheme.Accent;
        handle.DrawLine(position, bounds.Center, color.WithAlpha(0.65f));
        handle.DrawRect(bounds, WFInstrumentTheme.Ink.WithAlpha(0.95f));
        handle.DrawRect(bounds, color, false);
        handle.DrawString(Font, bounds.Center - dimensions / 2, text, scale, color);
        return true;
    }

    /// <summary>Where one of this ship's ports is plotted, in pixels from the plot's corner; the plot stays north-up around the viewed port.</summary>
    private Vector2? WfPortPlotPosition(DockingPortState port)
    {
        if (_viewedState == null)
            return null;

        var offset = port.Coordinates.Position - _viewedState.Coordinates.Position;
        return MidPointVector + new Vector2(offset.X, -offset.Y) * MinimapScale - WfCockpitDockPan;
    }

    /// <summary>The unselected port of this ship whose callout or body is under a point, if any; another ship's callout over it blocks the click.</summary>
    private DockingPortState? WfPortAt(Vector2 pixel)
    {
        if (DockState == null || GridEntity is not { } grid ||
            !DockState.Docks.TryGetValue(EntManager.GetNetEntity(grid), out var ports))
            return null;

        foreach (var (entity, callout) in _wfCockpitDockCallouts)
        {
            if (!callout.Contains(pixel))
                continue;

            var own = false;
            foreach (var port in ports)
            {
                if (port.Entity != entity)
                    continue;

                own = true;
                break;
            }

            if (!own)
                return null;
        }

        _wfPortTargets.Clear();
        _wfPortStates.Clear();
        var reach = MathF.Max(0.6f * MinimapScale, WfPortReachMinimum * UIScale);
        foreach (var port in ports)
        {
            if (port.Entity == ViewedDock || WfPortPlotPosition(port) is not { } center)
                continue;

            UIBox2? callout = _wfCockpitDockCallouts.TryGetValue(port.Entity, out var box) ? box : null;
            _wfPortTargets.Add(new WFDockTarget(center, reach, callout));
            _wfPortStates.Add(port);
        }

        var hit = WFDockMarkerLayout.Pick(pixel, _wfPortTargets);
        return hit < 0 ? null : _wfPortStates[hit];
    }

    /// <summary>Takes a left press on one of this ship's ports; its release selects it.</summary>
    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);
        if (args.Handled || args.Function != EngineKeyFunctions.UIClick)
            return;

        _wfPressedPort = null;
        if (WfPortAt(args.RelativePixelPosition) is not { } port)
            return;

        _wfPressedPort = port.Entity;
        args.Handle();
    }

    /// <summary>Views the port the press began on when the release is still over it, as the port list does.</summary>
    protected override void KeyBindUp(GUIBoundKeyEventArgs args)
    {
        base.KeyBindUp(args);
        if (args.Handled || args.Function != EngineKeyFunctions.UIClick)
            return;

        var pressed = _wfPressedPort;
        _wfPressedPort = null;
        if (pressed == null || WfPortAt(args.RelativePixelPosition) is not { } port || port.Entity != pressed)
            return;

        UserInterfaceManager.ClickSound();
        SetViewedDock(port);
        args.Handle();
    }

    /// <summary>A press has no release once the plot is hidden, so it must not wait for one.</summary>
    protected override void VisibilityChanged(bool newVisible)
    {
        base.VisibilityChanged(newVisible);
        if (!newVisible)
            _wfPressedPort = null;
    }

    protected override void ExitedTree()
    {
        base.ExitedTree();
        _wfPressedPort = null;
    }

    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        base.MouseMove(args);
        WfHoverPort(WfPortAt(args.RelativePixelPosition)?.Entity);
    }

    protected override void MouseExited()
    {
        base.MouseExited();
        WfHoverPort(null);
    }

    /// <summary>Highlights the port under the pointer, as hovering its list entry does, and shows it can be clicked.</summary>
    private void WfHoverPort(NetEntity? hovered)
    {
        if (hovered == _wfHoveredPort)
            return;

        if (_wfHoveredPort != null && HighlightedDock == _wfHoveredPort)
            HighlightedDock = null;
        if (hovered != null)
            HighlightedDock = hovered;
        _wfHoveredPort = hovered;
        DefaultCursorShape = hovered != null ? CursorShape.Hand : CursorShape.Arrow;
    }
}
