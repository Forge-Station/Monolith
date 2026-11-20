using Content.Shared._WF.PlanetCities;
using Content.Shared._WF.Planets;

namespace Content.Shared.Weather;

/// <summary>Weather on a planet's layers: roofs decide what is rained on, not the tile, and a vessel is always under cover.</summary>
public abstract partial class SharedWeatherSystem
{
    /// <summary>
    /// True for the ground of a planet layer and a fixed city's levels, which go by their roofs alone; false for any other
    /// grid on a planet layer, a vessel, which is never rained on inside; null off a planet, for the upstream rule.
    /// </summary>
    public bool? WfPlanetExposure(EntityUid grid)
    {
        if (!TryComp(grid, out TransformComponent? xform) || xform.MapUid is not { } map || !HasComp<WFPlanetLayerComponent>(map))
            return null;

        return grid == map || HasComp<WFPlanetCityGridComponent>(grid);
    }
}
