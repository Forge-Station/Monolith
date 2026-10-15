using Content.Shared.Atmos;

namespace Content.Server._Forge.KIAS;

public enum KiasRoomStatus : byte
{
    Ok, NoInteriorSeed, OpenToSpace, UnsupportedGeometry, RoomTooLarge, RebuildPending, AmbiguousBoundary, ExteriorSector,
}

public sealed class KiasPhysicalRoom
{
    public int Id;
    public KiasRoomStatus Status;
    public readonly HashSet<Vector2i> Tiles = new();
    public readonly HashSet<Vector2i> Doors = new();
    public readonly List<EntityUid> Scanners = new();
    public bool Usable => Status is KiasRoomStatus.Ok or KiasRoomStatus.OpenToSpace or KiasRoomStatus.ExteriorSector;
}

public sealed class KiasRoomGeometry
{
    public static readonly Vector2i[] Steps = { new(0, 1), new(1, 0), new(0, -1), new(-1, 0) };
    public static readonly AtmosDirection[] Directions = { AtmosDirection.North, AtmosDirection.East, AtmosDirection.South, AtmosDirection.West };
    public readonly Dictionary<Vector2i, AtmosDirection> Floors = new();
    public readonly HashSet<Vector2i> DoorTiles = new();
    public readonly HashSet<Vector2i> SpaceDeck = new();
    public readonly Dictionary<Vector2i, KiasPhysicalRoom> Interior = new();
    public readonly Dictionary<Vector2i, List<KiasPhysicalRoom>> Boundaries = new();
    public readonly List<KiasPhysicalRoom> Rooms = new();
    public long Visited;

    public IEnumerable<bool> Build(int maximumRoomTiles = 4096)
    {
        var frontier = new Queue<Vector2i>();
        foreach (var origin in Floors.Keys)
        {
            yield return true;
            if (Interior.ContainsKey(origin) || DoorTiles.Contains(origin) || Floors[origin] == AtmosDirection.All) continue;
            var room = new KiasPhysicalRoom { Id = Rooms.Count + 1 };
            Rooms.Add(room);
            Interior.Add(origin, room);
            frontier.Enqueue(origin);
            while (frontier.TryDequeue(out var cell))
            {
                Visited++;
                room.Tiles.Add(cell);
                if (room.Tiles.Count > maximumRoomTiles) room.Status = KiasRoomStatus.RoomTooLarge;
                for (var direction = 0; direction < 4; direction++)
                {
                    if ((Floors[cell] & Directions[direction]) != 0) continue;
                    var next = cell + Steps[direction];
                    if (DoorTiles.Contains(next))
                    {
                        room.Doors.Add(next);
                        if (!Boundaries.TryGetValue(next, out var adjacent)) Boundaries.Add(next, adjacent = new());
                        if (!adjacent.Contains(room)) adjacent.Add(room);
                        continue;
                    }
                    if (!Floors.TryGetValue(next, out var edges))
                    {
                        if (room.Status == KiasRoomStatus.Ok) room.Status = KiasRoomStatus.OpenToSpace;
                        continue;
                    }
                    if ((edges & Directions[(direction + 2) % 4]) != 0 || Interior.ContainsKey(next)) continue;
                    Interior.Add(next, room);
                    frontier.Enqueue(next);
                }
                yield return true;
            }
        }
    }

    public KiasPhysicalRoom? At(Vector2i cell) => Interior.GetValueOrDefault(cell);

