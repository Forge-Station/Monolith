using System.Numerics;
using System.Linq;
using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Shared._Forge.KIAS;
using Content.Shared.Atmos;
using Content.Shared.Doors.Components;
using Content.Shared.Maps;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Collision.Shapes;

namespace Content.Server._Forge.KIAS;

[ByRefEvent]
public readonly record struct KiasRoomsChangedEvent(EntityUid Grid, uint Revision);

[ByRefEvent]
public readonly record struct KiasRoomsInvalidatedEvent(EntityUid Grid);

public sealed partial class KiasRoomTopologySystem : EntitySystem
{
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private SharedTransformSystem _transforms = default!;
    [Dependency] private AtmosphereSystem _atmos = default!;
    [Dependency] private KiasSystem _kias = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private TurfSystem _turf = default!;

    public sealed class GridRooms
    {
        public KiasRoomGeometry Geometry = new();
        public readonly Dictionary<EntityUid, KiasPhysicalRoom> Scanners = new();
        public readonly Dictionary<EntityUid, HashSet<Vector2i>> OpenAreas = new();
        public readonly Dictionary<EntityUid, (Vector2i Tile, Vector2i Facing)> InteriorPlacements = new();
        public uint Revision;
        public bool Pending = true;
        public uint DirtyTick;
        public IEnumerator<bool>? Job;
        public uint JobGeneration, Generation;
        public long Rebuilds, IgnoredDoorChanges, MaxCommitTicks, TilesVisited;
    }

    private readonly Dictionary<EntityUid, GridRooms> _grids = new();
    private readonly Queue<EntityUid> _queue = new();
    private readonly HashSet<EntityUid> _queued = new();
    public IReadOnlyDictionary<EntityUid, GridRooms> Grids => _grids;

    public override void Initialize()
    {
        SubscribeLocalEvent<TileChangedEvent>(OnTiles);
        SubscribeLocalEvent<AirtightChanged>(OnAirtight);
        SubscribeLocalEvent<DoorComponent, ComponentStartup>(OnDoorStartup);
        SubscribeLocalEvent<DoorComponent, ComponentShutdown>(OnDoorShutdown);
        SubscribeLocalEvent<DoorComponent, MoveEvent>(OnDoorMove);
        SubscribeLocalEvent<KiasRoomScannerComponent, ComponentStartup>(OnScannerStartup);
        SubscribeLocalEvent<KiasRoomScannerComponent, MoveEvent>(OnScannerMove);
        SubscribeLocalEvent<KiasRoomScannerComponent, GridUidChangedEvent>(OnScannerGrid);
        SubscribeLocalEvent<KiasRoomScannerComponent, AnchorStateChangedEvent>(OnScannerAnchor);
        SubscribeLocalEvent<GridRemovalEvent>(OnGridRemoval);
        SubscribeLocalEvent<GridSplitEvent>(OnGridSplit);
    }

    private void OnTiles(ref TileChangedEvent args)
    {
        foreach (var change in args.Changes)
        {
            if (!change.EmptyChanged) continue;
            Invalidate(args.Entity.Owner);
            return;
        }
    }
    private void OnAirtight(ref AirtightChanged args)
    {
        if (args.AirBlockedChanged && HasComp<DoorComponent>(args.Entity))
        {
            if (_grids.TryGetValue(args.Position.Grid, out var rooms)) rooms.IgnoredDoorChanges++;
            return;
        }
        Invalidate(args.Position.Grid);
    }
    private void OnDoorStartup(Entity<DoorComponent> ent, ref ComponentStartup args) => Invalidate(Transform(ent).GridUid);
    private void OnDoorShutdown(Entity<DoorComponent> ent, ref ComponentShutdown args) => Invalidate(Transform(ent).GridUid);
    private void OnDoorMove(Entity<DoorComponent> ent, ref MoveEvent args)
    {
        Invalidate(Transform(ent).GridUid);
        Invalidate(args.OldPosition.EntityId);
    }
    private void OnScannerStartup(Entity<KiasRoomScannerComponent> ent, ref ComponentStartup args) => Invalidate(Transform(ent).GridUid);
    private void OnScannerMove(Entity<KiasRoomScannerComponent> ent, ref MoveEvent args) => Invalidate(Transform(ent).GridUid);
    private void OnScannerGrid(Entity<KiasRoomScannerComponent> ent, ref GridUidChangedEvent args)
    {
        Invalidate(args.OldGrid); Invalidate(args.NewGrid);
    }
    private void OnScannerAnchor(Entity<KiasRoomScannerComponent> ent, ref AnchorStateChangedEvent args) => Invalidate(Transform(ent).GridUid);
    private void OnGridRemoval(GridRemovalEvent args)
    {
        if (_grids.Remove(args.EntityUid, out var rooms)) rooms.Job?.Dispose();
        _queued.Remove(args.EntityUid);
    }
    private void OnGridSplit(ref GridSplitEvent args)
    {
        Invalidate(args.Grid);
        foreach (var grid in args.NewGrids) Invalidate(grid);
    }

