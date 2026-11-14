using System.Diagnostics.CodeAnalysis;

namespace Content.Shared._WF.MappingMode;

/// <summary>
/// Puts one uploaded map file back together from its chunks: <see cref="Bytes"/> bytes announced up front, sent as
/// <see cref="ChunkCount"/> chunks of at most <see cref="ChunkSize"/> bytes.
/// </summary>
public sealed class WFMappingUploadAssembler
{
    public const int ChunkSize = 128 * 1024;

    /// <summary>The hard cap on an upload, whatever <c>max_upload_bytes</c> says.</summary>
    public const int MaxFileBytes = 8 * 1024 * 1024;

    public const int MaxChunks = MaxFileBytes / ChunkSize;

    public readonly int TransferId;
    public readonly int Bytes;
    public readonly int ChunkCount;

    private readonly byte[]?[] _chunks;
    private int _received;
    private long _receivedBytes;

    private WFMappingUploadAssembler(int transferId, int bytes, int chunkCount)
    {
        TransferId = transferId;
        Bytes = bytes;
        ChunkCount = chunkCount;
        _chunks = new byte[]?[chunkCount];
    }

    /// <summary>The chunks a file of <paramref name="bytes"/> bytes is sent in.</summary>
    public static int ChunksFor(long bytes)
    {
        return (int) Math.Max(1, (bytes + ChunkSize - 1) / ChunkSize);
    }

    /// <summary>The upload cap in force: <paramref name="configured"/> kept between 1 byte and <see cref="MaxFileBytes"/>.</summary>
    public static int Cap(long configured)
    {
        return (int) Math.Clamp(configured, 1, MaxFileBytes);
    }

    /// <summary>
    /// Starts a transfer; false when the size is empty or over <paramref name="maxBytes"/> (itself capped at
    /// <see cref="MaxFileBytes"/>), or the chunk count doesn't match the size.
    /// </summary>
    public static bool TryCreate(int transferId, long bytes, int chunkCount, long maxBytes,
        [NotNullWhen(true)] out WFMappingUploadAssembler? assembler)
    {
        assembler = null;
        if (bytes < 1 || bytes > Cap(maxBytes) || chunkCount != ChunksFor(bytes))
            return false;

        assembler = new WFMappingUploadAssembler(transferId, (int) bytes, chunkCount);
        return true;
    }

    /// <summary>Chunks received so far.</summary>
    public int Received => _received;

    public bool IsComplete => _received == ChunkCount;

    /// <summary>Stores one chunk.</summary>
    public WFMappingChunkResult Add(int index, byte[] data)
    {
        if (index < 0 || index >= ChunkCount)
            return WFMappingChunkResult.OutOfRange;

        if (_chunks[index] != null)
            return WFMappingChunkResult.Duplicate;

        if (data.Length > ChunkSize || _receivedBytes + data.Length > Bytes)
            return WFMappingChunkResult.TooLarge;

        _chunks[index] = data;
        _received++;
        _receivedBytes += data.Length;
        return WFMappingChunkResult.Accepted;
    }

    /// <summary>The whole file once every chunk has arrived and they add up to the announced size.</summary>
    public bool TryComplete(out byte[] data)
    {
        data = Array.Empty<byte>();
        if (!IsComplete || _receivedBytes != Bytes)
            return false;

        data = new byte[Bytes];
        var offset = 0;
        foreach (var chunk in _chunks)
        {
            chunk!.CopyTo(data, offset);
            offset += chunk.Length;
        }

        return true;
    }
}
