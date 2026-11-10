using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.Shared.Physics;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using PhysicsTransform = Robust.Shared.Physics.Transform;

namespace Content.Shared._WF.Vehicles.Core;

/// <summary>What stands at a place someone leaving a vehicle could land.</summary>
public enum VehicleExitSpotKind : byte
{
    /// <summary>A hard object of any grid or of the map stands there.</summary>
    Blocked,

    /// <summary>Free floor of the vehicle's own grid; for a vehicle on no grid, any free ground.</summary>
    OwnFloor,

    /// <summary>Free floor of another grid, the topmost one there.</summary>
    OtherFloor,

    /// <summary>No floor under it on any grid: the edge of a deck, or open space.</summary>
    NoFloor,
}

/// <summary>Finds somewhere a person leaving a vehicle can stand: free floor, reachable from the hull.</summary>
/// <remarks>
/// A place is free only if nothing hard blocks it on any grid or on the map, and the floor under it is the topmost one there.
/// <see cref="TryFindConnectedSpot"/> offers only places that can be walked to from the hull across free floor, so an exit
/// never lands behind a wall, a closed door or a grid edge.
/// </remarks>
public sealed partial class VehicleExitSpotSystem : EntitySystem
{
    /// <summary>The share of a tile a blocker is tested against, so a wall in the next tile is not one.</summary>
    private const float BlockerBox = 0.95f;

    /// <summary>The smallest overlap of a hard fixture with the tested box that blocks, in square tiles.</summary>
    private const float BlockerArea = 0.1f;

    /// <summary>A gap this small, in tiles, between a tile and the hull still counts as touching it.</summary>
    private const float TouchGap = 0.1f;

    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    [Dependency] private EntityQuery<FixturesComponent> _fixtureQuery = default!;
    [Dependency] private EntityQuery<MapGridComponent> _gridQuery = default!;
    [Dependency] private EntityQuery<PhysicsComponent> _physicsQuery = default!;

    private static readonly Vector2i[] Steps = { new(1, 0), new(-1, 0), new(0, 1), new(0, -1) };

    private readonly HashSet<EntityUid> _hits = new();
    private readonly Dictionary<Vector2i, VehicleExitSpotKind> _kinds = new();
    private readonly HashSet<Vector2i> _queued = new();
    private readonly Queue<Vector2i> _queue = new();

    /// <summary>The tiles of the vehicle's parent frame around its hull, in that frame.</summary>
    private readonly record struct Lattice(float Size, Matrix3x2 Matrix, Box2 Hull, Box2 Occupied, Box2 Area)
    {
        public Vector2 Local(Vector2i cell) => new((cell.X + 0.5f) * Size, (cell.Y + 0.5f) * Size);

        public Vector2 World(Vector2i cell) => Vector2.Transform(Local(cell), Matrix);

        /// <summary>Whether a person standing at the middle of the tile would be inside the hull.</summary>
        public bool IsBody(Vector2i cell) => Occupied.Contains(Local(cell));

        public bool InArea(Vector2i cell) => Area.Contains(Local(cell));

        /// <summary>Whether a tile shares an edge with the hull, or overlaps it; a tile that meets only a corner does not.</summary>
        public bool Touches(Vector2i cell)
        {
            var middle = Local(cell);
            var half = Size / 2f;
            var gap = TouchGap * Size;
            var overlapX = MathF.Min(middle.X + half, Hull.Right + gap) - MathF.Max(middle.X - half, Hull.Left - gap);
            var overlapY = MathF.Min(middle.Y + half, Hull.Top + gap) - MathF.Max(middle.Y - half, Hull.Bottom - gap);

            return overlapX > 0f && overlapY > 0f && (overlapX > 2f * gap || overlapY > 2f * gap);
        }

        /// <summary>How many tiles the middle of a tile is from the hull, rounded.</summary>
        public int Ring(Vector2i cell)
        {
            var middle = Local(cell);
            var gapX = MathF.Max(MathF.Max(Hull.Left - middle.X, middle.X - Hull.Right), 0f);
            var gapY = MathF.Max(MathF.Max(Hull.Bottom - middle.Y, middle.Y - Hull.Top), 0f);
            return (int) MathF.Floor(MathF.Sqrt(gapX * gapX + gapY * gapY) / Size + 0.5f);
        }
    }

