using System.Linq;
using Content.Shared._WF.PlanetMap;

namespace Content.Server._WF.PlanetMap;

/// <summary>Turns sets of road tiles into a few simplified polylines.</summary>
public static class WFPlanetMapGeometry
{
    private static readonly Vector2i[] Neighbours =
    {
        new(1, 0), new(1, 1), new(0, 1), new(-1, 1), new(-1, 0), new(-1, -1), new(0, -1), new(1, -1),
    };

    /// <summary>
    /// Every line of a set of tiles, thinned to one tile wide and simplified, split wherever <paramref name="tunnel"/>
    /// changes; drops tolerance until all of them fit in <paramref name="maxPoints"/>, then the shortest.
    /// </summary>
    public static List<WFPlanetMapLine> Lines(IReadOnlyCollection<Vector2i> tiles, WFPlanetMapLineKind kind,
        Func<Vector2i, bool>? tunnel, int maxPoints, float tolerance = 1f)
    {
        var chains = Chains(Thin(tiles));
        var lines = new List<WFPlanetMapLine>();
        if (maxPoints < 2)
            return lines;

        for (var attempt = 0; attempt < 8; attempt++, tolerance *= 2f)
        {
            lines.Clear();
            foreach (var chain in chains)
            foreach (var (inTunnel, run) in Split(chain, tunnel))
            {
                var points = Simplify(run, tolerance);
                if (points.Count >= 2)
                    lines.Add(new WFPlanetMapLine(inTunnel ? WFPlanetMapLineKind.Tunnel : kind, points.ToArray()));
            }

            if (lines.Sum(line => line.Points.Length) <= maxPoints)
                return lines;
        }

        // Still too many: keep the longest lines that fit.
        var kept = new List<WFPlanetMapLine>();
        var total = 0;
        foreach (var line in lines.OrderByDescending(Length))
        {
            if (total + line.Points.Length > maxPoints)
                continue;
            kept.Add(line);
            total += line.Points.Length;
        }

        return kept;
    }

    private static float Length(WFPlanetMapLine line)
    {
        var length = 0f;
        for (var i = 1; i < line.Points.Length; i++)
            length += (line.Points[i] - line.Points[i - 1]).Length;
        return length;
    }

    /// <summary>Zhang-Suen thinning: strips a blob of tiles to its one-tile-wide skeleton, keeping ends and joins.</summary>
    public static HashSet<Vector2i> Thin(IEnumerable<Vector2i> tiles)
    {
        var set = new HashSet<Vector2i>(tiles);
        var remove = new List<Vector2i>();
        var on = new bool[8];
        for (var pass = 0; pass < 64; pass++)
        {
            var changed = false;
            for (var step = 0; step < 2; step++)
            {
                remove.Clear();
                foreach (var tile in set)
                {
                    // P2..P9 clockwise from north, as the algorithm names them.
                    on[0] = set.Contains(tile + new Vector2i(0, 1));
                    on[1] = set.Contains(tile + new Vector2i(1, 1));
                    on[2] = set.Contains(tile + new Vector2i(1, 0));
                    on[3] = set.Contains(tile + new Vector2i(1, -1));
                    on[4] = set.Contains(tile + new Vector2i(0, -1));
                    on[5] = set.Contains(tile + new Vector2i(-1, -1));
                    on[6] = set.Contains(tile + new Vector2i(-1, 0));
                    on[7] = set.Contains(tile + new Vector2i(-1, 1));

                    var count = 0;
                    var transitions = 0;
                    for (var i = 0; i < 8; i++)
                    {
                        if (on[i])
                            count++;
                        if (!on[i] && on[(i + 1) % 8])
                            transitions++;
                    }

                    if (count < 2 || count > 6 || transitions != 1)
                        continue;

                    var deletable = step == 0
                        ? !(on[0] && on[2] && on[4]) && !(on[2] && on[4] && on[6])
                        : !(on[0] && on[2] && on[6]) && !(on[0] && on[4] && on[6]);
                    if (deletable)
                        remove.Add(tile);
                }

                foreach (var tile in remove)
                    set.Remove(tile);
                changed |= remove.Count > 0;
            }

            if (!changed)
                break;
        }

        return set;
    }

