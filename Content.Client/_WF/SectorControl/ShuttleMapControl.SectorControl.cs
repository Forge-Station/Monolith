using System.Numerics;
using Content.Client._WF.SectorControl;
using Robust.Client.Graphics;

namespace Content.Client.Shuttles.UI;

public sealed partial class ShuttleMapControl
{
    private readonly WFSectorOverlay _sectorOverlay = new();

    /// <summary>Draws the viewed map's faction territory: fills, borders, contested cells, callsigns and the legend.</summary>
    private void DrawSectorTerritory(DrawingHandleScreen handle, Matrix3x2 matty)
    {
        var system = EntManager.System<WFSectorClientSystem>();
        if (!system.TryGetMap(ViewingMap, out var view))
            return;

        // Map space to pixels: the inverse offset, then the same Y flip and scale ScalePosition applies.
        var worldToView = matty * Matrix3x2.CreateScale(new Vector2(MinimapScale, -MinimapScale)) * Matrix3x2.CreateTranslation(MidPointVector);
        var bounds = new UIBox2(0f, 0f, PixelSize.X, PixelSize.Y);
        _sectorOverlay.DrawFills(handle, view, worldToView);
        _sectorOverlay.DrawBorders(handle, view, worldToView);
        _sectorOverlay.DrawContests(handle, system, view, worldToView, Font, UIScale, bounds);
        _sectorOverlay.DrawRetakes(handle, system, view, worldToView, Font, UIScale, bounds);
        _sectorOverlay.DrawClearing(handle, system, view, worldToView, Font, UIScale, bounds);
        _sectorOverlay.DrawCallsigns(handle, view, worldToView, Font, UIScale, MinimapScale, bounds);
        _sectorOverlay.DrawLegend(handle, system, Font, UIScale);
    }
}
