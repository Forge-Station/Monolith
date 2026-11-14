using Robust.Shared.Serialization;

namespace Content.Shared._WF.MappingMode;

/// <summary>
/// Asks to upload a map file of <see cref="Bytes"/> bytes in <see cref="ChunkCount"/> chunks into the player's library
/// under <see cref="Name"/>. The server answers <see cref="WFMappingUploadReadyEvent"/> or refuses.
/// </summary>
[Serializable, NetSerializable]
public sealed class WFMappingUploadBeginRequest(string name, int bytes, int chunkCount) : EntityEventArgs
{
    public readonly string Name = name;
    public readonly int Bytes = bytes;
    public readonly int ChunkCount = chunkCount;
}

/// <summary>Server to client: the upload is accepted; send its chunks under this transfer id.</summary>
[Serializable, NetSerializable]
public sealed class WFMappingUploadReadyEvent(int transferId) : EntityEventArgs
{
    public readonly int TransferId = transferId;
}

/// <summary>Client to server: one chunk of the file being uploaded.</summary>
[Serializable, NetSerializable]
public sealed class WFMappingUploadChunkEvent(int transferId, int chunk, byte[] data) : EntityEventArgs
{
    public readonly int TransferId = transferId;
    public readonly int Chunk = chunk;
    public readonly byte[] Data = data;
}

/// <summary>Server to client: a chunk arrived; the client sends the next.</summary>
[Serializable, NetSerializable]
public sealed class WFMappingUploadAckEvent(int transferId, int chunk) : EntityEventArgs
{
    public readonly int TransferId = transferId;
    public readonly int Chunk = chunk;
}

/// <summary>
/// Server to client: the upload was checked and stored as a version of a library map, without
/// <see cref="Dropped"/> entities of prototypes the server lacks.
/// </summary>
[Serializable, NetSerializable]
public sealed class WFMappingUploadDoneEvent(int transferId, string mapName, string version, int dropped = 0)
    : EntityEventArgs
{
    public readonly int TransferId = transferId;
    public readonly string MapName = mapName;
    public readonly string Version = version;
    public readonly int Dropped = dropped;
}

/// <summary>
/// Server to client: the file uses prototypes this server lacks. The server holds it until the player answers
/// with <see cref="WFMappingUploadConfirmRequest"/> or <see cref="WFMappingUploadCancelRequest"/>, or the wait runs
/// out. <see cref="MissingPrototypes"/> names at most <see cref="MaxListed"/> of them, sorted.
/// </summary>
[Serializable, NetSerializable]
public sealed class WFMappingUploadNeedsConfirmEvent(int transferId, List<string> missingPrototypes,
    int missingPrototypeCount, int entitiesToDrop) : EntityEventArgs
{
    public const int MaxListed = 25;

    public readonly int TransferId = transferId;
    public readonly List<string> MissingPrototypes = missingPrototypes;
    public readonly int MissingPrototypeCount = missingPrototypeCount;

    /// <summary>Entities of those prototypes plus everything parented to them.</summary>
    public readonly int EntitiesToDrop = entitiesToDrop;
}

/// <summary>
/// Client to server: store the held upload. Only <see cref="DropMissing"/> set does anything; the server works out
/// again which prototypes are missing.
/// </summary>
[Serializable, NetSerializable]
public sealed class WFMappingUploadConfirmRequest(int transferId, bool dropMissing) : EntityEventArgs
{
    public readonly int TransferId = transferId;
    public readonly bool DropMissing = dropMissing;
}

/// <summary>Stops the player's upload, running or waiting for a confirm.</summary>
[Serializable, NetSerializable]
public sealed class WFMappingUploadCancelRequest(int transferId) : EntityEventArgs
{
    public readonly int TransferId = transferId;
}

/// <summary>
/// Server to client: the upload was refused or stopped; 0 for a refused begin. A <see cref="WFMappingResultEvent"/>
/// says why.
/// </summary>
[Serializable, NetSerializable]
public sealed class WFMappingUploadCancelledEvent(int transferId) : EntityEventArgs
{
    public readonly int TransferId = transferId;
}
