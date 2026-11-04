using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WF.Planets;

namespace Content.Shared._WF.WolfWeather;

public abstract partial class SharedWolfWeatherSystem
{
    [Dependency] private EntityQuery<CEZMapComponent> _zMapQuery = default!;
    [Dependency] private EntityQuery<CEZGroundLayerComponent> _groundLayerQuery = default!;
    [Dependency] private EntityQuery<WFPlanetLayerComponent> _planetLayerQuery = default!;
    [Dependency] private EntityQuery<WFOrbitLayerComponent> _orbitQuery = default!;

    /// <summary>
    /// Whether a map is open to the sky. Weather falls only on a surface: a map outside any z-network, a ground layer,
    /// or the top level of a network. Levels below one (caverns, cellars), the air above a planet and its orbit have none.
    /// </summary>
    public bool IsSurface(EntityUid map)
    {
        if (!_zMapQuery.TryComp(map, out var z))
            return true;

        if (_groundLayerQuery.HasComp(map) || z.Depth == 0 && _planetLayerQuery.HasComp(map))
            return true;

        return z.MapAbove == null && !_orbitQuery.HasComp(map);
    }

    /// <summary>Whether a grid's tiles are always under cover on a planet layer, as a vessel's are.</summary>
    public bool IsCoverGrid(EntityUid grid)
    {
        return _weather.WfPlanetExposure(grid) == false;
    }
}
