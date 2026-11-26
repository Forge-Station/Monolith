using System.Linq;
using System.Numerics;
using Content.Shared._WF.PlanetPois;

namespace Content.Server._WF.PlanetPois;

/// <summary>Works out where a road's dressing goes from its tiles alone; the system spawns what this lays out.</summary>
public static class WFRoadDressingPlanner
{
    private static readonly Vector2i North = new(0, 1);
    private static readonly Vector2i South = new(0, -1);
    private static readonly Vector2i East = new(1, 0);
    private static readonly Vector2i West = new(-1, 0);

    /// <summary>How far in from each edge a piece's bounds are shrunk before its tiles are counted.</summary>
    private const float Slack = 0.2f;

    /// <summary>The way the road runs at a centreline tile, or null at a bend, a junction or an end.</summary>
    public static WFRoadAxis? AxisOf(IReadOnlySet<Vector2i> centreline, Vector2i tile)
    {
        var northSouth = (centreline.Contains(tile + North) ? 1 : 0) + (centreline.Contains(tile + South) ? 1 : 0);
        var eastWest = (centreline.Contains(tile + East) ? 1 : 0) + (centreline.Contains(tile + West) ? 1 : 0);

        if (northSouth == 2 && eastWest == 0)
            return WFRoadAxis.NorthSouth;
        if (eastWest == 2 && northSouth == 0)
            return WFRoadAxis.EastWest;
        return null;
    }

    /// <summary>Whether three or more roads meet at a centreline tile.</summary>
    public static bool IsJunction(IReadOnlySet<Vector2i> centreline, Vector2i tile)
    {
        var arms = 0;
        foreach (var step in new[] { North, South, East, West })
        {
            if (centreline.Contains(tile + step))
                arms++;
        }

        return arms >= 3;
    }

    /// <summary>The straight centreline tiles at least <paramref name="clear"/> steps along the road from every bend, junction and end.</summary>
    public static Dictionary<Vector2i, WFRoadAxis> StraightRuns(IReadOnlySet<Vector2i> centreline, int clear)
    {
        var straight = new Dictionary<Vector2i, WFRoadAxis>();
        var near = new Dictionary<Vector2i, int>();
        var frontier = new Queue<Vector2i>();

        foreach (var tile in centreline)
        {
            if (AxisOf(centreline, tile) is { } axis)
            {
                straight[tile] = axis;
                continue;
            }

            near[tile] = 0;
            frontier.Enqueue(tile);
        }

        while (frontier.TryDequeue(out var tile))
        {
            var depth = near[tile];
            if (depth >= clear)
                continue;

            foreach (var step in new[] { North, South, East, West })
            {
                var next = tile + step;
                if (!centreline.Contains(next) || near.ContainsKey(next))
                    continue;

                near[next] = depth + 1;
                frontier.Enqueue(next);
            }
        }

        foreach (var (tile, depth) in near)
        {
            if (depth < clear)
                straight.Remove(tile);
        }

        return straight;
    }

    /// <summary>Where the road lies from a shoulder tile, or null when no kerb fits: no road beside it, or road on both sides.</summary>
    public static WFRoadKerb? KerbOf(IReadOnlySet<Vector2i> road, Vector2i shoulder)
    {
        var north = road.Contains(shoulder + North);
        var south = road.Contains(shoulder + South);
        var east = road.Contains(shoulder + East);
        var west = road.Contains(shoulder + West);

        if ((north && south) || (east && west))
            return null;

        if (north)
            return east ? WFRoadKerb.NorthEast : west ? WFRoadKerb.NorthWest : WFRoadKerb.North;
        if (south)
            return east ? WFRoadKerb.SouthEast : west ? WFRoadKerb.SouthWest : WFRoadKerb.South;
        if (east)
            return WFRoadKerb.East;
        if (west)
            return WFRoadKerb.West;

        if (road.Contains(shoulder + North + East))
            return WFRoadKerb.InnerNorthEast;
        if (road.Contains(shoulder + North + West))
            return WFRoadKerb.InnerNorthWest;
        if (road.Contains(shoulder + South + East))
            return WFRoadKerb.InnerSouthEast;
        if (road.Contains(shoulder + South + West))
            return WFRoadKerb.InnerSouthWest;
        return null;
    }

    /// <summary>The step across the road at a stretch running the given way.</summary>
    public static Vector2i Across(WFRoadAxis axis) => axis == WFRoadAxis.NorthSouth ? East : North;

    /// <summary>Whether a tile has one of the set beside it, not counting corners.</summary>
    public static bool TouchesAny(Vector2i tile, IReadOnlySet<Vector2i> set)
        => set.Contains(tile + North) || set.Contains(tile + South) || set.Contains(tile + East) || set.Contains(tile + West);

    /// <summary>
    /// Street light tiles: every <paramref name="spacing"/> tiles along each straight stretch, on the first shoulder
    /// tile, on alternating sides. Counted from the world's origin, so the rhythm carries across a junction.
    /// </summary>
    public static IEnumerable<Vector2i> Lights(IReadOnlyDictionary<Vector2i, WFRoadAxis> straight, IReadOnlySet<Vector2i> shoulder, int half, int spacing)
    {
        if (spacing < 1)
            yield break;

        foreach (var (tile, axis) in straight.OrderBy(pair => pair.Key.X).ThenBy(pair => pair.Key.Y))
        {
            var along = axis == WFRoadAxis.NorthSouth ? tile.Y : tile.X;
            if (Mod(along, spacing) != 0)
                continue;

            var side = Mod(FloorDiv(along, spacing), 2) == 0 ? 1 : -1;
            var spot = tile + Across(axis) * (side * (half + 1));
            if (shoulder.Contains(spot))
                yield return spot;
        }
    }

    /// <summary>The tiles a piece covers when it stands on <paramref name="origin"/> with the given bounds about its own centre.</summary>
    public static IEnumerable<Vector2i> Footprint(Box2 bounds, Vector2i origin)
    {
        var centre = new Vector2(origin.X + 0.5f, origin.Y + 0.5f);
        var left = (int) MathF.Floor(centre.X + bounds.Left + Slack);
        var right = (int) MathF.Floor(centre.X + bounds.Right - Slack);
        var bottom = (int) MathF.Floor(centre.Y + bounds.Bottom + Slack);
        var top = (int) MathF.Floor(centre.Y + bounds.Top - Slack);

        for (var x = Math.Min(left, origin.X); x <= Math.Max(right, origin.X); x++)
        for (var y = Math.Min(bottom, origin.Y); y <= Math.Max(top, origin.Y); y++)
        {
            yield return new Vector2i(x, y);
        }
    }

    private static int Mod(int value, int by) => (value % by + by) % by;

    private static int FloorDiv(int value, int by) => (value - Mod(value, by)) / by;
}
