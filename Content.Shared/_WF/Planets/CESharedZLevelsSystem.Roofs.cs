using Content.Shared._WF.PlanetCities;
using Content.Shared._WF.Planets;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Robust.Shared.Map.Components;

namespace Content.Shared._CE.ZLevels.Core.EntitySystems;

public abstract partial class CESharedZLevelsSystem
{
    [Dependency] private SharedRoofSystem _wfRoof = default!;

    /// <summary>
    /// Whether a tile one layer down is roofed over, so a body above it stands on the roof instead of dropping through:
    /// a roofed tile on a planet layer, or any tile of a vessel there, which is always under cover.
    /// </summary>
    private bool WfRoofedBelow(EntityUid grid, MapGridComponent gridComp, Vector2i tile)
    {
        var map = Transform(grid).MapUid;
        if (!HasComp<WFPlanetLayerComponent>(map))
            return false;

        if (grid != map && !HasComp<WFPlanetCityGridComponent>(grid))
            return true;

        return TryComp<RoofComponent>(grid, out var roof) && _wfRoof.IsRooved((grid, gridComp, roof), tile);
    }
}
