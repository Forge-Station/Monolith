using System.Linq;
using Content.Server.Atmos.Components;
using Content.Shared._Forge.KIAS;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Server._Forge.KIAS;

public sealed class KiasDeviceIdentitySystem : EntitySystem
{
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private IGameTiming _timing = default!;
    private readonly Dictionary<string, EntityUid> _identifiers = new(StringComparer.Ordinal);
    private readonly Dictionary<EntityUid, (TimeSpan Until, Dictionary<Vector2i, int> Rooms)> _rooms = new();
    private static readonly Vector2i[] Neighbors = { new(0, -1), new(1, 0), new(0, 1), new(-1, 0) };

    public override void Initialize()
    {
        SubscribeLocalEvent<GridRemovalEvent>(OnGridRemoved);
    }

    public void Release(Entity<KiasDeviceComponent> ent)
    {
        if (_identifiers.GetValueOrDefault(ent.Comp.Identifier) == ent.Owner) _identifiers.Remove(ent.Comp.Identifier);
    }
    private void OnGridRemoved(GridRemovalEvent args) => _rooms.Remove(args.EntityUid);

    public string Identifier(EntityUid uid)
    {
        var device = Comp<KiasDeviceComponent>(uid);
        if (device.Identifier.Length == 0 || _identifiers.TryGetValue(device.Identifier, out var owner) && owner != uid)
        {
            do { device.Identifier = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(); }
            while (_identifiers.ContainsKey(device.Identifier));
        }
        _identifiers[device.Identifier] = uid;
        return device.Identifier;
    }

    public string Label(EntityUid uid) => HasComp<KiasDeviceComponent>(uid) ? $"{Name(uid)} · #{Identifier(uid)}" : Name(uid);

    public Dictionary<EntityUid, (string Label, bool Named, int Order)> Rooms(EntityUid grid, IEnumerable<EntityUid> devices)
    {
        var result = new Dictionary<EntityUid, (string, bool, int)>();
        if (!TryComp<MapGridComponent>(grid, out var map)) return result;
        var positions = devices.Distinct().ToDictionary(uid => uid, uid =>
        {
            var transform = Transform(uid);
            var tile = _map.TileIndicesFor(grid, map, transform.Coordinates);
            var anchors = _map.GetAnchoredEntities(grid, map, tile);
            var onWall = false;
            while (anchors.MoveNext(out var anchored)) onWall |= HasComp<AirtightComponent>(anchored);
            if (onWall && HasComp<Content.Shared.Wall.WallMountComponent>(uid))
            {
                var facing = transform.LocalRotation.RotateVec(new System.Numerics.Vector2(0, -1));
                var neighbor = tile + new Vector2i((int) MathF.Round(facing.X), (int) MathF.Round(facing.Y));
                if (!_map.GetTileRef(grid, map, neighbor).Tile.IsEmpty) tile = neighbor;
            }
            return tile;
        });
        if (!_rooms.TryGetValue(grid, out var cached) || _timing.CurTime >= cached.Until
            || positions.Values.Any(tile => !cached.Rooms.ContainsKey(tile)))
        {
            var rooms = new Dictionary<Vector2i, int>();
            var blocked = new Dictionary<Vector2i, bool>();
            bool Blocked(Vector2i tile)
            {
                if (blocked.TryGetValue(tile, out var value)) return value;
                value = _map.GetTileRef(grid, map, tile).Tile.IsEmpty;
                var anchors = _map.GetAnchoredEntities(grid, map, tile);
                while (!value && anchors.MoveNext(out var anchored))
                    value = HasComp<AirtightComponent>(anchored);
                blocked[tile] = value;
                return value;
            }
            var nextRoom = 1;
            var budget = 16384;
            foreach (var seed in positions.Values.Distinct().OrderBy(tile => tile.Y).ThenBy(tile => tile.X))
            {
                if (rooms.ContainsKey(seed)) continue;
                var start = seed;
                if (Blocked(start))
                {
                    var neighbor = Neighbors.Select(offset => seed + offset)
                        .FirstOrDefault(tile => !Blocked(tile), seed);
                    start = neighbor;
                }
                if (rooms.TryGetValue(start, out var existing)) { rooms[seed] = existing; continue; }
                var number = nextRoom++;
                var pending = new Queue<Vector2i>();
                rooms[start] = number;
                if (!Blocked(start)) pending.Enqueue(start);
                while (budget-- > 0 && pending.TryDequeue(out var tile))
                {
                    foreach (var offset in Neighbors)
                    {
                        var neighbor = tile + offset;
                        if (rooms.ContainsKey(neighbor) || Blocked(neighbor)) continue;
                        rooms[neighbor] = number; pending.Enqueue(neighbor);
                    }
                }
                rooms[seed] = number;
            }
            cached = (_timing.CurTime + TimeSpan.FromSeconds(2), rooms);
            _rooms[grid] = cached;
        }
        var names = new Dictionary<int, string>();
        foreach (var (uid, tile) in positions.OrderByDescending(pair => HasComp<KiasRoomScannerComponent>(pair.Key)).ThenBy(pair => Identifier(pair.Key), StringComparer.Ordinal))
            if (Comp<KiasDeviceComponent>(uid).Room.Trim() is { Length: > 0 } name) names.TryAdd(cached.Rooms[tile], name);
        foreach (var (uid, tile) in positions)
        {
            var own = Comp<KiasDeviceComponent>(uid).Room.Trim();
            var label = own.Length > 0 ? own : names.GetValueOrDefault(cached.Rooms[tile]);
            result[uid] = label is { Length: > 0 } ? (label, true, cached.Rooms[tile]) : (Loc.GetString("kias-room-number", ("number", cached.Rooms[tile])), false, cached.Rooms[tile]);
        }
        return result;
    }
}
