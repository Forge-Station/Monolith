namespace Content.Shared._Forge.KIAS;

public sealed class KiasTopology
{
    private static readonly Vector2i[] Neighbours =
    {
        new(1, 0), new(-1, 0), new(0, 1), new(0, -1),
    };

    private readonly Dictionary<Vector2i, int> _segments = new();
    private readonly Dictionary<Vector2i, HashSet<int>> _coverage = new();

    public int SegmentCount { get; private set; }

    public void Rebuild(IEnumerable<Vector2i> cableTiles)
    {
        _segments.Clear();
        _coverage.Clear();
        SegmentCount = 0;
        var remaining = new HashSet<Vector2i>(cableTiles);
        var queue = new Queue<Vector2i>();
        while (remaining.Count > 0)
        {
            using var iterator = remaining.GetEnumerator();
            iterator.MoveNext();
            var start = iterator.Current;
            remaining.Remove(start);
            queue.Enqueue(start);
            var segment = SegmentCount++;
            while (queue.TryDequeue(out var tile))
            {
                _segments.Add(tile, segment);
                for (var x = -2; x <= 2; x++)
                for (var y = -2; y <= 2; y++)
                {
                    if (x * x + y * y > 4)
                        continue;
                    var serviced = tile + new Vector2i(x, y);
                    if (!_coverage.TryGetValue(serviced, out var networks))
                        _coverage.Add(serviced, networks = new HashSet<int>());
                    networks.Add(segment);
                }

                foreach (var offset in Neighbours)
                {
                    var next = tile + offset;
                    if (remaining.Remove(next))
                        queue.Enqueue(next);
                }
            }
        }
    }

    public bool Connected(Vector2i coreTile, Vector2i deviceTile)
    {
        return _coverage.TryGetValue(coreTile, out var coreNetworks)
               && _coverage.TryGetValue(deviceTile, out var deviceNetworks)
               && coreNetworks.Overlaps(deviceNetworks);
    }

    public List<Vector2i> DiagnosticPath(Vector2i coreTile, Vector2i deviceTile)
    {
        var parents = new Dictionary<Vector2i, Vector2i?>();
        var queue = new Queue<Vector2i>();
        foreach (var tile in _segments.Keys)
        {
            if ((tile - coreTile).LengthSquared > 4) continue;
            parents[tile] = null;
            queue.Enqueue(tile);
        }
        while (parents.Count <= 4096 && queue.TryDequeue(out var tile))
        {
            if ((tile - deviceTile).LengthSquared <= 4)
            {
                var path = new List<Vector2i>();
                Vector2i? current = tile;
                while (current is { } step) { path.Add(step); current = parents[step]; }
                path.Reverse();
                return path;
            }
            foreach (var offset in Neighbours)
            {
                var next = tile + offset;
                if (_segments.ContainsKey(next) && parents.TryAdd(next, tile)) queue.Enqueue(next);
            }
        }
        return new List<Vector2i>();
    }
}
