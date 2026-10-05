using System.Linq;
using System.Numerics;
using Content.Shared._WF.Caverns;

namespace Content.Server._WF.Caverns;

/// <summary>A mouth's hole, lip, climb tile and rim decor spots as offsets from its anchor, grown from one seed.</summary>
// Pure: the same spec and seed give the same shape in every process, so a cell's site never depends on claim timing.
public sealed class WFCavernMouthShape
{
    /// <summary>Holes grown before the fallback disc or staircase stands in for one that fits the spec.</summary>
    public const int Attempts = 16;

    /// <summary>How many tiles the fallback disc reaches from its centre, at most.</summary>
    private const int FallbackReach = 8;

    private static readonly Vector2i[] Cardinals = { new(0, 1), new(1, 0), new(0, -1), new(-1, 0) };

    private static readonly Vector2i[] Diagonals = { new(1, 1), new(-1, 1) };

    /// <summary>The hole's tiles. The anchor, (0, 0), is the one nearest its centroid.</summary>
    public readonly HashSet<Vector2i> Hole = new();

    /// <summary>The lip: every tile touching the hole, diagonals included, that is not hole.</summary>
    public readonly HashSet<Vector2i> Ring = new();

    /// <summary>Lip tiles that take rim decor, never on or beside the climb tile, nor beside each other.</summary>
    public readonly List<Vector2i> Rim = new();

    /// <summary>The lip tile over the climb point: outermost on the climb side, beside the hole.</summary>
    public Vector2i Climb;

    /// <summary>The hole's lowest corner tile.</summary>
    public Vector2i Min;

    /// <summary>The hole's highest corner tile.</summary>
    public Vector2i Max;

    /// <summary>The mean of the hole's tile centres, relative to the anchor tile's bottom-left corner.</summary>
    public Vector2 Centroid;

    /// <summary>Whether every attempt missed the spec and the fallback shape stands in.</summary>
    public bool Fallback;

    /// <summary>Grows the hole a spec describes from a seed, then finds its lip, climb tile and rim spots.</summary>
    public static WFCavernMouthShape Generate(WFCavernMouthSpec spec, int seed)
    {
        var random = new System.Random(seed);
        var tiles = new HashSet<Vector2i>();
        var ends = new HashSet<Vector2i>();
        var grown = false;

        for (var attempt = 0; attempt < Attempts && !grown; attempt++)
        {
            tiles.Clear();
            ends.Clear();

            if (spec.Style == WFCavernMouthStyle.Rift)
                GrowRift(spec, random, tiles, ends);
            else
            {
                GrowBlob(spec, random, tiles);
                Smooth(tiles);
            }

            Tidy(tiles, ends);
            grown = Fits(spec, tiles);
        }

        if (!grown)
            FallbackShape(spec, tiles);

        var shape = new WFCavernMouthShape { Fallback = !grown };
        shape.Place(tiles);
        shape.FindClimb(spec.ClimbSide);
        shape.PickRim(spec, random);
        return shape;
    }

    /// <summary>A seed for the mouth anchored at a tile, from the ground's seed. Plain arithmetic, never HashCode.Combine.</summary>
    public static int SeedAt(int groundSeed, Vector2i tile)
    {
        return unchecked(groundSeed * 6007 + tile.X * 83492791 + tile.Y * 2654435);
    }

    /// <summary>Every tile within <paramref name="radius"/> of a hole tile in either axis: the cavern pad.</summary>
    public HashSet<Vector2i> Pad(int radius)
    {
        var pad = new HashSet<Vector2i>();

        foreach (var tile in Hole)
        {
            for (var x = -radius; x <= radius; x++)
            for (var y = -radius; y <= radius; y++)
            {
                pad.Add(tile + new Vector2i(x, y));
            }
        }

        return pad;
    }

