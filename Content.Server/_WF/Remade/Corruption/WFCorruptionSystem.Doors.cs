using System.Linq;
using Content.Server._WF.Remade.Counterplay;
using Content.Server._WF.Remade.Monolith;
using Content.Server.Atmos.Components;
using Content.Shared.Atmos;
using Content.Shared.Doors.Components;
using Content.Shared.Maps;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._WF.Remade.Corruption;

/// <summary>
/// How big a hull is, and the way corruption gets through doors. The edge spreader treats a shut door or windoor as a wall and
/// won't spread from the tile one stands on, so a deck split into rooms would stop at its first door. Here a tile next to a
/// door, or holding a windoor that faces its neighbour, grows under the door (and any door behind it) and onto the tile past
/// it in one go.
/// </summary>
public sealed partial class WFCorruptionSystem
{
    [Dependency] private TurfSystem _turf = default!;

    private static readonly TimeSpan HullTtl = TimeSpan.FromSeconds(15);

    /// <summary>The most doors in a row, such as an airlock chamber, that corruption crosses in one step.</summary>
    private const int MaxDoorChain = 3;

    /// <summary>North, east, south, west.</summary>
    private static readonly Vector2i[] Cardinals = { new(0, 1), new(1, 0), new(0, -1), new(-1, 0) };

    private static readonly AtmosDirection[] CardinalDirections =
        { AtmosDirection.North, AtmosDirection.East, AtmosDirection.South, AtmosDirection.West };

    private readonly Dictionary<EntityUid, (TimeSpan Until, int Tiles)> _hulls = new();
    private readonly Dictionary<EntityUid, (TimeSpan Until, int Tiles)> _reach = new();
    private readonly HashSet<Vector2i> _walk = new();
    private readonly Queue<Vector2i> _walkQueue = new();
    private readonly List<(List<Vector2i> Doors, Vector2i Beyond, bool BeyondFree)> _doorways = new();

    /// <summary>Floor on a hull that corruption could cover: every tile but space and what a wall stands on. A door's tile counts.</summary>
    public int HullTiles(EntityUid gridUid)
    {
        var now = _timing.CurTime;
        if (_hulls.TryGetValue(gridUid, out var cached) && now < cached.Until)
            return cached.Tiles;

        if (!TryComp<MapGridComponent>(gridUid, out var grid))
            return 0;

        var count = 0;
        foreach (var tile in _map.GetAllTiles(gridUid, grid))
        {
            if (!_turf.IsSpace(tile) && !IsWalled((gridUid, grid), tile.GridIndices))
                count++;
        }

        _hulls[gridUid] = (now + HullTtl, count);
        return count;
    }

    /// <summary>
    /// Floor a Monolith's corruption could walk to from where it stands, through doors, on its own hull. A hull can hold sealed
    /// compartments nothing can reach, so a stage's share is taken of this and not of the whole hull.
    /// </summary>
    public int ReachTiles(Entity<WFMonolithComponent> monolith)
    {
        var now = _timing.CurTime;
        if (_reach.TryGetValue(monolith, out var cached) && now < cached.Until)
            return cached.Tiles;

        var xform = Transform(monolith);
        if (xform.GridUid is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var gridComp))
            return 0;

        var grid = new Entity<MapGridComponent>(gridUid, gridComp);
        var start = _map.TileIndicesFor(gridUid, gridComp, xform.Coordinates);
        _walk.Clear();
        _walkQueue.Clear();
        _walk.Add(start);
        _walkQueue.Enqueue(start);
        while (_walkQueue.Count > 0)
        {
            var tile = _walkQueue.Dequeue();
            foreach (var step in Cardinals)
            {
                var next = tile + step;
                if (_walk.Contains(next)
                    || !_map.TryGetTileRef(gridUid, gridComp, next, out var tileRef)
                    || _turf.IsSpace(tileRef)
                    || IsWalled(grid, next))
                {
                    continue;
                }

                _walk.Add(next);
                _walkQueue.Enqueue(next);
            }
        }

