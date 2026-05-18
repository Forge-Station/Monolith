using Robust.Shared.GameStates;

namespace Content.Shared._Forge.Shuttles.Components;

/// <summary>
/// Networked visuals for a POI capture zone. Drawn on shuttle radar
/// (see ShuttleNavControl.DrawZones) as a translucent disc in the company color.
/// Place this on a marker entity at the desired zone center on the POI map,
/// or directly on the grid. The capture system updates color + visibility
/// when ownership changes; <see cref="Radius"/> is set in YAML or filled from
/// the <c>forge.poi_capture.zone_radius_tiles</c> CVar at map init.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class PoiCaptureZoneVisualsComponent : Component
{
    /// <summary>
    /// Zone radius in tiles. World radius is the same numeric value (tiles == meters).
    /// Zero means "use the global CVar default" — the server fills it on map init.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Radius;

    /// <summary>
    /// Fill color (alpha is applied client-side). Updated when ownership changes.
    /// </summary>
    [DataField, AutoNetworkedField]
    public Color ZoneColor = Color.Gray;

    /// <summary>
    /// True when the POI is currently owned by some company. Neutral zones are not drawn.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Visible;
}