    /// <summary>An ellipse of about the target area, turned at random, whose edge wanders by the spec's roughness.</summary>
    private static void GrowBlob(WFCavernMouthSpec spec, System.Random random, HashSet<Vector2i> tiles)
    {
        var target = random.Next(spec.MinTiles, spec.MaxTiles + 1);
        var stretch = 1f + (float) random.NextDouble() * Math.Max(0f, spec.Elongation - 1f);
        var angle = (float) (random.NextDouble() * Math.PI);
        var major = MathF.Sqrt(target / MathF.PI * stretch);
        var minor = MathF.Sqrt(target / MathF.PI / stretch);
        var centre = new Vector2((float) random.NextDouble() - 0.5f, (float) random.NextDouble() - 0.5f);

        // Three harmonics with random phases and weights make the edge wander without breaking it up.
        var phases = new float[3];
        var weights = new float[3];
        var total = 0f;
        for (var i = 0; i < 3; i++)
        {
            phases[i] = (float) (random.NextDouble() * Math.PI * 2);
            weights[i] = 0.5f + (float) random.NextDouble() * 0.5f;
            total += weights[i];
        }

        var cos = MathF.Cos(angle);
        var sin = MathF.Sin(angle);
        var reach = (int) MathF.Ceiling(major * (1f + spec.Roughness)) + 1;

        for (var x = -reach; x <= reach; x++)
        for (var y = -reach; y <= reach; y++)
        {
            var point = new Vector2(x + 0.5f, y + 0.5f) - centre;
            var u = (point.X * cos + point.Y * sin) / major;
            var v = (point.Y * cos - point.X * sin) / minor;
            var theta = MathF.Atan2(v, u);

            var wander = 0f;
            for (var i = 0; i < 3; i++)
            {
                wander += weights[i] * MathF.Sin((i + 2) * theta + phases[i]);
            }

            if (MathF.Sqrt(u * u + v * v) <= 1f + spec.Roughness * wander / total)
                tiles.Add(new Vector2i(x, y));
        }
    }

    /// <summary>One majority pass over the hole and its lip: lone bumps fill out and lone notches close, so the edge reads round.</summary>
    private static void Smooth(HashSet<Vector2i> tiles)
    {
        if (tiles.Count == 0)
            return;

        Bounds(tiles, out var min, out var max);
        var next = new HashSet<Vector2i>();

        for (var x = min.X - 1; x <= max.X + 1; x++)
        for (var y = min.Y - 1; y <= max.Y + 1; y++)
        {
            var tile = new Vector2i(x, y);
            var around = 0;
            for (var dx = -1; dx <= 1; dx++)
            for (var dy = -1; dy <= 1; dy++)
            {
                if ((dx != 0 || dy != 0) && tiles.Contains(tile + new Vector2i(dx, dy)))
                    around++;
            }

            if (around >= 5 || (tiles.Contains(tile) && around >= 3))
                next.Add(tile);
        }

        tiles.Clear();
        tiles.UnionWith(next);
    }

    /// <summary>A crack walked tile by tile along a heading that drifts but never turns back, one or two tiles wide.</summary>
    private static void GrowRift(WFCavernMouthSpec spec, System.Random random, HashSet<Vector2i> tiles, HashSet<Vector2i> ends)
    {
        var target = random.Next(spec.MinTiles, spec.MaxTiles + 1);
        var angle = (float) (random.NextDouble() * Math.PI * 2);
        var heading = angle;
        var across = MathF.Abs(MathF.Cos(angle)) >= MathF.Abs(MathF.Sin(angle)) ? new Vector2i(0, 1) : new Vector2i(1, 0);
        var position = new Vector2(0.5f, 0.5f);
        var current = Vector2i.Zero;
        var wide = false;

        tiles.Add(current);
        ends.Add(current);

        for (var step = 0; step < target * 4 && tiles.Count < target; step++)
        {
            heading = Math.Clamp(heading + ((float) random.NextDouble() - 0.5f) * 0.7f, angle - 0.6f, angle + 0.6f);
            position += new Vector2(MathF.Cos(heading), MathF.Sin(heading));

            var next = new Vector2i((int) MathF.Floor(position.X), (int) MathF.Floor(position.Y));
            if (next == current)
                continue;

            // A diagonal step gets an elbow, so the crack stays joined edge to edge.
            if (next.X != current.X && next.Y != current.Y)
                tiles.Add(random.Next(2) == 0 ? new Vector2i(current.X, next.Y) : new Vector2i(next.X, current.Y));

            tiles.Add(next);
            current = next;

            if (spec.RiftWidth >= 2)
                wide = wide ? random.NextDouble() >= 0.45 : random.NextDouble() < 0.3;

            if (wide)
                tiles.Add(next + across);
        }

        ends.Add(current);
    }

