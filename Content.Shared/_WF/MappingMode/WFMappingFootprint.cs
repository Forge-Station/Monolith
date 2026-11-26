using System.Linq;
using System.Numerics;
using Content.Shared._WF.MappingTools;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.MappingMode;

/// <summary>
/// One grid of a reference footprint: where it sits and how it is turned relative to the footprint's anchor, and its
/// tiles as grid-local rectangles.
/// </summary>
[Serializable, NetSerializable]
public sealed class WFMappingFootprintGrid(Vector2 offset, Angle rotation, List<Box2i> rects)
{
    public readonly Vector2 Offset = offset;
    public readonly Angle Rotation = rotation;
    public readonly List<Box2i> Rects = rects;
}

/// <summary>
/// Footprint maths for reference loads: tile cells merged into rectangles, and the bounds of a turned footprint.
/// </summary>
public static class WFMappingFootprint
{
    /// <summary>
    /// Covers exactly <paramref name="cells"/> with rectangles: runs along each row, stacked while consecutive rows
    /// have a run with the same span.
    /// </summary>
    public static List<Box2i> Merge(IEnumerable<Vector2i> cells)
    {
        var result = new List<Box2i>();
        var open = new Dictionary<(int Left, int Right), int>();

        foreach (var row in cells.Distinct().GroupBy(c => c.Y).OrderBy(g => g.Key))
        {
            var y = row.Key;
            var next = new Dictionary<(int Left, int Right), int>();
            var xs = row.Select(c => c.X).Order().ToList();

            var start = 0;
            for (var i = 1; i <= xs.Count; i++)
            {
                if (i < xs.Count && xs[i] == xs[i - 1] + 1)
                    continue;

                var span = (xs[start], xs[i - 1] + 1);
                if (open.Remove(span, out var index) && result[index].Top == y)
                {
                    var box = result[index];
                    result[index] = new Box2i(box.Left, box.Bottom, box.Right, y + 1);
                    next[span] = index;
                }
                else
                {
                    result.Add(new Box2i(span.Item1, y, span.Item2, y + 1));
                    next[span] = result.Count - 1;
                }

                start = i;
            }

            open = next;
        }

        return result;
    }

    /// <summary>
    /// The anchor-relative bounds of a footprint turned by <paramref name="turns"/> quarter turns clockwise.
    /// </summary>
    public static Box2 Bounds(IEnumerable<WFMappingFootprintGrid> grids, int turns)
    {
        var turn = MappingToolsMath.RotationDelta(turns);
        Box2? bounds = null;

        foreach (var grid in grids)
        {
            foreach (var rect in grid.Rects)
            {
                foreach (var corner in new[]
                         {
                             new Vector2(rect.Left, rect.Bottom), new Vector2(rect.Right, rect.Bottom),
                             new Vector2(rect.Right, rect.Top), new Vector2(rect.Left, rect.Top),
                         })
                {
                    var point = turn.RotateVec(grid.Offset + grid.Rotation.RotateVec(corner));
                    bounds = bounds is { } b ? b.ExtendToContain(point) : new Box2(point, point);
                }
            }
        }

        return bounds ?? new Box2();
    }

    /// <summary>
    /// Where a grid of the footprint lands when the anchor is put at <paramref name="anchor"/> and turned.
    /// </summary>
    public static (Vector2 Position, Angle Rotation) Place(WFMappingFootprintGrid grid, Vector2 anchor, int turns)
    {
        var turn = MappingToolsMath.RotationDelta(turns);
        return (anchor + turn.RotateVec(grid.Offset), grid.Rotation + turn);
    }
}