    public void Invalidate(EntityUid? grid)
    {
        if (grid is not { } uid || !HasComp<MapGridComponent>(uid) || TerminatingOrDeleted(uid)) return;
        if (!_grids.TryGetValue(uid, out var rooms))
        {
            if (!HasComp<KiasGridComponent>(uid) && !HasScanner(uid)) return;
            _grids.Add(uid, rooms = new() { DirtyTick = _timing.CurTick.Value });
        }
        var wasPending = rooms.Pending;
        if (!wasPending) rooms.DirtyTick = _timing.CurTick.Value;
        rooms.Pending = true;
        rooms.Generation++;
        if (_queued.Add(uid)) _queue.Enqueue(uid);
        if (!wasPending)
        {
            var changed = new KiasRoomsInvalidatedEvent(uid);
            RaiseLocalEvent(uid, ref changed, true);
        }
    }

    private bool HasScanner(EntityUid grid)
    {
        var query = EntityQueryEnumerator<KiasRoomScannerComponent, TransformComponent>();
        while (query.MoveNext(out _, out _, out var transform)) if (transform.GridUid == grid) return true;
        return false;
    }

    public override void Update(float frameTime)
    {
        var remaining = 4096;
        var grids = Math.Min(_queue.Count, 8);
        for (var i = 0; i < grids && remaining > 0 && _queue.TryDequeue(out var grid); i++)
        {
            _queued.Remove(grid);
            if (!_grids.TryGetValue(grid, out var rooms) || TerminatingOrDeleted(grid)) continue;
            if (rooms.Job == null || rooms.JobGeneration != rooms.Generation)
            {
                rooms.Job?.Dispose();
                rooms.JobGeneration = rooms.Generation;
                rooms.Job = Build(grid, rooms).GetEnumerator();
            }
            var budget = Math.Min(512, remaining);
            for (var work = 0; work < budget; work++, remaining--)
            {
                if (rooms.Job.MoveNext()) continue;
                rooms.Job.Dispose(); rooms.Job = null;
                break;
            }
            if (rooms.Pending && _queued.Add(grid)) _queue.Enqueue(grid);
        }
    }

    private IEnumerable<bool> Build(EntityUid grid, GridRooms cache)
    {
        if (!TryComp<MapGridComponent>(grid, out var map)) yield break;
        var geometry = new KiasRoomGeometry();
        var tiles = _maps.GetAllTiles(grid, map);
        while (tiles.MoveNext(out var tile))
        {
            if (tile is not { } floor) continue;
            var blocked = AtmosDirection.Invalid;
            foreach (var direction in KiasRoomGeometry.Directions)
                if (_atmos.IsTileAirBlocked(grid, floor.GridIndices, direction, map)) blocked |= direction;
            geometry.Floors.Add(floor.GridIndices, blocked);
            if (_turf.IsSpace(floor)) geometry.SpaceDeck.Add(floor.GridIndices);
            yield return true;
        }
        var doors = EntityQueryEnumerator<DoorComponent, TransformComponent>();
        while (doors.MoveNext(out var door, out _, out var transform))
        {
            yield return true;
            if (transform.GridUid != grid || !transform.Anchored || TerminatingOrDeleted(door)) continue;
            if (TryComp<FixturesComponent>(door, out var fixtures))
            {
                var local = DoorGridTransform(grid, door, transform);
                foreach (var fixture in fixtures.Fixtures.Values)
                {
                    // Берём физическую створку, исключаем датчики стыковки и приближения.
                    if (!fixture.Hard) continue;
                    for (var child = 0; child < fixture.Shape.ChildCount; child++)
                    {
                        var bounds = fixture.Shape.ComputeAABB(local, child);
                        var skin = fixture.Shape.ShapeType == ShapeType.Circle ? 0 : fixture.Shape.Radius;
                        for (var x = (int) MathF.Floor(bounds.Left + skin + .001f); x <= (int) MathF.Floor(bounds.Right - skin - .001f); x++)
                        for (var y = (int) MathF.Floor(bounds.Bottom + skin + .001f); y <= (int) MathF.Floor(bounds.Top - skin - .001f); y++)
                            geometry.DoorTiles.Add(new Vector2i(x, y));
                    }
                }
            }
            geometry.DoorTiles.Add(_maps.TileIndicesFor(grid, map, transform.Coordinates));
        }
        foreach (var step in geometry.Build()) yield return step;
        cache.Geometry = geometry;
        cache.OpenAreas.Clear();
        cache.Pending = false;
        cache.Revision++;
        cache.Rebuilds++;
        cache.TilesVisited += geometry.Visited;
        cache.MaxCommitTicks = Math.Max(cache.MaxCommitTicks, _timing.CurTick.Value - cache.DirtyTick);
        BindScanners(grid);
        var changed = new KiasRoomsChangedEvent(grid, cache.Revision);
        RaiseLocalEvent(grid, ref changed, true);
    }