    /// <summary>
    /// Whether a grown hole is usable: in the size range, not a plain rectangle, joined edge to edge, with no tile
    /// hanging on by one edge (but a rift's two ends), no two tiles touching only at a corner and no enclosed ground.
    /// </summary>
    // Tidy stops after a few passes, so its rules are checked again here rather than trusted.
    private static bool Fits(WFCavernMouthSpec spec, IReadOnlySet<Vector2i> tiles)
    {
        if (tiles.Count < spec.MinTiles || tiles.Count > spec.MaxTiles || tiles.Count == 0 || IsRectangle(tiles))
            return false;

        var start = tiles.OrderBy(t => t.Y).ThenBy(t => t.X).First();
        if (Flood(tiles, start, new HashSet<Vector2i>()).Count != tiles.Count)
            return false;

        var ends = tiles.Count(tile => Neighbours(tiles, tile) < 2);
        if (ends > (spec.Style == WFCavernMouthStyle.Rift ? 2 : 0))
            return false;

        foreach (var tile in tiles)
        {
            foreach (var diagonal in Diagonals)
            {
                if (tiles.Contains(tile + diagonal) && !tiles.Contains(tile + new Vector2i(diagonal.X, 0))
                    && !tiles.Contains(tile + new Vector2i(0, 1)))
                    return false;
            }
        }

        Bounds(tiles, out var min, out var max);
        min -= Vector2i.One;
        max += Vector2i.One;

        var ground = new HashSet<Vector2i>();
        for (var x = min.X; x <= max.X; x++)
        for (var y = min.Y; y <= max.Y; y++)
        {
            if (!tiles.Contains(new Vector2i(x, y)))
                ground.Add(new Vector2i(x, y));
        }

        return Flood(ground, min, new HashSet<Vector2i>()).Count == ground.Count;
    }

    /// <summary>Whether a tile set fills its bounding box exactly.</summary>
    private static bool IsRectangle(IReadOnlyCollection<Vector2i> tiles)
    {
        if (tiles.Count == 0)
            return false;

        Bounds(tiles, out var min, out var max);
        return tiles.Count == (max.X - min.X + 1) * (max.Y - min.Y + 1);
    }

    /// <summary>
    /// The shape for a seed whose every attempt missed: a rift's diagonal staircase of the minimum length, or the
    /// smallest digital disc that holds the minimum and is not a square.
    /// </summary>
    private static void FallbackShape(WFCavernMouthSpec spec, HashSet<Vector2i> tiles)
    {
        tiles.Clear();
        var min = Math.Max(1, spec.MinTiles);

        if (spec.Style == WFCavernMouthStyle.Rift)
        {
            var step = Vector2i.Zero;
            for (var i = 0; i < min; i++)
            {
                tiles.Add(step);
                step += i % 2 == 0 ? new Vector2i(1, 0) : new Vector2i(0, 1);
            }

            return;
        }

        // Tile centres within a growing radius of the corner the four middle tiles share.
        var limits = new SortedSet<float>();
        for (var i = 0; i < FallbackReach; i++)
        for (var j = 0; j < FallbackReach; j++)
        {
            limits.Add((i + 0.5f) * (i + 0.5f) + (j + 0.5f) * (j + 0.5f));
        }

        foreach (var limit in limits)
        {
            tiles.Clear();

            for (var x = -FallbackReach; x < FallbackReach; x++)
            for (var y = -FallbackReach; y < FallbackReach; y++)
            {
                if ((x + 0.5f) * (x + 0.5f) + (y + 0.5f) * (y + 0.5f) <= limit)
                    tiles.Add(new Vector2i(x, y));
            }

            if (tiles.Count >= min && !IsRectangle(tiles))
                return;
        }
    }

