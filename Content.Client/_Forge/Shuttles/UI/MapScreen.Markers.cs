using Content.Shared._Forge.Shuttles.Components;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Map;
using System.Numerics;

namespace Content.Client.Shuttles.UI;

public sealed partial class MapScreen
{
    public event Action<int>? OnRemoveMarker;

    private int _lastMapMarkerSignature = int.MinValue;

    private void ForgeUpdateMapMarkers()
    {
        if (_shuttleEntity is not { } shuttle ||
            !_entManager.TryGetComponent(shuttle, out ShuttleNavMarkerComponent? markers) ||
            markers.Markers.Count == 0)
        {
            if (_lastMapMarkerSignature != 0)
            {
                MapMarkerList.DisposeAllChildren();
                MapMarkerCountLabel.Text = Loc.GetString("shuttle-console-marker-count",
                    ("current", 0),
                    ("max", ShuttleNavMarkerComponent.MaxMarkers));
                _lastMapMarkerSignature = 0;
            }

            return;
        }

        var signature = markers.NextId;
        foreach (var marker in markers.Markers)
        {
            signature = HashCode.Combine(signature, marker.Id);
        }

        if (signature == _lastMapMarkerSignature)
            return;

        _lastMapMarkerSignature = signature;
        MapMarkerList.DisposeAllChildren();
        MapMarkerCountLabel.Text = Loc.GetString("shuttle-console-marker-count",
            ("current", markers.Markers.Count),
            ("max", ShuttleNavMarkerComponent.MaxMarkers));

        foreach (var marker in markers.Markers)
        {
            var row = new BoxContainer
            {
                Orientation = BoxContainer.LayoutOrientation.Horizontal,
                HorizontalExpand = true,
                SeparationOverride = 2,
            };

            var focus = new Button
            {
                Text = marker.Name,
                HorizontalExpand = true,
                ClipText = true,
                StyleClasses = { "ButtonSquare" },
                ToolTip = Loc.GetString("shuttle-console-marker-focus-tooltip"),
            };
            focus.OnPressed += _ => FocusMarker(marker);

            var autopilot = new Button
            {
                Text = Loc.GetString("shuttle-console-marker-autopilot"),
                MinWidth = 36f,
                StyleClasses = { "OpenBoth", "ButtonSquare" },
                ToolTip = Loc.GetString("shuttle-console-marker-autopilot-tooltip"),
            };
            var captured = marker;
            autopilot.OnPressed += _ => AutopilotToMarker(captured);

            var remove = new Button
            {
                Text = Loc.GetString("shuttle-console-marker-remove"),
                StyleClasses = { "ButtonSquare" },
            };
            var id = marker.Id;
            remove.OnPressed += _ => OnRemoveMarker?.Invoke(id);

            row.AddChild(focus);
            row.AddChild(autopilot);
            row.AddChild(remove);
            MapMarkerList.AddChild(row);
        }
    }

    private void FocusMarker(ShuttleNavMarker marker)
    {
        if (!TryGetMarkerCoordinates(marker, out var coords))
            return;

        SetMap(coords.MapId, coords.Position);
    }

    private void AutopilotToMarker(ShuttleNavMarker marker)
    {
        if (!TryGetMarkerCoordinates(marker, out var coords))
            return;

        if (_shuttleEntity is not { } shuttle)
            return;

        var shuttleMap = _xformSystem.GetMapCoordinates(shuttle);
        if (shuttleMap.MapId != coords.MapId)
            return;

        var delta = coords.Position - shuttleMap.Position;
        var angle = delta.LengthSquared() > 0.01f ? delta.ToWorldAngle() : Angle.Zero;
        RequestAutopilot?.Invoke(coords, angle);
    }

    private bool TryGetMarkerCoordinates(ShuttleNavMarker marker, out MapCoordinates coords)
    {
        if (marker.Kind == ShuttleNavMarkerKind.Entity &&
            marker.Target is { } net &&
            _entManager.TryGetEntity(net, out var target))
        {
            coords = _xformSystem.GetMapCoordinates(target.Value);
            if (coords.MapId != MapId.Nullspace)
                return true;
        }

        coords = new MapCoordinates(marker.Coordinates, new MapId(marker.MapId));
        return coords.MapId != MapId.Nullspace;
    }
}
