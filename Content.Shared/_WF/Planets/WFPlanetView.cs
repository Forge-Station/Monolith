using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._CE.ZLevels.Core.EntitySystems;

namespace Content.Shared._WF.Planets;

/// <summary>How the z-level renderer scales a planet's layers. Monolith draws lower levels full size; a planet keeps the perspective.</summary>
public static class WFPlanetView
{
    /// <summary>Each planet layer below the observer is drawn this much smaller, as every z-level was before Monolith set the factor to 1.</summary>
    public const float LayerShrink = 0.85f;

    /// <summary>The per-level scale for a map: a planet layer's, or the transit map between two, else the renderer's own.</summary>
    public static float Shrink(IEntityManager entMan, EntityUid? map)
    {
        if (map is not { } uid)
            return CESharedZLevelsSystem.ZLevelViewShrink;

        if (entMan.TryGetComponent(uid, out CEZTransitMapComponent? transit))
            uid = transit.LowerMap ?? transit.UpperMap ?? uid;

        return entMan.HasComponent<WFPlanetLayerComponent>(uid) ? LayerShrink : CESharedZLevelsSystem.ZLevelViewShrink;
    }
}
