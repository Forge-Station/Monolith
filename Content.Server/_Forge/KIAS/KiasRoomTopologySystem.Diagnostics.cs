using System.Linq;
using Content.Shared._Forge.KIAS;
using Robust.Shared.Map.Components;
using Content.Shared.Doors.Components;
using Robust.Shared.Physics;
using System.Security.Cryptography;
using System.Text;

namespace Content.Server._Forge.KIAS;

public sealed partial class KiasRoomTopologySystem
{
    public object DebugSnapshot(EntityUid grid)
    {
        if (!_grids.TryGetValue(grid, out var cache) || !TryComp<MapGridComponent>(grid, out var map))
            return new { grid = grid.ToString(), status = "REBUILD_PENDING" };
        var scanners = new List<object>();
        var physicalDoors = new List<object>();
        var doorQuery = EntityQueryEnumerator<DoorComponent, TransformComponent>();
        while (doorQuery.MoveNext(out var uid, out _, out var transform))
        {
            if (transform.GridUid != grid) continue;
            var local = DoorGridTransform(grid, uid, transform);
            physicalDoors.Add(new { uid = uid.ToString(), prototype = MetaData(uid).EntityPrototype?.ID,
                position = transform.LocalPosition.ToString(), rotation = transform.LocalRotation.Degrees,
                fixtures = TryComp<FixturesComponent>(uid, out var fixtures) ? fixtures.Fixtures.Select(pair =>
                    new { id = pair.Key, pair.Value.Hard, shape = pair.Value.Shape.GetType().Name,
                        bounds = pair.Value.Shape.ComputeAABB(local, 0).ToString() }).ToArray() : null });
        }
        var query = EntityQueryEnumerator<KiasRoomScannerComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var scanner, out var transform))
        {
            if (transform.GridUid != grid) continue;
            var tile = _maps.TileIndicesFor(grid, map, transform.Coordinates);
            var valid = TryGetScannerRoom(uid, out var room, out var status);
            var inward = Inward(transform.LocalRotation);
            var seed = cache.Geometry.ScannerSeed(tile, inward);
            var neighbors = KiasRoomGeometry.Steps.Select(step =>
            {
                var cell = tile + step;
                return new { x = cell.X, y = cell.Y, floor = cache.Geometry.Floors.ContainsKey(cell),
                    blocked = cache.Geometry.Floors.GetValueOrDefault(cell).ToString(),
                    door = cache.Geometry.DoorTiles.Contains(cell), roomId = cache.Geometry.At(cell)?.Id ?? 0 };
            }).ToArray();
            var cells = valid ? ScannerCells(uid).ToArray() : Array.Empty<Vector2i>();
            var ordered = cells.OrderBy(cell => cell.X).ThenBy(cell => cell.Y).Select(cell => $"{cell.X},{cell.Y}");
            var interior = valid ? cells.Where(cell => !room.Doors.Contains(cell)).ToArray() : cells;
            var doors = valid ? cells.Where(cell => room.Doors.Contains(cell)).ToArray() : cells;
            scanners.Add(new { uid = uid.ToString(), prototype = MetaData(uid).EntityPrototype?.ID,
                x = tile.X, y = tile.Y, rotation = transform.LocalRotation.Degrees, inward = Inward(transform.LocalRotation).ToString(),
                localPosition = transform.LocalPosition.ToString(), parent = transform.ParentUid.ToString(),
                worldPosition = _transforms.GetWorldPosition(uid).ToString(), worldRotation = _transforms.GetWorldRotation(uid).Degrees,
                seed = new[] { seed.X, seed.Y }, neighbors,
                sourceWallBlocked = cache.Geometry.Floors.GetValueOrDefault(tile).ToString(),
                seedFloor = _turf.GetContentTileDefinition(_maps.GetTileRef(grid, map, seed).Tile).ID,
                seedIsSpace = cache.Geometry.SpaceDeck.Contains(seed),
                mode = status == KiasRoomStatus.ExteriorSector ? "ExteriorSector" : status == KiasRoomStatus.Ok ? "InteriorRoom" : status.ToString(),
                cellsSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(";", ordered)))).ToLowerInvariant(),
                roomId = valid ? room.Id : 0, status = status.ToString(), online = _kias.IsOnline(uid), modules = scanner.Modules.ToString(),
                tileCount = interior.Length, doorCount = doors.Length,
                physicalRoomTileCount = valid ? room.Tiles.Count : 0, fallbackRadius = status == KiasRoomStatus.OpenToSpace ? 7 : 0,
                cells = interior.Select(cell => new[] { cell.X, cell.Y }).ToArray(),
                doors = doors.Select(cell => new[] { cell.X, cell.Y }).ToArray() });
        }
        return new { grid = grid.ToString(), revision = cache.Revision, pending = cache.Pending,
            orientation = "0 South; 90 East; 180 North; 270 West; Transform.LocalRotation rotates local -Y",
            cache.Rebuilds, cache.IgnoredDoorChanges, cache.MaxCommitTicks, cache.TilesVisited,
            geometry = new {
                floors = cache.Geometry.Floors.Keys.Select(cell => new[] { cell.X, cell.Y }).ToArray(),
                walls = cache.Geometry.Floors.Where(pair => pair.Value == Content.Shared.Atmos.AtmosDirection.All)
                    .Select(pair => new[] { pair.Key.X, pair.Key.Y }).ToArray(),
                doors = cache.Geometry.DoorTiles.Select(cell => new[] { cell.X, cell.Y }).ToArray() },
            scanners, physicalDoors, rooms = cache.Geometry.Rooms.Select(room => new { room.Id, status = room.Status.ToString(),
                tiles = room.Tiles.Select(cell => new[] { cell.X, cell.Y }).ToArray(),
                doors = room.Doors.Select(cell => new[] { cell.X, cell.Y }).ToArray(), scanners = room.Scanners.Select(uid => uid.ToString()).ToArray() }).ToArray() };
    }
}
