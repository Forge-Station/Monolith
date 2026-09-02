using Content.Shared._Forge.Shuttles.Components;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.Shuttles.UI;

public sealed partial class NavScreen
{
    public event Action<float, float>? OnAddCoordinateMarker;
    public event Action<int>? OnRemoveMarker;

    private int _lastMarkerSignature = int.MinValue;

    private void ForgeInitializeMarkers()
    {
        MarkerTrackButton.OnPressed += _ =>
        {
            if (!float.TryParse(MarkerXEdit.Text.Replace(',', '.'), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var x) ||
                !float.TryParse(MarkerYEdit.Text.Replace(',', '.'), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var y))
            {
                return;
            }

            OnAddCoordinateMarker?.Invoke(x, y);
        };
    }

    private void ForgeUpdateMarkers()
    {
        if (_shuttleEntity is not { } shuttle ||
            !_entManager.TryGetComponent(shuttle, out ShuttleNavMarkerComponent? markers) ||
            markers.Markers.Count == 0)
        {
            if (_lastMarkerSignature != 0)
            {
                MarkerList.DisposeAllChildren();
                MarkerCountLabel.Text = Loc.GetString("shuttle-console-marker-count",
                    ("current", 0),
                    ("max", ShuttleNavMarkerComponent.MaxMarkers));
                _lastMarkerSignature = 0;
            }

            return;
        }

        var signature = markers.NextId;
        foreach (var marker in markers.Markers)
        {
            signature = HashCode.Combine(signature, marker.Id);
        }

        if (signature == _lastMarkerSignature)
            return;

        _lastMarkerSignature = signature;
        MarkerList.DisposeAllChildren();
        MarkerCountLabel.Text = Loc.GetString("shuttle-console-marker-count",
            ("current", markers.Markers.Count),
            ("max", ShuttleNavMarkerComponent.MaxMarkers));

        foreach (var marker in markers.Markers)
        {
            var row = new BoxContainer
            {
                Orientation = BoxContainer.LayoutOrientation.Horizontal,
                HorizontalExpand = true,
                SeparationOverride = 4,
            };

            var label = new Label
            {
                Text = marker.Name,
                HorizontalExpand = true,
                ClipText = true,
                FontColorOverride = marker.Color,
            };

            var remove = new Button
            {
                Text = Loc.GetString("shuttle-console-marker-remove"),
                StyleClasses = { "ButtonSquare" },
            };

            var id = marker.Id;
            remove.OnPressed += _ => OnRemoveMarker?.Invoke(id);

            row.AddChild(label);
            row.AddChild(remove);
            MarkerList.AddChild(row);
        }
    }
}
