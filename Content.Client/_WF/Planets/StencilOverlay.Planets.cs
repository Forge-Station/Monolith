using System.Numerics;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._CE.ZLevels.Core.EntitySystems;
using Content.Shared._WF.Planets;
using Content.Shared.Light.Components;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Client.Overlays;

/// <summary>Rain on a planet's upper layers, seen through a gap in the floor.</summary>
public sealed partial class StencilOverlay
{
    [Dependency] private IPlayerManager _player = default!;

    private readonly HashSet<Vector2i> _wfDecided = new();
    private List<Entity<MapGridComponent>> _wfGrids = new();

    /// <summary>
    /// Where this layer has no floor, the rain shows only if the first floor below is out in the open: none down a
    /// stairwell onto an indoor floor or past a rooftop onto a covered street. Each lower floor is masked where the
    /// z-level renderer draws it, a storey's offset further per level; only for someone on foot, who sees lower layers
    /// full size.
    /// </summary>
    private void WfMaskCoveredBelow(DrawingHandleWorld handle, MapId mapId, Box2 worldAABB, Angle eyeRotation, Matrix3x2 invMatrix)
    {
        var mapUid = _map.GetMapOrInvalid(mapId);
        if (!_entManager.TryGetComponent(mapUid, out CEZMapComponent? zMap)
            || !_entManager.HasComponent<WFPlanetLayerComponent>(mapUid)
            || WFPlanetView.Shrink(_entManager, mapUid, _player.LocalEntity) != 1f)
            return;

        var zLevels = _entManager.System<CESharedZLevelsSystem>();
        if (!zLevels.TryMapDown((mapUid, zMap), out var below))
            return;

        _wfDecided.Clear();
        _wfGrids.Clear();
        _map.FindGridsIntersecting(mapId, worldAABB, ref _wfGrids);
        foreach (var grid in _wfGrids)
        {
            foreach (var tile in _map.GetTilesIntersecting(grid.Owner, grid, worldAABB))
            {
                _wfDecided.Add(WorldTile(grid, tile.GridIndices));
            }
        }

        var step = (-eyeRotation).ToWorldVec() * CESharedZLevelsSystem.ZLevelOffset;
        for (var level = 1; ; level++)
        {
            var shift = step * level;
            var bounds = worldAABB.Translated(-shift);
            _wfGrids.Clear();
            _map.FindGridsIntersecting(_entManager.GetComponent<MapComponent>(below.Owner).MapId, bounds, ref _wfGrids);
            foreach (var grid in _wfGrids)
            {
                _entManager.TryGetComponent(grid.Owner, out RoofComponent? roof);
                var matrix = _transform.GetWorldMatrix(grid);
                handle.SetTransform(Matrix3x2.Multiply(Matrix3x2.Multiply(matrix, Matrix3x2.CreateTranslation(shift)), invMatrix));
                foreach (var tile in _map.GetTilesIntersecting(grid.Owner, grid, bounds))
                {
                    if (!_wfDecided.Add(WorldTile(grid, tile.GridIndices)) || _weather.CanWeatherAffect(grid.Owner, grid, tile, roof))
                        continue;

                    handle.DrawRect(new Box2(tile.GridIndices * grid.Comp.TileSize, (tile.GridIndices + Vector2i.One) * grid.Comp.TileSize), Color.White);
                }
            }

            if (!zLevels.TryMapDown((below.Owner, below.Comp), out below))
                break;
        }
    }

    private Vector2i WorldTile(Entity<MapGridComponent> grid, Vector2i indices) =>
        Vector2.Transform(indices + new Vector2(0.5f), _transform.GetWorldMatrix(grid)).Floored();
}
