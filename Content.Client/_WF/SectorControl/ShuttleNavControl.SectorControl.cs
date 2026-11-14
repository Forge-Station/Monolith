using System.Numerics;
using Content.Client._WF.SectorControl;
using Robust.Client.Graphics;
using Robust.Shared.Map;

namespace Content.Client.Shuttles.UI;

public partial class ShuttleNavControl
{
    private readonly WFSectorOverlay _sectorBorderOverlay = new();

    /// <summary>Draws the edges of held territory under the radar contacts, so a pilot sees the line they are about to cross.</summary>
    private void DrawSectorBorders(DrawingHandleScreen handle, Matrix3x2 worldToView, MapId map)
    {
        var system = EntManager.System<WFSectorClientSystem>();
        if (!system.TryGetMap(map, out var view))
            return;

        _sectorBorderOverlay.DrawBorders(handle, view, worldToView);
    }
}
