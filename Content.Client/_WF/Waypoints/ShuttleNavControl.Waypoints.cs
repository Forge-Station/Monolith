using System.Numerics;
using Content.Client._WF.CombatConsole;
using Content.Client._WF.Lattice;
using Content.Client._WF.Waypoints;
using Content.Shared._Mono.Shuttles;
using Content.Shared._WF.Waypoints;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;
using Robust.Shared.Map;

namespace Content.Client.Shuttles.UI;

public partial class ShuttleNavControl
{
    private const float WfWaypointMarkerSize = 6f;
    private const float WfOverlayEdge = 8f;
    /// <summary>Clears the terrain toggle above the waypoint key.</summary>
    private const float WfWaypointKeyTop = 44f;
    private const float WfWaypointPanelTop = 80f;
    /// <summary>Clears the cockpit's contact toggles in the same corner.</summary>
    private const float WfCockpitReadoutClearance = 44f;

    private static readonly Color WfWaypointColor = Color.FromHex("#6EE7FF");
    private static readonly Color WfReadoutColor = Color.FromHex("#C8D2C8");
    private static readonly Color WfReadoutShadow = Color.Black.WithAlpha(0.8f);
    private static readonly IReadOnlyList<WFWaypoint> WfNoWaypoints = new List<WFWaypoint>();

    private readonly Vector2[] _wfWaypointVertices = new Vector2[5];
    private Enum? _wfWaypointKey;
    private bool _wfWaypointCanFly;
    private bool _wfWaypointPicking;
    private bool _wfSwallowRelease;
    private Button? _wfWaypointButton;
    private WFWaypointPanel? _wfWaypointPanel;

    /// <summary>Gives this radar the waypoint panel, talking through the given console window; only a helm can fly to them.</summary>
    public void WfEnableWaypoints(Enum uiKey, bool canFly)
    {
        _wfWaypointKey = uiKey;
        _wfWaypointCanFly = canFly;
    }

    /// <summary>The world position the cursor is over, or null with no cursor on the radar.</summary>
    public Vector2? WfCursorWorldPosition(Vector2 relativePosition)
    {
        var coordinates = GetMouseEntityCoordinates(relativePosition);
        return coordinates.IsValid(EntManager) ? _transform.ToMapCoordinates(coordinates).Position : null;
    }

    /// <summary>The world position of the console this radar belongs to, else of the point it is centred on.</summary>
    public Vector2? WfScannerWorldPosition()
    {
        if (EntManager.TryGetComponent<TransformComponent>(_consoleEntity, out var console))
            return _transform.GetWorldPosition(console);
        return _coordinates is { } centre && centre.IsValid(EntManager)
            ? _transform.ToMapCoordinates(centre).Position
            : null;
    }

    private void AddWfWaypointControls(LayoutContainer parent)
    {
        if (_wfWaypointKey == null)
            return;

        var button = _wfWaypointButton = new Button
        {
            Name = "WfWaypointKey",
            Text = Loc.GetString("wf-waypoint-key"),
            ToolTip = Loc.GetString("wf-waypoint-key-tooltip"),
            ToggleMode = true,
        };
        var panel = _wfWaypointPanel = new WFWaypointPanel { Visible = false };
        button.OnToggled += args =>
        {
            panel.Visible = args.Pressed;
            if (!args.Pressed)
                panel.CancelPick();
        };
        panel.PickToggled += picking => _wfWaypointPicking = picking;
        panel.AddRequested += (name, position) => WfSendWaypointMessage(new WFWaypointAddMessage(name, position));
        panel.RemoveRequested += id => WfSendWaypointMessage(new WFWaypointRemoveMessage(id));
        panel.FlyRequested += WfFlyTo;

        parent.AddChild(button);
        parent.AddChild(panel);
        WfPlaceWaypointControls(parent);
    }

