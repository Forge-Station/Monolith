using System.Linq;
using Robust.Shared.Maths;

namespace Content.Server._WF.Planets.Flight;

/// <summary>Plan narrow cuts against actual tile connectivity, retaining a few large connected sections.</summary>
public static class WFCrashFractures
{
    private static readonly Vector2i[] Neighbours = { new(1, 0), new(-1, 0), new(0, 1), new(0, -1) };

    /// <summary>Returns the tiles to remove so the hull splits into 2 to 4 large sections.</summary>
    public static HashSet<Vector2i> Plan(HashSet<Vector2i> original, int targetParts, int seed = 0)
    {
        var random = new Random(seed);
        var phase = random.Next(12);
        var cuts = new HashSet<Vector2i>();
        if (original.Count < 24)
            return cuts;
        var remaining = new HashSet<Vector2i>(original);
        var minimumSection = Math.Max(6, original.Count / 30);
        var budget = Math.Max(1, original.Count / 8);
        targetParts = Math.Clamp(targetParts, 2, 4);
        while (true)
        {
            var sections = Sections(remaining);
            if (sections.Count >= targetParts)
                break;
            var largest = sections.OrderByDescending(s => s.Count).First();
            HashSet<Vector2i>? best = null;
            var bestLargest = double.PositiveInfinity;
            foreach (var axis in new[] { 2, 3, 0, 1 })
            {
                var min = largest.Min(t => Projection(t, axis));
                var max = largest.Max(t => Projection(t, axis));
                // A fixed small candidate count keeps capital-ship planning bounded.
                foreach (var fraction in new[] { 0.15f, 0.25f, 0.4f, 0.5f, 0.65f, 0.8f, 0.9f })
                {
                    var coordinate = min + (int) MathF.Round((max - min) * fraction);
                    // Cut the high side of a zig-zag line; the adjacency test closes the gaps at its turns.
                    bool LowSide(Vector2i tile) => Projection(tile, axis) <= coordinate + ZigZag(axis == 0 ? tile.Y : tile.X, phase);
                    var band = largest.Where(t => !LowSide(t) &&
                        Neighbours.Any(offset => largest.Contains(t + offset) && LowSide(t + offset))).ToHashSet();
                    if (band.Count == 0 || cuts.Count + band.Count > budget)
                        continue;
                    var trial = new HashSet<Vector2i>(remaining);
                    trial.ExceptWith(band);
                    var pieces = Sections(trial);
                    if (pieces.Count <= sections.Count || pieces.Count > targetParts || pieces.Any(p => p.Count < minimumSection))
                        continue;
                    // Prefer diagonal cuts when viable; vary the location rather than always bisecting the ship.
                    var biggest = (axis < 2 ? 2.0 : 0.0) + random.NextDouble();
                    if (biggest >= bestLargest)
                        continue;
                    bestLargest = biggest;
                    best = band;
                }
            }
            if (best == null)
                break;
            cuts.UnionWith(best);
            remaining.ExceptWith(best);
        }
        return cuts;
    }

    private static int ZigZag(int along, int phase) =>
        ((int) MathF.Floor((along + phase) / 3f) & 3) switch { 0 => -1, 2 => 1, _ => 0 };

    private static int Projection(Vector2i tile, int axis) =>
        axis switch { 0 => tile.X, 1 => tile.Y, 2 => tile.X + tile.Y, _ => tile.X - tile.Y };

    /// <summary>Splits tiles into 4-connected sections.</summary>
    public static List<HashSet<Vector2i>> Sections(HashSet<Vector2i> tiles)
    {
        var unseen = new HashSet<Vector2i>(tiles);
        var result = new List<HashSet<Vector2i>>();
        var queue = new Queue<Vector2i>();
        while (unseen.Count > 0)
        {
            var seed = unseen.First();
            unseen.Remove(seed);
            queue.Enqueue(seed);
            var section = new HashSet<Vector2i> { seed };
            while (queue.TryDequeue(out var tile))
                foreach (var offset in Neighbours)
                {
                    var next = tile + offset;
                    if (!unseen.Remove(next))
                        continue;
                    section.Add(next);
                    queue.Enqueue(next);
                }
            result.Add(section);
        }
        return result;
    }
}