    public HashSet<Vector2i> OpenCoverage(KiasPhysicalRoom room, Vector2i seed, Vector2i scanner, int radius = 7)
    {
        var result = new HashSet<Vector2i>();
        var frontier = new Queue<Vector2i>();
        var seedOffset = seed - scanner;
        if (At(seed) != room || seedOffset.X * seedOffset.X + seedOffset.Y * seedOffset.Y > radius * radius) return result;
        result.Add(seed);
        frontier.Enqueue(seed);
        while (frontier.TryDequeue(out var cell))
        {
            for (var direction = 0; direction < 4; direction++)
            {
                if ((Floors[cell] & Directions[direction]) != 0) continue;
                var next = cell + Steps[direction];
                var offset = next - scanner;
                if (offset.X * offset.X + offset.Y * offset.Y > radius * radius || result.Contains(next)) continue;
                if (DoorTiles.Contains(next)) { result.Add(next); continue; }
                if (At(next) != room || (Floors[next] & Directions[(direction + 2) % 4]) != 0) continue;
                result.Add(next);
                frontier.Enqueue(next);
            }
        }
        return result;
    }

    public Vector2i ScannerSeed(Vector2i tile, Vector2i inward)
    {
        var seed = tile + inward;
        if (!DoorTiles.Contains(tile)) return seed;
        // Выходим из собственного дверного проёма в сторону сканера.
        for (var i = 0; i < 64 && DoorTiles.Contains(seed); i++) seed += inward;
        return seed;
    }

    public bool IsExteriorDeck(KiasPhysicalRoom room, Vector2i scanner, Vector2i outward)
    {
        if (room.Status != KiasRoomStatus.OpenToSpace || room.Tiles.Count > 256) return false;
        foreach (var cell in room.Tiles)
        {
            var delta = cell - scanner;
            if (delta.X * outward.X + delta.Y * outward.Y <= 0) return false;
            var next = cell;
            var escaped = false;
            for (var distance = 0; distance < 32; distance++)
            {
                next += outward;
                if (DoorTiles.Contains(next)) return false;
                if (!Floors.TryGetValue(next, out var blocked)) { escaped = true; break; }
                if (blocked != AtmosDirection.Invalid) return false;
            }
            if (!escaped) return false;
        }
        return room.Tiles.Count > 0;
    }

    public HashSet<Vector2i> ExteriorCoverage(Vector2i scanner, Vector2i outward, int reach, IReadOnlySet<Vector2i>? deck = null)
    {
        var result = new HashSet<Vector2i>();
        reach = Math.Clamp(reach, 1, 32);
        if (!Floors.TryGetValue(scanner, out var wall) || wall != AtmosDirection.All
            || Blocked(scanner + outward)) return result;
        for (var x = -reach; x <= reach; x++)
        for (var y = -reach; y <= reach; y++)
        {
            if (x * x + y * y > reach * reach || x * outward.X + y * outward.Y <= 0) continue;
            var target = scanner + new Vector2i(x, y);
            if (Blocked(target) || !Visible(target)) continue;
            result.Add(target);
        }
        return result;

        bool Visible(Vector2i target)
        {
            var cell = scanner;
            var dx = Math.Abs(target.X - scanner.X);
            var dy = Math.Abs(target.Y - scanner.Y);
            var sx = Math.Sign(target.X - scanner.X);
            var sy = Math.Sign(target.Y - scanner.Y);
            var ix = 0;
            var iy = 0;
            while (ix < dx || iy < dy)
            {
                var crossing = (1 + 2 * ix) * dy - (1 + 2 * iy) * dx;
                if (crossing == 0)
                {
                    if (Blocked(cell + new Vector2i(sx, 0)) || Blocked(cell + new Vector2i(0, sy))) return false;
                    cell += new Vector2i(sx, sy); ix++; iy++;
                }
                else if (crossing < 0) { cell += new Vector2i(sx, 0); ix++; }
                else { cell += new Vector2i(0, sy); iy++; }
                if (Blocked(cell)) return false;
            }
            return true;
        }
        bool Blocked(Vector2i cell) => DoorTiles.Contains(cell)
            || Floors.TryGetValue(cell, out var edges) && (edges != AtmosDirection.Invalid
                || !SpaceDeck.Contains(cell) && deck?.Contains(cell) != true);
    }

    public bool Contains(KiasPhysicalRoom room, Vector2i cell) => room.Usable &&
        (Interior.GetValueOrDefault(cell) == room || Boundaries.TryGetValue(cell, out var rooms) && rooms.Contains(room));
}