        _reach[monolith] = (now + HullTtl, _walk.Count);
        return _walk.Count;
    }

    /// <summary>Whether the hull already carries all the corruption it can hold.</summary>
    public bool GridFull(EntityUid grid)
    {
        return GridTiles(grid) >= Math.Min(HullCap, HullTiles(grid));
    }

    private void PruneHulls()
    {
        foreach (var grid in _hulls.Keys.ToList())
        {
            if (TerminatingOrDeleted(grid))
                _hulls.Remove(grid);
        }

        foreach (var monolith in _reach.Keys.ToList())
        {
            if (TerminatingOrDeleted(monolith))
                _reach.Remove(monolith);
        }
    }

    /// <summary>Whether something that shuts out air on every side stands here, and isn't a door.</summary>
    private bool IsWalled(Entity<MapGridComponent> grid, Vector2i tile)
    {
        var anchored = _map.GetAnchoredEntities(grid, tile);
        while (anchored.MoveNext(out var uid))
        {
            if (TryComp<AirtightComponent>(uid, out var airtight)
                && airtight.AirBlocked
                && airtight.AirBlockedDirection == AtmosDirection.All
                && !HasComp<DoorComponent>(uid))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The sides on which the doors and windoors standing on a tile shut out air.</summary>
    private AtmosDirection DoorBlocks(Entity<MapGridComponent> grid, Vector2i tile)
    {
        var blocked = AtmosDirection.Invalid;
        var anchored = _map.GetAnchoredEntities(grid, tile);
        while (anchored.MoveNext(out var uid))
        {
            if (HasComp<DoorComponent>(uid) && TryComp<AirtightComponent>(uid, out var airtight) && airtight.AirBlocked)
                blocked |= airtight.AirBlockedDirection;
        }

        return blocked;
    }

    /// <summary>Fills <see cref="_doorways"/> with the closed doors beside a tile that corruption has still to get under or past.</summary>
    private void FindDoorways(Entity<MapGridComponent> grid, Vector2i tile)
    {
        _doorways.Clear();
        var own = DoorBlocks(grid, tile);
        for (var i = 0; i < Cardinals.Length; i++)
        {
            var step = Cardinals[i];
            var direction = CardinalDirections[i];
            var next = tile + step;
            var shutHere = (own & direction) != 0;
            var shutThere = (DoorBlocks(grid, next) & direction.GetOpposite()) != 0;
            if (!shutHere && !shutThere)
                continue;

            var doors = new List<Vector2i>();
            var cursor = next;
            while (doors.Count < MaxDoorChain && DoorBlocks(grid, cursor) != AtmosDirection.Invalid)
            {
                if (!IsCorrupted(grid, cursor))
                    doors.Add(cursor);

                cursor += step;
            }

            var beyondFree = _map.TryGetTileRef(grid.Owner, grid.Comp, cursor, out var tileRef)
                             && !_turf.IsSpace(tileRef)
                             && !IsWalled(grid, cursor)
                             && DoorBlocks(grid, cursor) == AtmosDirection.Invalid
                             && !IsCorrupted(grid, cursor);

            if (beyondFree || doors.Count > 0)
                _doorways.Add((doors, cursor, beyondFree));
        }
    }

    private bool HasDoorway(Entity<WFCorruptionComponent> ent, TransformComponent xform)
    {
        if (xform.GridUid is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var grid))
            return false;

        FindDoorways((gridUid, grid), _map.TileIndicesFor(gridUid, grid, xform.Coordinates));
        return _doorways.Count > 0;
    }

    private void SpreadThroughDoors(Entity<WFCorruptionComponent> ent, Entity<WFMonolithComponent> source, TransformComponent xform, ref int updates)
    {
        if (xform.GridUid is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var grid))
            return;

        FindDoorways((gridUid, grid), _map.TileIndicesFor(gridUid, grid, xform.Coordinates));
        foreach (var (doors, beyond, beyondFree) in _doorways.ToArray())
        {
            foreach (var door in doors)
            {
                if (TryGrowAt(source, gridUid, grid, door, ent.Comp.Node))
                    updates--;

                if (updates <= 0)
                    return;
            }

            if (beyondFree && TryGrowAt(source, gridUid, grid, beyond, ent.Comp.Node))
                updates--;

            if (updates <= 0)
                return;
        }
    }

    /// <summary>Grows a tile if the sanctuary, scorch and the Monolith's budget allow it.</summary>
    private bool TryGrowAt(Entity<WFMonolithComponent> source, EntityUid gridUid, MapGridComponent grid, Vector2i tile, EntityUid? from)
    {
        if (IsSanctuary(gridUid) || GridFull(gridUid))
            return false;

        var attempt = new WFCorruptionGrowAttemptEvent(tile);
        RaiseLocalEvent(gridUid, ref attempt);
        if (attempt.Cancelled)
            return false;

        var coordinates = _map.GridTileToLocal(gridUid, grid, tile);
        if (!TryPay(source, coordinates, out var node, from))
            return false;

        Grow(source, coordinates, node);
        return true;
    }
}