    /// <summary>Fills enclosed ground, joins diagonal-only touches, trims one-tile spurs and keeps the largest piece.</summary>
    /// <param name="ends">A rift's two end tiles, which may keep a single neighbour.</param>
    private static void Tidy(HashSet<Vector2i> tiles, HashSet<Vector2i> ends)
    {
        for (var pass = 0; pass < 8; pass++)
        {
            var changed = FillIslands(tiles);
            changed |= JoinDiagonals(tiles);
            changed |= TrimSpurs(tiles, ends);
            changed |= KeepLargest(tiles);

            if (!changed)
                return;
        }
    }

    /// <summary>Turns ground the hole encloses into hole.</summary>
    private static bool FillIslands(HashSet<Vector2i> tiles)
    {
        if (tiles.Count == 0)
            return false;

        Bounds(tiles, out var min, out var max);
        min -= Vector2i.One;
        max += Vector2i.One;

        var outside = new HashSet<Vector2i>();
        var queue = new Queue<Vector2i>();
        queue.Enqueue(min);
        outside.Add(min);

        while (queue.TryDequeue(out var tile))
        {
            foreach (var step in Cardinals)
            {
                var next = tile + step;
                if (next.X < min.X || next.Y < min.Y || next.X > max.X || next.Y > max.Y
                    || tiles.Contains(next) || !outside.Add(next))
                    continue;

                queue.Enqueue(next);
            }
        }

        var changed = false;
        for (var x = min.X; x <= max.X; x++)
        for (var y = min.Y; y <= max.Y; y++)
        {
            var tile = new Vector2i(x, y);
            if (!outside.Contains(tile) && tiles.Add(tile))
                changed = true;
        }

        return changed;
    }

    /// <summary>Where two hole tiles touch only at a corner, fills one of the two tiles between them.</summary>
    private static bool JoinDiagonals(HashSet<Vector2i> tiles)
    {
        var changed = false;

        foreach (var tile in tiles.OrderBy(t => t.Y).ThenBy(t => t.X).ToList())
        {
            foreach (var diagonal in Diagonals)
            {
                var other = tile + diagonal;
                var side = tile + new Vector2i(diagonal.X, 0);
                var above = tile + new Vector2i(0, 1);

                if (!tiles.Contains(other) || tiles.Contains(side) || tiles.Contains(above))
                    continue;

                tiles.Add(side);
                changed = true;
            }
        }

        return changed;
    }

    /// <summary>Removes hole tiles with fewer than two hole neighbours, but a rift's ends, until none is left.</summary>
    private static bool TrimSpurs(HashSet<Vector2i> tiles, HashSet<Vector2i> ends)
    {
        var changed = false;
        var trimmed = true;

        while (trimmed && tiles.Count > 1)
        {
            trimmed = false;

            foreach (var tile in tiles.OrderBy(t => t.Y).ThenBy(t => t.X).ToList())
            {
                if (ends.Contains(tile) || Neighbours(tiles, tile) >= 2)
                    continue;

                tiles.Remove(tile);
                trimmed = changed = true;
            }
        }

        return changed;
    }

    /// <summary>Keeps only the largest edge-connected piece of the hole.</summary>
    private static bool KeepLargest(HashSet<Vector2i> tiles)
    {
        var seen = new HashSet<Vector2i>();
        List<Vector2i>? largest = null;

        foreach (var start in tiles.OrderBy(t => t.Y).ThenBy(t => t.X))
        {
            if (seen.Contains(start))
                continue;

            var piece = Flood(tiles, start, seen);
            if (largest == null || piece.Count > largest.Count)
                largest = piece;
        }

        if (largest == null || largest.Count == tiles.Count)
            return false;

        tiles.Clear();
        tiles.UnionWith(largest);
        return true;
    }

    /// <summary>The edge-connected piece of a tile set holding a start tile, marked in <paramref name="seen"/>.</summary>
    public static List<Vector2i> Flood(IReadOnlySet<Vector2i> tiles, Vector2i start, HashSet<Vector2i> seen)
    {
        var piece = new List<Vector2i>();
        var queue = new Queue<Vector2i>();
        queue.Enqueue(start);
        seen.Add(start);

        while (queue.TryDequeue(out var tile))
        {
            piece.Add(tile);

            foreach (var step in Cardinals)
            {
                var next = tile + step;
                if (tiles.Contains(next) && seen.Add(next))
                    queue.Enqueue(next);
            }
        }

        return piece;
    }