    /// <summary>What a person leaving <paramref name="vehicle"/> would find at <paramref name="position"/>: the topmost floor and whether anything hard blocks it.</summary>
    public VehicleExitSpotKind Classify(MapCoordinates position, EntityUid vehicle, EntityUid user)
    {
        if (position.MapId == MapId.Nullspace)
            return VehicleExitSpotKind.Blocked;

        var vehicleGrid = Transform(vehicle).GridUid;

        if (!TryGetFloor(position, out var gridUid, out var grid, out var tile))
        {
            // A vehicle on no grid has no deck to keep people on; one that is has an edge to fall off.
            if (vehicleGrid != null)
                return VehicleExitSpotKind.NoFloor;

            return IsBlocked(position.MapId, position.Position, Angle.Zero, 1f, vehicle, user)
                ? VehicleExitSpotKind.Blocked
                : VehicleExitSpotKind.OwnFloor;
        }

        var size = (float) grid.TileSize;
        var middle = Vector2.Transform(
            new Vector2((tile.GridIndices.X + 0.5f) * size, (tile.GridIndices.Y + 0.5f) * size),
            _transform.GetWorldMatrix(gridUid));

        if (IsBlocked(position.MapId, middle, _transform.GetWorldRotation(gridUid), size, vehicle, user))
            return VehicleExitSpotKind.Blocked;

        return vehicleGrid == null || gridUid == vehicleGrid
            ? VehicleExitSpotKind.OwnFloor
            : VehicleExitSpotKind.OtherFloor;
    }

    /// <summary>The free floor nearest <paramref name="preferred"/> within <paramref name="reach"/> that can be walked to from the hull; the vehicle's own grid first.</summary>
    public bool TryFindConnectedSpot(EntityUid vehicle, EntityUid user, Vector2 preferred, float reach, out MapCoordinates spot)
    {
        spot = default;

        var xform = Transform(vehicle);
        var mapId = xform.MapID;
        var parent = xform.ParentUid.IsValid() ? xform.ParentUid : xform.MapUid ?? EntityUid.Invalid;
        if (mapId == MapId.Nullspace || !parent.IsValid())
            return false;

        // Tiles are those of the frame the vehicle stands in, whichever grid or grids lie under them.
        var size = TryComp(parent, out MapGridComponent? parentGrid) ? (float) parentGrid.TileSize : 1f;
        var hull = _lookup.GetAABBNoContainer(vehicle, xform.LocalPosition, xform.LocalRotation);
        var userBounds = _lookup.GetWorldAABB(user);
        var clearance = MathF.Max(userBounds.Width, userBounds.Height) / 2f;
        var lattice = new Lattice(size, _transform.GetWorldMatrix(parent), hull, hull.Enlarged(clearance), hull.Enlarged(reach));
        var vehicleGrid = xform.GridUid;

        _kinds.Clear();
        _queued.Clear();
        _queue.Clear();

        var min = new Vector2i((int) MathF.Floor(lattice.Area.Left / size), (int) MathF.Floor(lattice.Area.Bottom / size));
        var max = new Vector2i((int) MathF.Floor(lattice.Area.Right / size), (int) MathF.Floor(lattice.Area.Top / size));

        for (var x = min.X; x <= max.X; x++)
        {
            for (var y = min.Y; y <= max.Y; y++)
            {
                var cell = new Vector2i(x, y);
                if (!lattice.InArea(cell) || lattice.IsBody(cell))
                    continue;

                if (!lattice.Touches(cell) && !IsNextToBody(lattice, cell))
                    continue;

                if (IsFree(lattice, mapId, cell, vehicle, user) && _queued.Add(cell))
                    _queue.Enqueue(cell);
            }
        }

        var found = false;
        var bestGroup = 0;
        var bestRing = 0;
        var bestGap = 0f;

        while (_queue.TryDequeue(out var cell))
        {
            var group = _kinds[cell] == VehicleExitSpotKind.OwnFloor ? 0 : 1;
            var world = lattice.World(cell);
            var ring = lattice.Ring(cell);
            var gap = (world - preferred).LengthSquared();

            if (!found ||
                group < bestGroup ||
                group == bestGroup && (ring < bestRing || ring == bestRing && gap < bestGap))
            {
                found = true;
                bestGroup = group;
                bestRing = ring;
                bestGap = gap;
                spot = new MapCoordinates(world, mapId);
            }

            foreach (var step in Steps)
            {
                var next = cell + step;
                if (!lattice.InArea(next) || lattice.IsBody(next) || _queued.Contains(next))
                    continue;

                if (IsFree(lattice, mapId, next, vehicle, user))
                {
                    _queued.Add(next);
                    _queue.Enqueue(next);
                }
            }
        }

        _kinds.Clear();
        _queued.Clear();
        return found;
    }

