using Content.Shared._Forge.KIAS;
using Robust.Shared.Map.Components;

namespace Content.Server._Forge.KIAS;

public sealed partial class KiasRoomTopologySystem
{
    public bool TryGetScannerRoom(EntityUid scanner, out KiasPhysicalRoom room, out KiasRoomStatus status)
    {
        room = default!;
        status = KiasRoomStatus.NoInteriorSeed;
        if (TerminatingOrDeleted(scanner) || Transform(scanner).GridUid is not { } grid) return false;
        if (!_grids.TryGetValue(grid, out var cache) || cache.Pending)
        {
            status = KiasRoomStatus.RebuildPending;
            return false;
        }
        if (!cache.Scanners.TryGetValue(scanner, out room!))
        {
            if (TryComp<MapGridComponent>(grid, out var map))
            {
                var transform = Transform(scanner);
                var seed = cache.Geometry.ScannerSeed(_maps.TileIndicesFor(grid, map, transform.Coordinates), Inward(transform.LocalRotation));
                if (cache.Geometry.At(seed) is { } invalid) status = invalid.Status;
            }
            return false;
        }
        status = room.Status;
        return room.Usable;
    }

    public bool Contains(EntityUid scanner, EntityUid target, KiasScannerModules module)
    {
        return !TerminatingOrDeleted(scanner) && !TerminatingOrDeleted(target)
            && _kias.IsOnline(scanner) && TryComp<KiasRoomScannerComponent>(scanner, out var component)
            && component.LifeStage <= ComponentLifeStage.Running && (component.Modules & module) != 0
            && (ContainsTargetForScanner(scanner, target) || ContainsExterior(scanner, target, module));
    }

    private bool ContainsExterior(EntityUid scanner, EntityUid target, KiasScannerModules module)
    {
        if ((module & ~KiasScannerModules.Connector) == 0
            || !TryGetScannerRoom(scanner, out var room, out _) || room.Status != KiasRoomStatus.ExteriorSector
            || Transform(scanner).GridUid is not { } grid || Transform(scanner).MapUid != Transform(target).MapUid) return false;
        var local = _transforms.ToCoordinates(grid, _transforms.GetMapCoordinates(target));
        return room.Tiles.Contains(_maps.TileIndicesFor(grid, Comp<MapGridComponent>(grid), local));
    }

    public bool ContainsTargetForScanner(EntityUid scanner, EntityUid target)
    {
        if (!TryGetScannerRoom(scanner, out var room, out _) || room.Status == KiasRoomStatus.ExteriorSector || Transform(scanner).GridUid is not { } grid
            || !ContainsTarget(grid, room, target)) return false;
        if (!_grids[grid].OpenAreas.TryGetValue(scanner, out var cells)) return true;
        var tile = _maps.TileIndicesFor(grid, Comp<MapGridComponent>(grid), _transforms.ToCoordinates(grid, _transforms.GetMapCoordinates(target)));
        if (cells.Contains(tile)) return true;
        var scannerTile = _maps.TileIndicesFor(grid, Comp<MapGridComponent>(grid), Transform(scanner).Coordinates);
        var offset = tile - scannerTile;
        if (offset.X * offset.X + offset.Y * offset.Y > 49) return false;
        return Transform(target).Anchored && cells.Contains(tile + Inward(Transform(target).LocalRotation));
    }

    public IEnumerable<Vector2i> ScannerCells(EntityUid scanner)
    {
        if (!TryGetScannerRoom(scanner, out var room, out _) || Transform(scanner).GridUid is not { } grid) yield break;
        if (_grids[grid].OpenAreas.TryGetValue(scanner, out var cells))
        {
            foreach (var cell in cells) yield return cell;
            yield break;
        }
        foreach (var cell in room.Tiles) yield return cell;
        foreach (var cell in room.Doors) yield return cell;
    }

