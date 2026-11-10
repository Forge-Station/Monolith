using System.Numerics;
using Content.Shared._WF.Vehicles.Grid.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using PhysicsTransform = Robust.Shared.Physics.Transform;

namespace Content.Shared._WF.Vehicles.Grid;

/// <summary>Floor checks for the grid mover: the tiles under the vehicle, and the hand-over between grids.</summary>
public sealed partial class GridVehicleMoverSystem : EntitySystem
{
    private const float OverhangTolerance = 0.0001f;

    /// <summary>Whether the probes running now are the drive itself, the only ones that may cross an allowed empty tile.</summary>
    private bool _driveProbe;

    /// <summary>Whether every tile under a footprint is floor, or is an empty one something allows.</summary>
    private bool IsFootprintSupported(
        EntityUid uid,
        GridVehicleMoverComponent mover,
        FixturesComponent fixtures,
        EntityUid grid,
        MapGridComponent gridComp,
        PhysicsTransform gridTransform,
        MapId mapId,
        Box2 footprint,
        HullPose hull,
        out Vector2i emptyTile)
    {
        emptyTile = default;

        var tileSize = gridComp.TileSize;
        if (!GridVehicleGeometry.TryGetTileRange(footprint, tileSize, out var min, out var max))
            return true;

        Box2? current = null;

        for (var x = min.X; x <= max.X; x++)
        {
            for (var y = min.Y; y <= max.Y; y++)
            {
                var tile = new Vector2i(x, y);
                if (!_map.GetTileRef(grid, gridComp, tile).Tile.IsEmpty)
                    continue;

                // A tile under the upright box that the turned hull does not reach is not under the vehicle.
                if (!hull.Overlaps(GridVehicleGeometry.GetTileBox(tile, tileSize), movement: true))
                    continue;

                if (IsCoveredByOtherGrid(grid, mapId, gridTransform, footprint, tile, tileSize))
                    continue;

                current ??= GetCurrentFootprint(uid, mover, fixtures, grid);
                if (GridVehicleGeometry.GetOverlapArea(footprint, tile, tileSize) <=
                    GridVehicleGeometry.GetOverlapArea(current.Value, tile, tileSize) + OverhangTolerance)
                {
                    continue;
                }

                if (_driveProbe)
                {
                    var ev = new VehicleOpenTileAttemptEvent(grid, tile);
                    RaiseLocalEvent(uid, ref ev);
                    if (ev.Allow)
                        continue;
                }

                emptyTile = tile;
                return false;
            }
        }

        return true;
    }

    /// <summary>The movement footprint where the mover stands now, in the grid's frame; empty when the vehicle has no hard fixture.</summary>
    private Box2 GetCurrentFootprint(EntityUid uid, GridVehicleMoverComponent mover, FixturesComponent fixtures, EntityUid grid)
    {
        var pose = new PhysicsTransform(mover.Position, GetCollisionLocalRotation(uid, grid, null));
        return TryGetFixtureAabb(fixtures, pose, out var aabb)
            ? GetMovementAabb(aabb, mover)
            : default;
    }

    /// <summary>Whether some other grid has floor under the middle of the part of a tile the footprint covers.</summary>
    private bool IsCoveredByOtherGrid(
        EntityUid grid,
        MapId mapId,
        PhysicsTransform gridTransform,
        Box2 footprint,
        Vector2i tile,
        float tileSize)
    {
        var overlap = footprint.Intersect(GridVehicleGeometry.GetTileBox(tile, tileSize));
        var sample = PhysicsTransform.Mul(gridTransform, overlap.Center);

        return _map.TryFindGridAt(mapId, sample, out var other, out var otherComp) &&
               other != grid &&
               IsFloorAt(other, otherComp, sample);
    }

    /// <summary>Whether a grid has a tile under a world point; <c>TryFindGridAt</c> also answers with a map that is itself a grid where that grid has none.</summary>
    private bool IsFloorAt(EntityUid grid, MapGridComponent gridComp, Vector2 worldPosition)
    {
        var local = Vector2.Transform(worldPosition, _transform.GetInvWorldMatrix(grid));
        var size = gridComp.TileSize;
        var tile = new Vector2i((int) MathF.Floor(local.X / size), (int) MathF.Floor(local.Y / size));
        return !_map.GetTileRef(grid, gridComp, tile).Tile.IsEmpty;
    }

    /// <summary>Hands the vehicle to the grid whose floor lies under its origin; over no floor it stays with its parent.</summary>
    private void CheckGridTraversal(EntityUid uid, TransformComponent xform)
    {
        if (_timing.ApplyingState || xform.MapUid is not { } map)
            return;

        // Anything else, such as a container, is not for the mover to take it out of.
        if (xform.ParentUid != map && xform.ParentUid != xform.GridUid)
            return;

        var position = _transform.GetWorldPosition(xform);

        if (_map.TryFindGridAt(xform.MapID, position, out var found, out var foundComp))
        {
            if (found == xform.GridUid)
                return;

            if (IsFloorAt(found, foundComp, position) && !TerminatingOrDeleted(found))
            {
                _transform.SetParent(uid, xform, found);
                return;
            }
        }

        if (xform.GridUid is not { } current || current == map || !gridQ.TryComp(current, out var currentComp))
            return;

        var local = Vector2.Transform(position, _transform.GetInvWorldMatrix(current));
        var ev = new VehicleOpenTileAttemptEvent(
            current,
            _map.TileIndicesFor(current, currentComp, new EntityCoordinates(current, local)),
            HandOver: true);
        RaiseLocalEvent(uid, ref ev);

        if (ev.Allow)
            _transform.SetParent(uid, xform, map);
    }
}
