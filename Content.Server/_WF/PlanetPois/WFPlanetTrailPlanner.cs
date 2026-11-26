using System.Numerics;

namespace Content.Server._WF.PlanetPois;

/// <summary>Routes a wandering footpath from an off-road site to the nearest road.</summary>
public static class WFPlanetTrailPlanner
{
    /// <summary>Tiles across one patch of the wander field.</summary>
    private const int WanderScale = 6;

    /// <summary>The most a step can cost over a plain one, where the ground is roughest.</summary>
    private const float WanderCost = 2.5f;

    private static readonly Vector2i[] Steps = { new(1, 0), new(-1, 0), new(0, 1), new(0, -1) };

    /// <summary>
    /// The cheapest path from <paramref name="start"/> to any target tile, over ground whose cost wanders by
    /// <paramref name="seed"/> so the path bends like a worn one. Ends on the first target tile it reaches; null when none
    /// is within <paramref name="maxLength"/> steps.
    /// </summary>
    public static List<Vector2i>? Route(Vector2i start, IReadOnlySet<Vector2i> targets, Func<Vector2i, bool> passable,
        Vector2 centre, float radius, int maxLength, int seed)
    {
        if (targets.Count == 0 || maxLength < 1)
            return null;

        var from = new Dictionary<Vector2i, Vector2i>();
        var cost = new Dictionary<Vector2i, float> { [start] = 0f };
        var steps = new Dictionary<Vector2i, int> { [start] = 0 };
        var open = new PriorityQueue<Vector2i, float>();
        open.Enqueue(start, 0f);
        while (open.TryDequeue(out var tile, out var priority))
        {
            if (priority > cost[tile])
                continue;
            if (targets.Contains(tile))
                return Trace(from, start, tile);
            if (steps[tile] >= maxLength)
                continue;

            foreach (var step in Steps)
            {
                var next = tile + step;
                if (!targets.Contains(next) && (!passable(next) || Vector2.DistanceSquared(next, centre) >= radius * radius))
                    continue;
                var nextCost = cost[tile] + 1f + Wander(next, seed);
                if (cost.TryGetValue(next, out var known) && known <= nextCost)
                    continue;
                cost[next] = nextCost;
                steps[next] = steps[tile] + 1;
                from[next] = tile;
                open.Enqueue(next, nextCost);
            }
        }
        return null;
    }

    private static List<Vector2i> Trace(Dictionary<Vector2i, Vector2i> from, Vector2i start, Vector2i end)
    {
        var path = new List<Vector2i> { end };
        while (path[^1] != start)
            path.Add(from[path[^1]]);
        path.Reverse();
        return path;
    }

    /// <summary>A smooth extra cost from 0 to <see cref="WanderCost"/>, varying over a few tiles.</summary>
    private static float Wander(Vector2i tile, int seed)
    {
        var x = (float) tile.X / WanderScale;
        var y = (float) tile.Y / WanderScale;
        var x0 = (int) MathF.Floor(x);
        var y0 = (int) MathF.Floor(y);
        var fx = Smooth(x - x0);
        var fy = Smooth(y - y0);
        var bottom = Lerp(Corner(x0, y0, seed), Corner(x0 + 1, y0, seed), fx);
        var top = Lerp(Corner(x0, y0 + 1, seed), Corner(x0 + 1, y0 + 1, seed), fx);
        return Lerp(bottom, top, fy) * WanderCost;
    }

    private static float Smooth(float t) => t * t * (3f - 2f * t);

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    /// <summary>A repeatable value from 0 to 1 for one corner of the wander field.</summary>
    private static float Corner(int x, int y, int seed)
    {
        unchecked
        {
            var hash = (uint) (x * 73856093 ^ y * 19349663 ^ seed * 83492791);
            hash ^= hash >> 13;
            hash *= 0x5bd1e995;
            hash ^= hash >> 15;
            return (hash & 0xFFFF) / 65535f;
        }
    }
}
