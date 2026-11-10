namespace Content.Shared._WF.MappingTools;

/// <summary>Cell shapes for the mapping screen's draw modes: lines, rectangles and flood fills.</summary>
public static class MappingToolsShapes
{
    /// <summary>The cells of a Bresenham line from <paramref name="a"/> to <paramref name="b"/>, both included.</summary>
    public static List<Vector2i> Line(Vector2i a, Vector2i b)
    {
        var cells = new List<Vector2i>();
        var dx = Math.Abs(b.X - a.X);
        var dy = -Math.Abs(b.Y - a.Y);
        var sx = a.X < b.X ? 1 : -1;
        var sy = a.Y < b.Y ? 1 : -1;
        var error = dx + dy;
        var x = a.X;
        var y = a.Y;

        while (true)
        {
            cells.Add(new Vector2i(x, y));
            if (x == b.X && y == b.Y)
                break;

            var doubled = 2 * error;
            if (doubled >= dy)
            {
                error += dy;
                x += sx;
            }

            if (doubled <= dx)
            {
                error += dx;
                y += sy;
            }
        }

        return cells;
    }

    /// <summary>
    /// The cells of the rectangle with corners <paramref name="a"/> and <paramref name="b"/>, both included; only its
    /// border when <paramref name="hollow"/>.
    /// </summary>
    public static List<Vector2i> Rect(Vector2i a, Vector2i b, bool hollow)
    {
        var min = Vector2i.ComponentMin(a, b);
        var max = Vector2i.ComponentMax(a, b);
        var cells = new List<Vector2i>();

        for (var y = min.Y; y <= max.Y; y++)
        {
            for (var x = min.X; x <= max.X; x++)
            {
                if (hollow && x != min.X && x != max.X && y != min.Y && y != max.Y)
                    continue;

                cells.Add(new Vector2i(x, y));
            }
        }

        return cells;
    }

    /// <summary>How many cells <see cref="Rect"/> returns, without building them.</summary>
    public static long RectCount(Vector2i a, Vector2i b, bool hollow)
    {
        var size = Vector2i.ComponentMax(a, b) - Vector2i.ComponentMin(a, b) + Vector2i.One;
        var full = (long) size.X * size.Y;
        if (!hollow || size.X <= 2 || size.Y <= 2)
            return full;

        return full - (long) (size.X - 2) * (size.Y - 2);
    }

    /// <summary>
    /// The cells of the ellipse inscribed in the box with corners <paramref name="a"/> and <paramref name="b"/>, both
    /// included: every cell whose centre lies inside it, plus the middle cells of any row or column that would
    /// otherwise stay empty, so it touches all four sides. When <paramref name="hollow"/>, only the cells with a side
    /// on the outside, a closed ring one cell thick.
    /// </summary>
    public static List<Vector2i> Ellipse(Vector2i a, Vector2i b, bool hollow)
    {
        var min = Vector2i.ComponentMin(a, b);
        var max = Vector2i.ComponentMax(a, b);
        long w = max.X - min.X + 1;
        long h = max.Y - min.Y + 1;

        // Twice a cell centre's offset from the box centre is an integer, so the test is exact and symmetric.
        bool Inside(long x, long y)
        {
            var dx = 2 * (x - min.X) + 1 - w;
            var dy = 2 * (y - min.Y) + 1 - h;
            return dx * dx * h * h + dy * dy * w * w <= w * w * h * h;
        }

        bool Middle(long offset, long size)
        {
            return offset == (size - 1) / 2 || offset == size / 2;
        }

        // A row or column is empty only at the very ends of a thin ellipse; its middle cell(s) stand in.
        var rowFilled = new bool[h];
        var columnFilled = new bool[w];
        for (var y = 0L; y < h; y++)
        {
            for (var x = 0L; x < w; x++)
            {
                if (!Inside(min.X + x, min.Y + y))
                    continue;

                rowFilled[y] = true;
                columnFilled[x] = true;
            }
        }

        bool Filled(long x, long y)
        {
            if (x < 0 || y < 0 || x >= w || y >= h)
                return false;

            return Inside(min.X + x, min.Y + y) ||
                   !rowFilled[y] && Middle(x, w) ||
                   !columnFilled[x] && Middle(y, h);
        }

        var cells = new List<Vector2i>();
        for (var y = 0L; y < h; y++)
        {
            for (var x = 0L; x < w; x++)
            {
                if (!Filled(x, y))
                    continue;

                if (hollow && Filled(x + 1, y) && Filled(x - 1, y) && Filled(x, y + 1) && Filled(x, y - 1))
                    continue;

                cells.Add(new Vector2i(min.X + (int) x, min.Y + (int) y));
            }
        }

        return cells;
    }

    /// <summary>
    /// The 4-connected cells around <paramref name="start"/> whose type matches the start's, inside
    /// <paramref name="bounds"/> (right and top exclusive). A null type is a cell that can't be filled. Stops after
    /// <paramref name="cap"/> cells and sets <paramref name="truncated"/>.
    /// </summary>
    public static List<Vector2i> Flood(Vector2i start, Func<Vector2i, int?> typeAt, Box2i bounds, int cap,
        out bool truncated)
    {
        truncated = false;
        var cells = new List<Vector2i>();
        if (cap <= 0 || !Contains(bounds, start) || typeAt(start) is not { } target)
            return cells;

        var seen = new HashSet<Vector2i> { start };
        var queue = new Queue<Vector2i>();
        queue.Enqueue(start);

        while (queue.TryDequeue(out var cell))
        {
            if (cells.Count >= cap)
            {
                truncated = true;
                break;
            }

            cells.Add(cell);

            for (var i = 0; i < 4; i++)
            {
                var next = cell + Neighbour(i);
                if (!Contains(bounds, next) || !seen.Add(next) || typeAt(next) != target)
                    continue;

                queue.Enqueue(next);
            }
        }

        return cells;
    }

    private static Vector2i Neighbour(int index)
    {
        return index switch
        {
            0 => new Vector2i(1, 0),
            1 => new Vector2i(-1, 0),
            2 => new Vector2i(0, 1),
            _ => new Vector2i(0, -1),
        };
    }

    private static bool Contains(Box2i box, Vector2i cell)
    {
        return cell.X >= box.Left && cell.X < box.Right && cell.Y >= box.Bottom && cell.Y < box.Top;
    }
}
