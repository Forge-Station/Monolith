using System.Numerics;
using Content.Shared._Forge.Shuttles.Components;
using Robust.Client.Graphics;
using Robust.Shared.Map;

namespace Content.Client.Shuttles.UI;

public sealed partial class ShuttleMapControl
{
    /// <summary>Plots the helm's saved markers, including tracked targets and off-screen positions.</summary>
    private void DrawForgeMapMarkers(DrawingHandleScreen handle, Matrix3x2 worldToView)
    {
        if (!EntManager.TryGetComponent<ShuttleNavMarkerComponent>(_shuttleEntity, out var markers))
            return;

        var size = 6f * UIScale;
        var inset = new Vector2(size + 2f * UIScale);
        foreach (var marker in markers.Markers)
        {
            var coordinates = new MapCoordinates(marker.Coordinates, new MapId(marker.MapId));
            if (marker.Kind == ShuttleNavMarkerKind.Entity && marker.Target is { } net &&
                EntManager.TryGetEntity(net, out var target))
            {
                var tracked = _xformSystem.GetMapCoordinates(target.Value);
                if (tracked.MapId != MapId.Nullspace)
                    coordinates = tracked;
            }

            if (coordinates.MapId != ViewingMap)
                continue;

            var position = Vector2.Clamp(Vector2.Transform(coordinates.Position, worldToView),
                inset, Vector2.Max(inset, new Vector2(PixelWidth, PixelHeight) - inset));
            var top = position - new Vector2(0, size);
            var right = position + new Vector2(size, 0);
            var bottom = position + new Vector2(0, size);
            var left = position - new Vector2(size, 0);
            handle.DrawLine(top, right, marker.Color);
            handle.DrawLine(right, bottom, marker.Color);
            handle.DrawLine(bottom, left, marker.Color);
            handle.DrawLine(left, top, marker.Color);

            var dimensions = handle.GetDimensions(Font, marker.Name, UIScale * 0.8f);
            var labelX = position.X + size + 2f * UIScale;
            if (labelX + dimensions.X > PixelWidth)
                labelX = position.X - size - 2f * UIScale - dimensions.X;
            handle.DrawString(Font, new Vector2(Math.Max(0, labelX), position.Y - dimensions.Y / 2f),
                marker.Name, UIScale * 0.8f, marker.Color);
        }
    }
}
