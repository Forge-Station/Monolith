using Robust.Shared.Serialization;

namespace Content.Shared._WF.GroundCover;

/// <summary>Snow cover layout: one byte of depth per tile, in square chunks.</summary>
public static class WFSnowCover
{
    public const int ChunkSize = 16;

    public const int ChunkArea = ChunkSize * ChunkSize;

    /// <summary>Depth units in one cover level.</summary>
    public const int UnitsPerLevel = 60;

    public const int MaxLevels = 3;

    public const int MaxDepth = UnitsPerLevel * MaxLevels;

    /// <summary>Depth from which snow slows walking, and the depth at which it slows the most.</summary>
    public const int SlowFrom = UnitsPerLevel;

    /// <summary>Walking speed share in the deepest snow.</summary>
    public const float DeepestSpeed = 0.75f;

    /// <summary>Speed steps between no slowdown and the deepest, so patchy snow doesn't change speed every tile.</summary>
    public const int SpeedSteps = 3;

    /// <summary>Depth from which a tile counts as snow covered: footsteps crunch and leave prints.</summary>
    public const int CoverFrom = UnitsPerLevel / 2;

    public static Vector2i ChunkOf(Vector2i tile)
    {
        return new Vector2i((int) MathF.Floor(tile.X / (float) ChunkSize), (int) MathF.Floor(tile.Y / (float) ChunkSize));
    }

    /// <summary>A tile's index in its chunk's depth array.</summary>
    public static int IndexIn(Vector2i tile)
    {
        var x = tile.X - (int) MathF.Floor(tile.X / (float) ChunkSize) * ChunkSize;
        var y = tile.Y - (int) MathF.Floor(tile.Y / (float) ChunkSize) * ChunkSize;
        return x + y * ChunkSize;
    }

    /// <summary>The tile at an index of a chunk.</summary>
    public static Vector2i TileAt(Vector2i chunk, int index)
    {
        return new Vector2i(chunk.X * ChunkSize + index % ChunkSize, chunk.Y * ChunkSize + index / ChunkSize);
    }

    /// <summary>Walking speed share on snow of a depth, in <see cref="SpeedSteps"/> steps.</summary>
    public static float SpeedAt(int depth)
    {
        if (depth <= SlowFrom)
            return 1f;

        var deep = Math.Clamp((depth - SlowFrom) / (float) (MaxDepth - SlowFrom), 0f, 1f);
        var step = (int) MathF.Ceiling(deep * SpeedSteps);
        return 1f - (1f - DeepestSpeed) * step / SpeedSteps;
    }

    /// <summary>
    /// The depth that slows a mob on a tile: the tile's own, or the average of it and its eight neighbours if that's
    /// less, so a lone deep drift or a shoveled path decides less than the snow around it.
    /// </summary>
    public static int MoveDepth(Vector2i tile, Func<Vector2i, int> depthAt)
    {
        var center = depthAt(tile);
        if (center <= SlowFrom)
            return center;

        var sum = 0;
        for (var x = -1; x <= 1; x++)
        {
            for (var y = -1; y <= 1; y++)
                sum += x == 0 && y == 0 ? center : depthAt(tile + new Vector2i(x, y));
        }

        return Math.Min(center, sum / 9);
    }

    /// <summary>A steady 0.75 to 1.25 per tile, so snow lies in drifts rather than evenly.</summary>
    public static float Drift(Vector2i tile)
    {
        var h = unchecked((uint) (tile.X * 73856093) ^ (uint) (tile.Y * 19349663));
        h ^= h >> 13;
        h = unchecked(h * 0x5bd1e995);
        h ^= h >> 15;
        return 0.75f + (h & 0xFFFF) / 65535f * 0.5f;
    }
}

/// <summary>Snow cover chunks of one grid sent to a client. A chunk without depths was cleared.</summary>
[Serializable, NetSerializable]
public sealed class WFSnowCoverChunksMessage : EntityEventArgs
{
    public NetEntity Grid;

    public List<WFSnowChunkData> Chunks = new();
}

/// <summary>A client dropped its snow for these grids, as after a full state resync; the server sends it again.</summary>
[Serializable, NetSerializable]
public sealed class WFSnowCoverForgetMessage : EntityEventArgs
{
    public List<NetEntity> Grids = new();
}

/// <summary>One chunk's tile depths, or null when it has no snow left.</summary>
[Serializable, NetSerializable]
public record struct WFSnowChunkData(Vector2i Index, byte[]? Depths);