    /// <summary>Puts the waypoint controls on the given plot, below the terrain toggle at its top right.</summary>
    private void WfPlaceWaypointControls(LayoutContainer parent)
    {
        if (_wfWaypointButton == null || _wfWaypointPanel == null)
            return;

        Place(_wfWaypointButton, WfWaypointKeyTop);
        Place(_wfWaypointPanel, WfWaypointPanelTop);

        void Place(Control control, float top)
        {
            if (control.Parent != parent)
                parent.AddChild(WFInstrumentTheme.Detach(control));
            control.SetPositionLast();
            LayoutContainer.SetAnchorPreset(control, LayoutContainer.LayoutPreset.TopRight);
            LayoutContainer.SetGrowHorizontal(control, LayoutContainer.GrowDirection.Begin);
            LayoutContainer.SetGrowVertical(control, LayoutContainer.GrowDirection.End);
            LayoutContainer.SetMarginLeft(control, -WfOverlayEdge);
            LayoutContainer.SetMarginRight(control, -WfOverlayEdge);
            LayoutContainer.SetMarginTop(control, top);
            LayoutContainer.SetMarginBottom(control, top);
        }
    }

    /// <summary>Styles the waypoint controls as helm instruments and moves them with the plot.</summary>
    private void WfAttachWaypointControls(LayoutContainer parent)
    {
        if (_wfWaypointButton == null || _wfWaypointPanel == null)
            return;

        _wfWaypointButton.AddStyleClass("WfCompact");
        _wfWaypointButton.MinSize = _wfWaypointButton.MaxSize = new Vector2(120, 32);
        WFInstrumentTheme.Switch(_wfWaypointButton);
        _wfWaypointPanel.UseInstrumentTheme();
        WfPlaceWaypointControls(parent);
    }

    private void WfDisposeWaypointControls()
    {
        _wfWaypointButton?.Dispose();
        _wfWaypointPanel?.Dispose();
    }

    /// <summary>Follows the grid's waypoints; a console with no grid under it has nowhere to keep them.</summary>
    private void WfUpdateWaypoints()
    {
        if (_wfWaypointButton == null || _wfWaypointPanel == null)
            return;

        var hasGrid = EntManager.TryGetComponent<TransformComponent>(_consoleEntity, out var console) && console.GridUid != null;
        _wfWaypointButton.Visible = hasGrid;
        if (!hasGrid)
        {
            _wfWaypointButton.Pressed = false;
            _wfWaypointPanel.Visible = false;
            _wfWaypointPanel.CancelPick();
            return;
        }

        var waypoints = EntManager.TryGetComponent<WFWaypointsComponent>(console!.GridUid, out var stored)
            ? stored.Waypoints
            : WfNoWaypoints;
        NetEntity? map = console.MapUid is { } mapUid ? EntManager.GetNetEntity(mapUid) : null;
        _wfWaypointPanel.UpdateWaypoints(waypoints, map, _wfWaypointCanFly);
    }

    private void WfSendWaypointMessage(BoundUserInterfaceMessage message)
    {
        if (_consoleEntity is { } console && _wfWaypointKey != null)
            EntManager.System<SharedUserInterfaceSystem>().ClientSendUiMessage(console, _wfWaypointKey, message);
    }

    /// <summary>Engages the helm's autopilot, holding the ship's present heading on arrival.</summary>
    private void WfFlyTo(Vector2 position)
    {
        if (!EntManager.TryGetComponent<TransformComponent>(_consoleEntity, out var console) || console.MapID == MapId.Nullspace)
            return;

        WfSendWaypointMessage(new ShuttleConsoleAutopilotPositionMessage
        {
            Coordinates = new MapCoordinates(position, console.MapID),
            Angle = _transform.GetWorldRotation(console.GridUid ?? console.Owner),
        });
    }

    /// <summary>While picking, a left click on the plot fills the waypoint form and reaches nothing else.</summary>
    private bool WfTryPickWaypoint(GUIBoundKeyEventArgs args)
    {
        if (!_wfWaypointPicking || args.Function != EngineKeyFunctions.UIClick)
            return false;

        if (WfCursorWorldPosition(args.RelativePosition) is { } position)
            _wfWaypointPanel?.SetPicked(position);
        _wfSwallowRelease = true;
        args.Handle();
        return true;
    }

    /// <summary>Consumes the release of the click a pick took.</summary>
    private bool WfTrySwallowPickRelease(GUIBoundKeyEventArgs args)
    {
        if (!_wfSwallowRelease || args.Function != EngineKeyFunctions.UIClick)
            return false;

        _wfSwallowRelease = false;
        args.Handle();
        return true;
    }