    public bool ContainsTarget(EntityUid grid, KiasPhysicalRoom room, EntityUid target)
    {
        if (TerminatingOrDeleted(target) || Transform(target).GridUid != grid
            || !_grids.TryGetValue(grid, out var cache) || cache.Pending || !TryComp<MapGridComponent>(grid, out var map)) return false;
        var position = _transforms.ToCoordinates(grid, _transforms.GetMapCoordinates(target));
        var tile = _maps.TileIndicesFor(grid, map, position);
        if (cache.Geometry.Contains(room, tile)) return true;
        if (!Transform(target).Anchored || cache.Geometry.Interior.ContainsKey(tile)) return false;
        var inward = tile + Inward(Transform(target).LocalRotation);
        if (cache.Geometry.At(inward) is { } oriented) return oriented == room && oriented.Usable;
        KiasPhysicalRoom? only = null;
        foreach (var step in KiasRoomGeometry.Steps)
        {
            if (cache.Geometry.At(tile + step) is not { Usable: true } candidate) continue;
            if (only != null && only != candidate) return false;
            only = candidate;
        }
        return only == room;
    }

    public void GetScannersCovering(EntityUid grid, EntityUid target, KiasScannerModules modules, List<EntityUid> result)
    {
        result.Clear();
        if (!_grids.TryGetValue(grid, out var cache) || cache.Pending || TerminatingOrDeleted(target)
            || !TryComp<MapGridComponent>(grid, out var map)) return;
        foreach (var (scanner, region) in cache.Scanners)
            if (region.Status == KiasRoomStatus.ExteriorSector && Contains(scanner, target, modules)) result.Add(scanner);
        if (Transform(target).GridUid != grid) return;
        var tile = _maps.TileIndicesFor(grid, map, _transforms.ToCoordinates(grid, _transforms.GetMapCoordinates(target)));
        if (cache.Geometry.Interior.TryGetValue(tile, out var room)) Add(room);
        else if (cache.Geometry.Boundaries.TryGetValue(tile, out var adjacent))
        {
            foreach (var boundary in adjacent) Add(boundary);
        }
        void Add(KiasPhysicalRoom region)
        {
            if (!region.Usable) return;
            foreach (var scanner in region.Scanners)
                if (_kias.IsOnline(scanner) && TryComp<KiasRoomScannerComponent>(scanner, out var component)
                    && component.LifeStage <= ComponentLifeStage.Running && (component.Modules & modules) != 0
                    && (!cache.OpenAreas.TryGetValue(scanner, out var cells) || cells.Contains(tile))) result.Add(scanner);
        }
    }

    public Dictionary<Vector2i, List<EntityUid>> Coverage(EntityUid grid)
    {
        var result = new Dictionary<Vector2i, List<EntityUid>>();
        if (!_grids.TryGetValue(grid, out var cache) || cache.Pending) return result;
        foreach (var room in cache.Geometry.Rooms)
        {
            if (!room.Usable || room.Scanners.Count == 0) continue;
            if (room.Status == KiasRoomStatus.OpenToSpace)
            {
                foreach (var scanner in room.Scanners)
                foreach (var cell in cache.OpenAreas[scanner])
                {
                    if (!result.TryGetValue(cell, out var scanners)) result.Add(cell, scanners = new());
                    scanners.Add(scanner);
                }
                continue;
            }
            foreach (var tile in room.Tiles) result.Add(tile, room.Scanners);
        }
        foreach (var (tile, rooms) in cache.Geometry.Boundaries)
        {
            var scanners = new List<EntityUid>();
            foreach (var room in rooms)
            {
                if (!room.Usable) continue;
                foreach (var scanner in room.Scanners)
                    if (!cache.OpenAreas.TryGetValue(scanner, out var cells) || cells.Contains(tile)) scanners.Add(scanner);
            }
            if (scanners.Count > 0) result[tile] = scanners;
        }
        foreach (var (scanner, room) in cache.Scanners)
        {
            if (room.Status != KiasRoomStatus.ExteriorSector) continue;
            foreach (var cell in room.Tiles)
            {
                if (!result.TryGetValue(cell, out var scanners)) result.Add(cell, scanners = new());
                else
                    result[cell] = scanners = new List<EntityUid>(scanners);
                scanners.Add(scanner);
            }
        }
        return result;
    }

    public bool SharesRoom(EntityUid grid, EntityUid first, EntityUid second)
    {
        if (!_grids.TryGetValue(grid, out var cache) || cache.Pending) return false;
        foreach (var room in cache.Geometry.Rooms)
        {
            if (!ContainsTarget(grid, room, first) || !ContainsTarget(grid, room, second)) continue;
            foreach (var scanner in room.Scanners)
                if (_kias.IsOnline(scanner) && ContainsTargetForScanner(scanner, first)
                    && ContainsTargetForScanner(scanner, second)) return true;
        }
        return false;
    }
}