    /// <summary>
    /// Every path between ends and junctions of an eight-connected skeleton, and every closed loop. A diagonal step
    /// is skipped where a straight pair already joins the two tiles.
    /// </summary>
    public static List<List<Vector2i>> Chains(HashSet<Vector2i> tiles)
    {
        var adjacency = new Dictionary<Vector2i, List<Vector2i>>();
        foreach (var tile in tiles.OrderBy(t => t.X).ThenBy(t => t.Y))
        {
            var list = new List<Vector2i>();
            foreach (var delta in Neighbours)
            {
                var next = tile + delta;
                if (!tiles.Contains(next))
                    continue;
                if (delta.X != 0 && delta.Y != 0
                    && (tiles.Contains(tile + new Vector2i(delta.X, 0)) || tiles.Contains(tile + new Vector2i(0, delta.Y))))
                    continue;
                list.Add(next);
            }
            adjacency[tile] = list;
        }

        var chains = new List<List<Vector2i>>();
        var walked = new HashSet<(Vector2i, Vector2i)>();

        bool Walked(Vector2i a, Vector2i b) => walked.Contains(Key(a, b));

        List<Vector2i> Walk(Vector2i start, Vector2i first)
        {
            var chain = new List<Vector2i> { start };
            var previous = start;
            var current = first;
            walked.Add(Key(start, first));
            chain.Add(current);
            while (current != start && adjacency[current].Count == 2)
            {
                var next = adjacency[current][0] == previous ? adjacency[current][1] : adjacency[current][0];
                if (Walked(current, next))
                    break;
                walked.Add(Key(current, next));
                previous = current;
                current = next;
                chain.Add(current);
            }

            return chain;
        }

        foreach (var (tile, list) in adjacency)
        {
            if (list.Count == 2)
                continue;
            foreach (var next in list)
            {
                if (!Walked(tile, next))
                    chains.Add(Walk(tile, next));
            }
            if (list.Count == 0)
                chains.Add(new List<Vector2i> { tile });
        }

        // Whatever is left is a closed loop with no ends.
        foreach (var (tile, list) in adjacency)
        {
            foreach (var next in list)
            {
                if (!Walked(tile, next))
                    chains.Add(Walk(tile, next));
            }
        }

        return chains;
    }

    private static (Vector2i, Vector2i) Key(Vector2i a, Vector2i b)
    {
        return a.X < b.X || a.X == b.X && a.Y <= b.Y ? (a, b) : (b, a);
    }

    /// <summary>Cuts a chain into runs inside and outside tunnels; neighbouring runs share their end point.</summary>
    private static IEnumerable<(bool Tunnel, List<Vector2i> Run)> Split(List<Vector2i> chain, Func<Vector2i, bool>? tunnel)
    {
        if (tunnel == null || chain.Count == 0)
        {
            yield return (false, chain);
            yield break;
        }

        var run = new List<Vector2i> { chain[0] };
        var inside = tunnel(chain[0]);
        for (var i = 1; i < chain.Count; i++)
        {
            var here = tunnel(chain[i]);
            run.Add(chain[i]);
            if (here == inside)
                continue;
            yield return (inside, run);
            run = new List<Vector2i> { chain[i] };
            inside = here;
        }

        yield return (inside, run);
    }

    /// <summary>Ramer-Douglas-Peucker: keeps both ends and every point further than <paramref name="tolerance"/> from the simplified line.</summary>
    public static List<Vector2i> Simplify(IReadOnlyList<Vector2i> points, float tolerance)
    {
        if (points.Count <= 2)
            return points.ToList();

        var keep = new bool[points.Count];
        keep[0] = keep[^1] = true;
        var stack = new Stack<(int, int)>();
        stack.Push((0, points.Count - 1));
        while (stack.Count > 0)
        {
            var (first, last) = stack.Pop();
            var furthest = -1;
            var distance = tolerance;
            for (var i = first + 1; i < last; i++)
            {
                var d = Distance(points[i], points[first], points[last]);
                if (d <= distance)
                    continue;
                distance = d;
                furthest = i;
            }

            if (furthest < 0)
                continue;
            keep[furthest] = true;
            stack.Push((first, furthest));
            stack.Push((furthest, last));
        }

        var result = new List<Vector2i>();
        for (var i = 0; i < points.Count; i++)
        {
            if (keep[i])
                result.Add(points[i]);
        }

        return result;
    }

    /// <summary>The distance from a point to the segment a-b.</summary>
    private static float Distance(Vector2i point, Vector2i a, Vector2i b)
    {
        var ab = (System.Numerics.Vector2) (b - a);
        var ap = (System.Numerics.Vector2) (point - a);
        var length = ab.LengthSquared();
        if (length <= 0f)
            return ap.Length();
        var t = Math.Clamp(System.Numerics.Vector2.Dot(ap, ab) / length, 0f, 1f);
        return (ap - ab * t).Length();
    }
}
