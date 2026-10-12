using System.Numerics;
using System.Text;

namespace Content.Shared._WF.SectorControl;

/// <summary>Flat-top hex maths for the sector grid; size is the circumradius in metres and edge i faces Directions[i].</summary>
public static class WFSectorHex
{
    private static readonly float Sqrt3 = MathF.Sqrt(3f);

    /// <summary>The six neighbour steps, in edge order: 30, 90, 150, 210, 270 and 330 degrees.</summary>
    public static readonly WFSectorCell[] Directions =
    {
        new(1, 0),
        new(0, 1),
        new(-1, 1),
        new(-1, 0),
        new(0, -1),
        new(1, -1),
    };

    /// <summary>Columns that read as a single letter run on either side of the origin.</summary>
    private const int CallsignOffset = 13;

    /// <summary>The centre of a cell in map space.</summary>
    public static Vector2 Centre(WFSectorCell cell, float size)
    {
        return new Vector2(size * 1.5f * cell.Q, size * Sqrt3 * (cell.R + cell.Q * 0.5f));
    }

    /// <summary>The cell a map-space point lies in.</summary>
    public static WFSectorCell CellOf(Vector2 point, float size)
    {
        var q = 2f / 3f * point.X / size;
        var r = (-1f / 3f * point.X + Sqrt3 / 3f * point.Y) / size;
        return Round(q, r);
    }

    /// <summary>Rounds fractional axial coordinates to the nearest cell through cube coordinates.</summary>
    public static WFSectorCell Round(float q, float r)
    {
        var s = -q - r;
        var rq = MathF.Round(q);
        var rr = MathF.Round(r);
        var rs = MathF.Round(s);
        var dq = MathF.Abs(rq - q);
        var dr = MathF.Abs(rr - r);
        var ds = MathF.Abs(rs - s);
        if (dq > dr && dq > ds)
            rq = -rr - rs;
        else if (dr > ds)
            rr = -rq - rs;

        return new WFSectorCell((int) rq, (int) rr);
    }

    /// <summary>Corner <paramref name="index"/>, 0 to 5, of a cell in map space.</summary>
    public static Vector2 Corner(WFSectorCell cell, float size, int index)
    {
        var angle = MathF.PI / 3f * (((index % 6) + 6) % 6);
        return Centre(cell, size) + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * size;
    }

    /// <summary>Writes the six corners of a cell, in order, into <paramref name="corners"/>.</summary>
    public static void Corners(WFSectorCell cell, float size, Span<Vector2> corners)
    {
        for (var i = 0; i < 6 && i < corners.Length; i++)
        {
            corners[i] = Corner(cell, size, i);
        }
    }

    /// <summary>How many steps apart two cells are.</summary>
    public static int Distance(WFSectorCell a, WFSectorCell b)
    {
        var dq = a.Q - b.Q;
        var dr = a.R - b.R;
        return (Math.Abs(dq) + Math.Abs(dq + dr) + Math.Abs(dr)) / 2;
    }

    /// <summary>The cells exactly <paramref name="radius"/> steps from a cell; the cell itself for zero.</summary>
    public static List<WFSectorCell> Ring(WFSectorCell centre, int radius)
    {
        var cells = new List<WFSectorCell>();
        if (radius <= 0)
        {
            cells.Add(centre);
            return cells;
        }

        var cell = new WFSectorCell(centre.Q + Directions[4].Q * radius, centre.R + Directions[4].R * radius);
        for (var side = 0; side < 6; side++)
        {
            for (var step = 0; step < radius; step++)
            {
                cells.Add(cell);
                cell = cell.Neighbour(side);
            }
        }

        return cells;
    }

    /// <summary>The cells at most <paramref name="radius"/> steps from a cell, nearest first.</summary>
    public static List<WFSectorCell> Within(WFSectorCell centre, int radius)
    {
        var cells = new List<WFSectorCell>();
        for (var ring = 0; ring <= radius; ring++)
        {
            cells.AddRange(Ring(centre, ring));
        }

        return cells;
    }

    /// <summary>The cells whose centre lies within <paramref name="radius"/> metres of the origin, nearest ring first.</summary>
    public static List<WFSectorCell> CellsWithin(float radius, float size)
    {
        var cells = new List<WFSectorCell>();
        if (radius < 0f || size <= 0f)
            return cells;

        // Every centre in ring k lies at least 1.5 * size * k from the origin.
        var rings = (int) MathF.Ceiling(radius / (1.5f * size));
        var limit = radius * radius;
        foreach (var cell in Within(WFSectorCell.Origin, rings))
        {
            if (Centre(cell, size).LengthSquared() <= limit)
                cells.Add(cell);
        }

        return cells;
    }

    /// <summary>The cell's name for announcements and the map, a letter run and a number such as N-13 for the origin.</summary>
    public static string Callsign(WFSectorCell cell)
    {
        // Offset rows (odd-q) run straight across the map, where axial r runs on a slant.
        var row = cell.R + (cell.Q - (cell.Q & 1)) / 2;
        return $"{Letters(cell.Q + CallsignOffset)}-{CallsignOffset - row}";
    }

    /// <summary>A bijective base-26 letter run: 0 is A, 25 is Z, 26 is AA. Negative columns take a leading minus.</summary>
    private static string Letters(int index)
    {
        var builder = new StringBuilder();
        var n = index < 0 ? -(long) index : (long) index + 1;
        while (n > 0)
        {
            n--;
            builder.Insert(0, (char) ('A' + (int) (n % 26)));
            n /= 26;
        }

        if (index < 0)
            builder.Insert(0, '-');

        return builder.ToString();
    }
}
