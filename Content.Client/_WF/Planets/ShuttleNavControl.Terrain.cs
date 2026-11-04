using System.Numerics;
using Content.Shared._WF.Planets;
using Content.Shared._CE.ZLevels.Core.Components;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Map;

namespace Content.Client.Shuttles.UI;

public partial class ShuttleNavControl
{
    /// <summary>Whether the radar draws the planet map.</summary>
    public bool ShowPlanetTerrain { get; set; } = true;
    // Keep the server's grid-relative origin even while the user pans onto map coordinates.
    private EntityCoordinates? _wfRadarOrigin;
    private Button? _wfTerrainButton;

    private void AddWfTerrainButton(LayoutContainer parent)
    {
        var button = _wfTerrainButton = new Button
        {
            Text = Loc.GetString(ShowPlanetTerrain ? "wf-planet-map-on" : "wf-planet-map-off"),
            ToolTip = Loc.GetString("wf-planet-map-tooltip"),
            ToggleMode = true,
            Pressed = ShowPlanetTerrain,
        };
        button.OnToggled += args =>
        {
            ShowPlanetTerrain = args.Pressed;
            button.Text = Loc.GetString(args.Pressed ? "wf-planet-map-on" : "wf-planet-map-off");
        };
        parent.AddChild(button);
        LayoutContainer.SetAnchorAndMarginPreset(button, LayoutContainer.LayoutPreset.TopRight, margin: 10);
    }

    /// <summary>The orbit layer of the world a map belongs to, resolved through transit maps and the world's network.</summary>
    private EntityUid? GetWfPlanetOrbit(EntityUid? map)
    {
        if (map is { } own && EntManager.HasComponent<WFOrbitLayerComponent>(own))
            return own;

        // Transit maps sit between two permanent layers of the same world.
        if (EntManager.TryGetComponent<CEZTransitMapComponent>(map, out var transit))
            map = transit.LowerMap ?? transit.UpperMap;
        if (map is { } lower && EntManager.HasComponent<WFOrbitLayerComponent>(lower))
            return lower;
        if (!EntManager.TryGetComponent<WFPlanetLayerComponent>(map, out var layer) || layer.Network == null)
            return null;

        var query = EntManager.EntityQueryEnumerator<WFOrbitLayerComponent>();
        while (query.MoveNext(out var uid, out var candidate))
        {
            if (candidate.Network == layer.Network)
                return uid;
        }
        return null;
    }

    /// <summary>Draws the planet map of the world the radar is on, beneath radar contacts.</summary>
    private void DrawWfTerrain(DrawingHandleScreen handle, Matrix3x2 worldToView, EntityUid? map)
    {
        _wfMapDrewSites = false;
        if (ShowPlanetTerrain && GetWfPlanetOrbit(map) is { } orbit)
            DrawWfPlanetMap(handle, worldToView, orbit);
    }
}
