using System.Linq;
using Content.Server._NF.GameRule;
using Content.Server.Warps;
using Content.Shared._WF.Contracts;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Content.Shared.Station.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._WF.Contracts;

/// <summary>Finding contractors' stations, the board, and where cargo is handed out.</summary>
public sealed partial class WFContractSystem
{
    /// <summary>Each contractor in the round and its home station: the first of its maps that exists.</summary>
    public Dictionary<string, EntityUid> ContractorHomes()
    {
        var homes = new Dictionary<string, EntityUid>();
        foreach (var contractor in _proto.EnumeratePrototypes<WFContractorPrototype>())
        {
            foreach (var map in contractor.Stations)
            {
                if (StationOf(map) is not { } station)
                    continue;

                homes[contractor.ID] = station;
                break;
            }
        }

        return homes;
    }

    /// <summary>The round's station for a game map, matched by name.</summary>
    public EntityUid? StationOf(string map)
    {
        if (!_proto.TryIndex<Content.Shared.Maps.GameMapPrototype>(map, out var gameMap))
            return null;

        var names = new HashSet<string> { gameMap.MapName };
        if (_proto.TryIndex<PointOfInterestPrototype>(map, out var poi))
            names.Add(poi.Name);

        var query = EntityQueryEnumerator<StationDataComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            if (names.Contains(MetaData(uid).EntityName))
                return uid;
        }