    private Robust.Shared.Physics.Transform DoorGridTransform(EntityUid grid, EntityUid door, TransformComponent transform)
    {
        if (transform.ParentUid == grid)
            return new Robust.Shared.Physics.Transform(transform.LocalPosition, transform.LocalRotation);

        return new Robust.Shared.Physics.Transform(
            _transforms.ToCoordinates(grid, _transforms.GetMapCoordinates(door)).Position,
            _transforms.GetWorldRotation(door) - _transforms.GetWorldRotation(grid));
    }

    public static Vector2i Inward(Angle rotation)
    {
        var direction = rotation.RotateVec(new Vector2(0, -1));
        return MathF.Abs(direction.X) > MathF.Abs(direction.Y)
            ? new Vector2i(Math.Sign(direction.X), 0) : new Vector2i(0, Math.Sign(direction.Y));
    }

    public void BindScanners(EntityUid grid)
    {
        if (!_grids.TryGetValue(grid, out var cache) || cache.Pending || !TryComp<MapGridComponent>(grid, out var map)) return;
        cache.Scanners.Clear();
        cache.OpenAreas.Clear();
        foreach (var uid in cache.InteriorPlacements.Keys.ToArray())
            if (TerminatingOrDeleted(uid) || !TryComp<TransformComponent>(uid, out var placementTransform)
                || placementTransform.GridUid != grid || !placementTransform.Anchored)
                cache.InteriorPlacements.Remove(uid);
        foreach (var room in cache.Geometry.Rooms) room.Scanners.Clear();
        var query = EntityQueryEnumerator<KiasRoomScannerComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var scanner, out var transform))
        {
            if (transform.GridUid != grid || !transform.Anchored || TerminatingOrDeleted(uid)) continue;
            var tile = _maps.TileIndicesFor(grid, map, transform.Coordinates);
            var facing = Inward(transform.LocalRotation);
            var seed = cache.Geometry.ScannerSeed(tile, facing);
            var room = cache.Geometry.At(seed);
            var placement = (tile, facing);
            if (cache.InteriorPlacements.TryGetValue(uid, out var interior) && interior != placement)
                cache.InteriorPlacements.Remove(uid);
            var exteriorDeck = scanner.Advanced && !cache.InteriorPlacements.ContainsKey(uid)
                && room != null && cache.Geometry.IsExteriorDeck(room, tile, facing);
            if (room is not { Usable: true } || exteriorDeck)
            {
                if (!scanner.Advanced || cache.InteriorPlacements.ContainsKey(uid)) continue;
                var exterior = cache.Geometry.ExteriorCoverage(tile, facing, scanner.Range, exteriorDeck ? room!.Tiles : null);
                if (exterior.Count == 0) continue;
                room = new KiasPhysicalRoom { Id = -uid.Id, Status = KiasRoomStatus.ExteriorSector };
                room.Tiles.UnionWith(exterior);
                cache.OpenAreas.Add(uid, exterior);
            }
            if (room.Status == KiasRoomStatus.Ok) cache.InteriorPlacements[uid] = placement;
            cache.Scanners.Add(uid, room);
            room.Scanners.Add(uid);
            if (room.Status == KiasRoomStatus.OpenToSpace && !cache.OpenAreas.ContainsKey(uid))
                cache.OpenAreas.Add(uid, cache.Geometry.OpenCoverage(room, seed, tile));
        }
        foreach (var room in cache.Geometry.Rooms) room.Scanners.Sort((a, b) => a.Id.CompareTo(b.Id));
    }
}