    /// <summary>How many of a tile's four edge neighbours are in the set.</summary>
    public static int Neighbours(IReadOnlySet<Vector2i> tiles, Vector2i tile)
    {
        var count = 0;
        foreach (var step in Cardinals)
        {
            if (tiles.Contains(tile + step))
                count++;
        }

        return count;
    }

    /// <summary>The corners of a tile set's bounding box.</summary>
    private static void Bounds(IEnumerable<Vector2i> tiles, out Vector2i min, out Vector2i max)
    {
        min = new Vector2i(int.MaxValue, int.MaxValue);
        max = new Vector2i(int.MinValue, int.MinValue);

        foreach (var tile in tiles)
        {
            min = Vector2i.ComponentMin(min, tile);
            max = Vector2i.ComponentMax(max, tile);
        }
    }

    /// <summary>Moves the hole so the tile nearest its centroid is (0, 0), then finds its bounds and lip.</summary>
    private void Place(HashSet<Vector2i> tiles)
    {
        var sum = Vector2.Zero;
        foreach (var tile in tiles)
        {
            sum += new Vector2(tile.X + 0.5f, tile.Y + 0.5f);
        }

        var centroid = sum / tiles.Count;
        var anchor = tiles
            .OrderBy(t => Vector2.DistanceSquared(new Vector2(t.X + 0.5f, t.Y + 0.5f), centroid))
            .ThenBy(t => t.Y)
            .ThenBy(t => t.X)
            .First();

        foreach (var tile in tiles)
        {
            Hole.Add(tile - anchor);
        }

        Centroid = centroid - new Vector2(anchor.X, anchor.Y);
        Bounds(Hole, out Min, out Max);

        foreach (var tile in Hole)
        {
            for (var x = -1; x <= 1; x++)
            for (var y = -1; y <= 1; y++)
            {
                var near = tile + new Vector2i(x, y);
                if (!Hole.Contains(near))
                    Ring.Add(near);
            }
        }
    }

    /// <summary>The lip tile beside the hole farthest out on the climb side, nearest the centroid across that side.</summary>
    private void FindClimb(Direction side)
    {
        var step = side.ToIntVec();
        var across = new Vector2(Math.Abs(step.Y), Math.Abs(step.X));
        var centre = Vector2.Dot(Centroid, across);

        Climb = Ring
            .Where(tile => Hole.Contains(tile - step))
            .OrderByDescending(tile => tile.X * step.X + tile.Y * step.Y)
            .ThenBy(tile => MathF.Abs(Vector2.Dot(new Vector2(tile.X + 0.5f, tile.Y + 0.5f), across) - centre))
            .ThenBy(tile => tile.X)
            .ThenBy(tile => tile.Y)
            .First();
    }

    /// <summary>Picks the rim decor spots at random among lip tiles clear of the climb tile and of each other.</summary>
    // A hole at the top of the size range gets about rimCount; a smaller one proportionally fewer, but at least one.
    private void PickRim(WFCavernMouthSpec spec, System.Random random)
    {
        if (spec.RimCount <= 0)
            return;

        var scaled = (int) MathF.Round(spec.RimCount * Hole.Count / (float) Math.Max(1, spec.MaxTiles));
        var count = Math.Max(1, scaled - random.Next(2));
        var free = Ring
            .Where(tile => Math.Max(Math.Abs(tile.X - Climb.X), Math.Abs(tile.Y - Climb.Y)) > 1)
            .OrderBy(tile => tile.Y)
            .ThenBy(tile => tile.X)
            .ToList();

        while (Rim.Count < count && free.Count > 0)
        {
            var spot = free[random.Next(free.Count)];
            Rim.Add(spot);
            free.RemoveAll(tile => Math.Max(Math.Abs(tile.X - spot.X), Math.Abs(tile.Y - spot.Y)) <= 1);
        }
    }
}