        return null;
    }

    /// <summary>The station the public board at Caelestinus stands on, where board pickups and drop-offs are; no board, no offers.</summary>
    public EntityUid? BoardStation()
    {
        var query = EntityQueryEnumerator<WFContractBoardComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var board, out _))
        {
            if (board.Board == WFContractBoards.Central && _station.GetOwningStation(uid) is { } station)
                return station;
        }

        return null;
    }

    /// <summary>What makes a tile no place for cargo: walls, tables, machines, racks and crates.</summary>
    private const CollisionGroup CargoBlockers = CollisionGroup.Impassable | CollisionGroup.MidImpassable | CollisionGroup.LowImpassable;

    /// <summary>How far, in tiles walked, cargo may be placed from where it was meant to go.</summary>
    private const int DropSearchTiles = 400;

    /// <summary>Where a board hands out cargo: its nearest drop, else the floor in front of it.</summary>
    private EntityCoordinates? BoardDrop(EntityUid board)
    {
        if (TerminatingOrDeleted(board) || Transform(board) is not { GridUid: { } grid } xform
            || !TryComp<MapGridComponent>(grid, out var gridComp))
            return null;

        if (NearestDrop(grid, xform.Coordinates, 8f) is { } drop)
            return ClearTileNear(grid, gridComp, drop);

        var front = xform.LocalRotation.RotateVec(new System.Numerics.Vector2(0f, -1f));
        return ClearTileNear(grid, gridComp, xform.Coordinates, front, skipStart: true);
    }

    /// <summary>Where a station hands out cargo: its central drop, else its warp point, else its centre.</summary>
    private EntityCoordinates? Anchor(EntityUid station)
    {
        EntityCoordinates? start = null;
        var centre = _station.GetLargestGrid(station) is { } main && TryComp<MapGridComponent>(main, out var mainGrid)
            ? _transform.GetWorldMatrix(main).TransformBox(mainGrid.LocalAABB).Center
            : System.Numerics.Vector2.Zero;
        var best = float.MaxValue;
        var drops = EntityQueryEnumerator<WFContractDropComponent, TransformComponent>();
        while (drops.MoveNext(out var uid, out _, out var xform))
        {
            if (_station.GetOwningStation(uid, xform) != station)
                continue;

            var distance = (_transform.GetWorldPosition(xform) - centre).LengthSquared();
            if (distance >= best)
                continue;

            best = distance;
            start = xform.Coordinates;
        }

        var warps = EntityQueryEnumerator<WarpPointComponent, TransformComponent>();
        while (start == null && warps.MoveNext(out var uid, out var warp, out var xform))
        {
            if (!warp.AdminOnly && _station.GetOwningStation(uid, xform) == station)
                start = xform.Coordinates;
        }

        if (_station.GetLargestGrid(station) is not { } largest || !TryComp<MapGridComponent>(largest, out var largestComp))
            return null;

        start ??= _map.GridTileToLocal(largest, largestComp, _map.TileIndicesFor(largest, largestComp,
            new EntityCoordinates(largest, largestComp.LocalAABB.Center)));
        if (start.Value.EntityId is not { Valid: true } startGrid || !TryComp<MapGridComponent>(startGrid, out var startComp))
        {
            startGrid = largest;
            startComp = largestComp;
        }

        return ClearTileNear(startGrid, startComp, start.Value);
    }

    /// <summary>The contract drop marker on a grid nearest a point, within a range.</summary>
    private EntityCoordinates? NearestDrop(EntityUid grid, EntityCoordinates near, float range)
    {
        EntityCoordinates? best = null;
        var bestDistance = range;
        var drops = EntityQueryEnumerator<WFContractDropComponent, TransformComponent>();
        while (drops.MoveNext(out _, out _, out var xform))
        {
            if (xform.GridUid != grid || !xform.Coordinates.TryDistance(EntityManager, _transform, near, out var distance)
                || distance > bestDistance)
                continue;

            bestDistance = distance;
            best = xform.Coordinates;
        }

        return best;
    }

    /// <summary>Whether cargo can be left on a tile: floor with nothing solid on it.</summary>
    private bool IsClearTile(EntityUid grid, MapGridComponent comp, Vector2i index)
    {
        return _map.TryGetTileRef(grid, comp, index, out var tile) && !_turf.IsSpace(tile)
               && !_turf.IsTileBlocked(grid, index, CargoBlockers, comp);
    }

    /// <summary>The nearest clear floor tile to a point, walked out to in a fixed order without crossing walls.</summary>
    private EntityCoordinates? ClearTileNear(EntityUid grid, MapGridComponent comp, EntityCoordinates near,
        System.Numerics.Vector2? first = null, bool skipStart = false)
    {
        var start = _map.TileIndicesFor(grid, comp, near);
        var directions = new List<Vector2i> { new(0, -1), new(1, 0), new(0, 1), new(-1, 0) };
        if (first is { } toward && toward.LengthSquared() > 0.01f)
        {
            var preferred = new Vector2i((int) MathF.Round(toward.X), (int) MathF.Round(toward.Y));
            if (directions.Remove(preferred))
                directions.Insert(0, preferred);
        }

        var seen = new HashSet<Vector2i> { start };
        var queue = new Queue<Vector2i>();
        queue.Enqueue(start);
        while (queue.Count > 0 && seen.Count <= DropSearchTiles)
        {
            var tile = queue.Dequeue();
            if ((tile != start || !skipStart) && IsClearTile(grid, comp, tile))
                return _map.GridTileToLocal(grid, comp, tile);

            foreach (var direction in directions)
            {
                var next = tile + direction;
                if (!seen.Add(next) || !_map.TryGetTileRef(grid, comp, next, out var nextTile) || _turf.IsSpace(nextTile)
                    || _turf.IsTileBlocked(grid, next, CollisionGroup.Impassable, comp))
                    continue;

                queue.Enqueue(next);
            }
        }

        return null;
    }

    /// <summary>Metres between two stations' largest grids.</summary>
    private float Distance(EntityUid a, EntityUid b)
    {
        if (_station.GetLargestGrid(a) is not { } gridA || _station.GetLargestGrid(b) is not { } gridB)
            return 0f;

        var posA = _transform.GetMapCoordinates(gridA);
        var posB = _transform.GetMapCoordinates(gridB);
        return posA.MapId == posB.MapId ? (posA.Position - posB.Position).Length() : 0f;
    }

    public string StationName(EntityUid? station)
    {
        return station is { } uid && !TerminatingOrDeleted(uid) ? Name(uid) : Loc.GetString("wf-contract-station-unknown");
    }
}
