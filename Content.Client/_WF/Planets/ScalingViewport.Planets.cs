using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WF.Planets;
using Robust.Shared.Map.Components;

namespace Content.Client.Viewport;

public sealed partial class ScalingViewport
{
    private List<Entity<MapGridComponent>> _wfLayerGrids = new();

    /// <summary>
    /// Whether a planet's air layer below the observer has nothing in view, no grid and no loose entity, so its pass
    /// can be skipped: each pass is a whole render, and from orbit most of the layers are empty sky.
    /// </summary>
    private bool WfNothingInView(EntityUid map, float depth, float ownDepth)
    {
        if (!_entityManager.HasComponent<WFPlanetLayerComponent>(map)
            || _entityManager.HasComponent<CEZGroundLayerComponent>(map)
            || _entityManager.HasComponent<CEZCloudLayerComponent>(map)
            || !_entityManager.TryGetComponent(map, out MapComponent? mapComp))
            return false;

        var view = WfLevelView(map, depth, ownDepth);

        _wfLayerGrids.Clear();
        _mapManager.FindGridsIntersecting(mapComp.MapId, view, ref _wfLayerGrids);
        if (_wfLayerGrids.Count > 0)
            return false;

        return !_entityManager.System<EntityLookupSystem>().AnyEntitiesIntersecting(mapComp.MapId, view);
    }
}
