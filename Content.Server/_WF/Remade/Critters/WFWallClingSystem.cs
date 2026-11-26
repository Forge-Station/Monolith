using Content.Shared.Maps;
using Content.Shared.Physics;
using Content.Shared.Tag;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.Remade.Critters;

/// <summary>
/// Leapers hugging walls. Finds the nearest free floor tile beside a wall for a <see cref="WFWallHuggerComponent"/> to stand on,
/// and says whether a mob is standing against one, which is what powers its leap.
/// </summary>
public sealed class WFWallClingSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private TagSystem _tags = default!;
    [Dependency] private TurfSystem _turf = default!;

    /// <summary>How far a mob's middle may be from a wall tile's middle and still count as against it: a tile and a bit.</summary>
    private const float Hug = 1.45f;

    private const float SearchEvery = 4f;

    private static readonly ProtoId<TagPrototype> WallTag = "Wall";
    private static readonly Vector2i[] Sides = { new(1, 0), new(-1, 0), new(0, 1), new(0, -1) };

    /// <summary>Whether the mob is standing against a wall.</summary>
    public bool AgainstWall(EntityUid uid)
    {
        var xform = Transform(uid);
        if (xform.GridUid is not { } grid || !TryComp<MapGridComponent>(grid, out var gridComp))
            return false;

        var tile = _map.TileIndicesFor(grid, gridComp, xform.Coordinates);
        var position = _transform.GetWorldPosition(xform);
        foreach (var side in Sides)
        {
            if (!HasWall(grid, gridComp, tile + side))
                continue;

            var wall = _transform.ToMapCoordinates(_map.GridTileToLocal(grid, gridComp, tile + side)).Position;
            if ((wall - position).Length() <= Hug)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Where the hugger should stand: where it is if that is against a wall, else its last pick, else the nearest free floor tile
    /// beside a wall. Null when there is none, and it won't search again for a few seconds.
    /// </summary>
    public EntityCoordinates? PickSpot(Entity<WFWallHuggerComponent> hugger)
    {
        var xform = Transform(hugger);
        if (xform.GridUid is not { } grid || !TryComp<MapGridComponent>(grid, out var gridComp))
            return null;

        if (AgainstWall(hugger))
        {
            hugger.Comp.Spot = xform.Coordinates;
            return xform.Coordinates;
        }

        if (hugger.Comp.Spot is { } held && IsSpot(grid, gridComp, _map.TileIndicesFor(grid, gridComp, held)))
            return held;

        hugger.Comp.Spot = null;
        var now = _timing.CurTime;
        if (now < hugger.Comp.NextSearch)
            return null;

        var spot = Search(grid, gridComp, _map.TileIndicesFor(grid, gridComp, xform.Coordinates), hugger.Comp.Range);
        if (spot is not { } found)
        {
            hugger.Comp.NextSearch = now + TimeSpan.FromSeconds(SearchEvery);
            return null;
        }

        hugger.Comp.Spot = _map.GridTileToLocal(grid, gridComp, found);
        return hugger.Comp.Spot;
    }

    /// <summary>Walks outward ring by ring and takes the nearest spot of the first ring that has one.</summary>
    private Vector2i? Search(EntityUid grid, MapGridComponent gridComp, Vector2i origin, int range)
    {
        for (var ring = 1; ring <= range; ring++)
        {
            Vector2i? best = null;
            var bestDistance = float.MaxValue;
            for (var x = -ring; x <= ring; x++)
            for (var y = -ring; y <= ring; y++)
            {
                if (Math.Max(Math.Abs(x), Math.Abs(y)) != ring)
                    continue;

                var tile = origin + new Vector2i(x, y);
                var distance = x * x + y * y;
                if (distance < bestDistance && IsSpot(grid, gridComp, tile))
                {
                    best = tile;
                    bestDistance = distance;
                }
            }

            if (best != null)
                return best;
        }

        return null;
    }

    /// <summary>A free floor tile with a wall on one side.</summary>
    private bool IsSpot(EntityUid grid, MapGridComponent gridComp, Vector2i tile)
    {
        if (!_map.TryGetTileRef(grid, gridComp, tile, out var tileRef)
            || tileRef.Tile.IsEmpty
            || _turf.IsTileBlocked(tileRef, CollisionGroup.MobMask))
        {
            return false;
        }

        foreach (var side in Sides)
        {
            if (HasWall(grid, gridComp, tile + side))
                return true;
        }

        return false;
    }

    private bool HasWall(EntityUid grid, MapGridComponent gridComp, Vector2i tile)
    {
        var anchored = _map.GetAnchoredEntities(grid, gridComp, tile);
        while (anchored.MoveNext(out var uid))
        {
            if (uid is { } wall && _tags.HasTag(wall, WallTag))
                return true;
        }

        return false;
    }
}