    /// <summary>Draws the grid's waypoints on this map, pinned to the plot's edge when out of view.</summary>
    private void DrawWfWaypoints(DrawingHandleScreen handle, Matrix3x2 worldToView, EntityUid? map)
    {
        if (map is not { } mapUid ||
            !EntManager.TryGetComponent<TransformComponent>(_consoleEntity, out var console) ||
            !EntManager.TryGetComponent<WFWaypointsComponent>(console.GridUid, out var stored))
            return;

        var mapNet = EntManager.GetNetEntity(mapUid);
        var inset = (WfWaypointMarkerSize + 2f) * UIScale;
        foreach (var waypoint in stored.Waypoints)
        {
            if (waypoint.Map != mapNet)
                continue;

            var view = Vector2.Transform(waypoint.Position, worldToView);
            view = Vector2.Clamp(view, new Vector2(inset), Vector2.Max(new Vector2(inset), new Vector2(PixelSize.X, PixelSize.Y) - new Vector2(inset)));
            var size = WfWaypointMarkerSize * UIScale;
            _wfWaypointVertices[0] = view + new Vector2(0, -size);
            _wfWaypointVertices[1] = view + new Vector2(size, 0);
            _wfWaypointVertices[2] = view + new Vector2(0, size);
            _wfWaypointVertices[3] = view + new Vector2(-size, 0);
            _wfWaypointVertices[4] = _wfWaypointVertices[0];
            handle.DrawPrimitives(DrawPrimitiveTopology.LineStrip, _wfWaypointVertices, WfWaypointColor);

            var dimensions = handle.GetDimensions(Font, waypoint.Name, UIScale * 0.8f);
            var labelX = view.X + size + 2f * UIScale + dimensions.X > PixelSize.X
                ? view.X - size - 2f * UIScale - dimensions.X
                : view.X + size + 2f * UIScale;
            handle.DrawString(Font, new Vector2(labelX, view.Y - dimensions.Y / 2f), waypoint.Name, UIScale * 0.8f, WfWaypointColor);
        }
    }

    /// <summary>Writes the cursor's and the scanner's world coordinates in the plot's bottom right corner.</summary>
    private void DrawWfCoordinateReadout(DrawingHandleScreen handle)
    {
        var cursor = _isMouseInside
            ? WfCursorWorldPosition(UserInterfaceManager.MousePositionScaled.Position - GlobalPosition)
            : null;
        var scanner = WfScannerWorldPosition();
        const float scale = 0.8f;
        var bottom = PixelSize.Y - (WfOverlayEdge + (WfCockpitControls ? WfCockpitReadoutClearance : 0f)) * UIScale;
        var right = PixelSize.X - WfOverlayEdge * UIScale;

        // Out past the Lattice's hold the numbers garble, half as badly as names.
        var lattice = EntManager.System<WFLatticeViewSystem>();
        bottom = DrawLine(scanner is { } scan
            ? lattice.GarbleForViewer(Loc.GetString("wf-radar-scanner-coordinates", ("x", $"{scan.X:0.0}"), ("y", $"{scan.Y:0.0}")), 2, 0.5f)
            : Loc.GetString("wf-radar-scanner-unknown"), bottom);
        DrawLine(cursor is { } point
            ? lattice.GarbleForViewer(Loc.GetString("wf-radar-cursor-coordinates", ("x", $"{point.X:0.0}"), ("y", $"{point.Y:0.0}")), 3, 0.5f)
            : Loc.GetString("wf-radar-cursor-unknown"), bottom);

        float DrawLine(string text, float lineBottom)
        {
            var dimensions = handle.GetDimensions(Font, text, UIScale * scale);
            var position = new Vector2(right - dimensions.X, lineBottom - dimensions.Y);
            handle.DrawString(Font, position + new Vector2(1f, 1f), text, UIScale * scale, WfReadoutShadow);
            handle.DrawString(Font, position, text, UIScale * scale, WfReadoutColor);
            return position.Y;
        }
    }
}
