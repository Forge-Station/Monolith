using Robust.Shared.Map;

namespace Content.Shared._WF.Planets;

/// <summary>
/// Natural terrain built over on a planet layer, restored when the build is cut away instead of leaving a hole.
/// </summary>
[RegisterComponent, UnsavedComponent]
public sealed partial class WFPlanetBuiltTilesComponent : Component
{
    [ViewVariables]
    public Dictionary<Vector2i, Tile> Underlay = new();
}
