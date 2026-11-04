namespace Content.Shared._WF.PlanetMap;

/// <summary>Run-length coding for a map's terrain cells.</summary>
public static class WFPlanetMapEncoding
{
    /// <summary>The most cells a terrain may hold, so its state stays small.</summary>
    public const int MaxCells = 129 * 129;

    /// <summary>Pairs of (value, run length up to 255).</summary>
    public static byte[] Encode(IReadOnlyList<byte> cells)
    {
        var runs = new List<byte>();
        var i = 0;
        while (i < cells.Count)
        {
            var value = cells[i];
            var length = 1;
            while (i + length < cells.Count && cells[i + length] == value && length < byte.MaxValue)
                length++;
            runs.Add(value);
            runs.Add((byte) length);
            i += length;
        }

        return runs.ToArray();
    }

    /// <summary>Expands runs into exactly <paramref name="count"/> cells; a short or overlong input is cut or padded with zero.</summary>
    public static byte[] Decode(byte[] runs, int count)
    {
        var cells = new byte[Math.Clamp(count, 0, MaxCells)];
        var at = 0;
        for (var i = 0; i + 1 < runs.Length && at < cells.Length; i += 2)
        {
            var length = Math.Min(runs[i + 1], cells.Length - at);
            Array.Fill(cells, runs[i], at, length);
            at += length;
        }

        return cells;
    }
}