    private static bool IsNextToBody(Lattice lattice, Vector2i cell)
    {
        foreach (var step in Steps)
        {
            if (lattice.IsBody(cell + step))
                return true;
        }

        return false;
    }

    /// <summary>Whether a person can stand on the tile: it has floor and nothing hard blocks it. Each tile is asked once per search.</summary>
    private bool IsFree(Lattice lattice, MapId mapId, Vector2i cell, EntityUid vehicle, EntityUid user)
    {
        if (!_kinds.TryGetValue(cell, out var kind))
        {
            kind = Classify(new MapCoordinates(lattice.World(cell), mapId), vehicle, user);
            _kinds[cell] = kind;
        }

        return kind is VehicleExitSpotKind.OwnFloor or VehicleExitSpotKind.OtherFloor;
    }

    /// <summary>The topmost grid with a tile at a point: another grid's tile hides the ground's, and a tile with nothing in it hides nothing.</summary>
    private bool TryGetFloor(MapCoordinates position, out EntityUid gridUid, [NotNullWhen(true)] out MapGridComponent? grid, out TileRef tile)
    {
        tile = default;

        if (_map.TryFindGridAt(position, out gridUid, out grid) &&
            _map.TryGetTileRef(gridUid, grid, position.Position, out tile) &&
            !tile.Tile.IsEmpty)
        {
            return true;
        }

        grid = null;
        return false;
    }

    /// <summary>Whether a hard object a mob cannot walk through overlaps the tile centred on <paramref name="middle"/>, on any grid or on the map.</summary>
    private bool IsBlocked(MapId mapId, Vector2 middle, Angle rotation, float size, EntityUid vehicle, EntityUid user)
    {
        var box = Box2.UnitCentered.Scale(BlockerBox * size);
        var worldBox = new Box2Rotated(box.Translated(middle), rotation, middle);

        _hits.Clear();
        _lookup.GetEntitiesIntersecting(mapId, worldBox.CalcBoundingBox(), _hits, LookupFlags.Dynamic | LookupFlags.Static);

        foreach (var ent in _hits)
        {
            if (ent == vehicle || ent == user || _gridQuery.HasComp(ent))
                continue;

            if (!_fixtureQuery.TryComp(ent, out var fixtures))
                continue;

            // An open door and the like cannot be collided with.
            if (_physicsQuery.TryComp(ent, out var body) && !body.CanCollide)
                continue;

            var (position, entRotation) = _transform.GetWorldPositionRotation(ent);
            var fixtureTransform = new PhysicsTransform((-rotation).RotateVec(position - middle), (float) (entRotation - rotation).Theta);

            foreach (var fixture in fixtures.Fixtures.Values)
            {
                if (!fixture.Hard || (fixture.CollisionLayer & (int) CollisionGroup.MobMask) == 0)
                    continue;

                for (var i = 0; i < fixture.Shape.ChildCount; i++)
                {
                    var overlap = fixture.Shape.ComputeAABB(fixtureTransform, i).Intersect(box);
                    if (overlap.Width * overlap.Height > BlockerArea)
                        return true;
                }
            }
        }

        return false;
    }
}
