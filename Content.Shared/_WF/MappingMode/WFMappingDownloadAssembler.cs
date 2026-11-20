using System.Diagnostics.CodeAnalysis;

namespace Content.Shared._WF.MappingMode;

/// <summary>What became of a chunk handed to a <see cref="WFMappingDownloadAssembler"/>.</summary>
public enum WFMappingChunkResult : byte
{
    Accepted,

    /// <summary>The index is outside the transfer's chunk count, or the count doesn't match.</summary>
    OutOfRange,

    /// <summary>That chunk already arrived.</summary>
    Duplicate,

    /// <summary>The chunk, or the file so far, is over its size cap or the size the server announced.</summary>
    TooLarge,
}

/// <summary>
/// Puts one downloaded file back together from its chunks: a transfer is one file, sent as up to
/// <see cref="MaxChunks"/> chunks of at most <see cref="ChunkSize"/> bytes.
/// </summary>
public sealed class WFMappingDownloadAssembler
{
    public const int ChunkSize = 256 * 1024;
    public const int MaxFileBytes = 32 * 1024 * 1024;
    public const int MaxChunks = MaxFileBytes / ChunkSize;

    public readonly int TransferId;
    public readonly string FileName;
    public readonly int ChunkCount;

    /// <summary>The file's size as the server read it; the chunks must add up to exactly this.</summary>
    public readonly int TotalBytes;

    private readonly byte[]?[] _chunks;
    private int _received;
    private long _bytes;

    private WFMappingDownloadAssembler(int transferId, string fileName, int chunkCount, int totalBytes)
    {
        TransferId = transferId;
        FileName = fileName;
        ChunkCount = chunkCount;
        TotalBytes = totalBytes;
        _chunks = new byte[]?[chunkCount];
    }

    /// <summary>
    /// Starts a transfer; false when the file name isn't one a download of <paramref name="mapName"/> may carry
    /// (<see cref="WFMappingLibraryNames.IsDownloadFile"/>), the chunk count is outside 1 to <see cref="MaxChunks"/>
    /// or the size is outside 1 to <see cref="MaxFileBytes"/>: an empty file is never a valid download.
    /// </summary>
    public static bool TryCreate(int transferId, string mapName, string fileName, int chunkCount, int totalBytes,
        [NotNullWhen(true)] out WFMappingDownloadAssembler? assembler)
    {
        assembler = null;
        if (chunkCount < 1 || chunkCount > MaxChunks || totalBytes < 1 || totalBytes > MaxFileBytes ||
            !WFMappingLibraryNames.IsDownloadFile(mapName, fileName))
        {
            return false;
        }

        assembler = new WFMappingDownloadAssembler(transferId, fileName, chunkCount, totalBytes);
        return true;
    }

    /// <summary>Chunks a file of <paramref name="bytes"/> is sent in.</summary>
    public static int ChunksFor(int bytes)
    {
        return Math.Max(1, (bytes + ChunkSize - 1) / ChunkSize);
    }

    /// <summary>Chunks received so far.</summary>
    public int Received => _received;

    public bool IsComplete => _received == ChunkCount;

    /// <summary>Stores one chunk; <paramref name="chunkCount"/> must match the transfer's.</summary>
    public WFMappingChunkResult Add(int index, int chunkCount, byte[] data)
    {
        if (chunkCount != ChunkCount || index < 0 || index >= ChunkCount)
            return WFMappingChunkResult.OutOfRange;

        if (_chunks[index] != null)
            return WFMappingChunkResult.Duplicate;

        if (data.Length > ChunkSize || _bytes + data.Length > Math.Min(MaxFileBytes, TotalBytes))
            return WFMappingChunkResult.TooLarge;

        _chunks[index] = data;
        _received++;
        _bytes += data.Length;
        return WFMappingChunkResult.Accepted;
    }

    /// <summary>The whole file once every chunk has arrived and they add up to <see cref="TotalBytes"/>.</summary>
    public bool TryComplete(out byte[] data)
    {
        data = Array.Empty<byte>();
        if (!IsComplete || _bytes != TotalBytes)
            return false;

        data = new byte[_bytes];
        var offset = 0;
        foreach (var chunk in _chunks)
        {
            chunk!.CopyTo(data, offset);
            offset += chunk.Length;
        }

        return true;
    }
}
