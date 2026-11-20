using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._CE.ZLevels.Core.EntitySystems;
using Content.Shared._WF.MappingMode;
using Content.Shared._WF.PlanetCities;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Robust.Shared.Map.Components;

namespace Content.Shared._WF.Planets;

/// <summary>How the z-level renderer scales a planet's layers. Monolith draws lower levels full size; a planet keeps the perspective.</summary>
public static class WFPlanetView
{
    /// <summary>Each planet layer below the observer is drawn this much smaller, as every z-level was before Monolith set the factor to 1.</summary>
    public const float LayerShrink = 0.85f;

    /// <summary>The smallest a lower layer is drawn; past it the ground from orbit would fill the view with five screens of terrain.</summary>
    public const float MinLayerScale = 0.6f;

    /// <summary>The eye scale of a pass <paramref name="depth"/> levels from the observer, negative below, for a layer of this shrink.</summary>
    public static float LayerScale(float shrink, float depth)
    {
        var scale = MathF.Pow(shrink, -depth);
        return shrink < 1f ? MathF.Max(scale, MinLayerScale) : scale;
    }

    /// <summary>
    /// The per-level scale for a map: a planet layer's or a mapping level with Depth perspective on, or the transit map
    /// between two, else the renderer's own. A <paramref name="viewer"/> inside a building, under a roof on a planet's
    /// ground or a city's level, sees the lower layers full size, as storeys rather than the ground far below; anywhere
    /// else, a ship, the open air or the open ground, keeps the perspective.
    /// </summary>
    public static float Shrink(IEntityManager entMan, EntityUid? map, EntityUid? viewer = null)
    {
        if (map is not { } uid)
            return CESharedZLevelsSystem.ZLevelViewShrink;

        if (viewer is { } who && Indoors(entMan, who))
            return 1f;

        if (entMan.TryGetComponent(uid, out CEZTransitMapComponent? transit))
            uid = transit.LowerMap ?? transit.UpperMap ?? uid;

        return entMan.HasComponent<WFPlanetLayerComponent>(uid) || entMan.HasComponent<WFMappingPerspectiveComponent>(uid)
            ? LayerShrink
            : CESharedZLevelsSystem.ZLevelViewShrink;
    }

    /// <summary>Whether a viewer stands on a roofed tile of a planet layer's ground or of a city's level.</summary>
    public static bool Indoors(IEntityManager entMan, EntityUid viewer)
    {
        if (!entMan.TryGetComponent(viewer, out TransformComponent? xform)
            || xform.GridUid is not { } grid
            || !entMan.HasComponent<WFPlanetLayerComponent>(xform.MapUid)
            || grid != xform.MapUid && !entMan.HasComponent<WFPlanetCityGridComponent>(grid)
            || !entMan.TryGetComponent(grid, out MapGridComponent? gridComp)
            || !entMan.TryGetComponent(grid, out RoofComponent? roof))
            return false;

        var tile = entMan.System<SharedMapSystem>().CoordinatesToTile(grid, gridComp, xform.Coordinates);
        return entMan.System<SharedRoofSystem>().IsRooved((grid, gridComp, roof), tile);
    }
}
