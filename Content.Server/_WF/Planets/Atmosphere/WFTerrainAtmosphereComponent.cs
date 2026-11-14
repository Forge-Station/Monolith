namespace Content.Server._WF.Planets.Atmosphere;

/// <summary>
/// A planet layer whose ground is its own grid. Its bare ground shares the map's air, like a tile off the grid;
/// atmos tracks and simulates only what is built on it.
/// </summary>
[RegisterComponent, UnsavedComponent]
public sealed partial class WFTerrainAtmosphereComponent : Component
{
    /// <summary>Tile types that are bare ground: what the layer's biome lays and what digging turns that into.</summary>
    public readonly HashSet<int> OpenTiles = new();
}
