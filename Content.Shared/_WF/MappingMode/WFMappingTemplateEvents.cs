using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.MappingMode;

/// <summary>
/// Where a shared template sits, the same for every member: its centre in world tiles, scale, pixels per tile,
/// rotation in degrees, layer and z-level. Opacity, show and lock stay with each player.
/// </summary>
[Serializable, NetSerializable]
public readonly record struct WFMappingTemplateState(
    float X,
    float Y,
    float ScaleX,
    float ScaleY,
    float Rotation,
    int PixelsPerTile,
    WFMappingTemplateLayer Layer,
    int Depth);

/// <summary>A session's shared template as its members see it; <see cref="Id"/> changes with every share.</summary>
[Serializable, NetSerializable]
public sealed class WFMappingTemplateInfo(int id, NetUserId sharedBy, string sharedByName, int width, int height,
    int bytes, WFMappingTemplateState state)
{
    public readonly int Id = id;
    public readonly NetUserId SharedBy = sharedBy;
    public readonly string SharedByName = sharedByName;
    public readonly int Width = width;
    public readonly int Height = height;
    public readonly int Bytes = bytes;
    public readonly WFMappingTemplateState State = state;
}

/// <summary>
/// Shares a PNG of <see cref="Bytes"/> bytes in <see cref="ChunkCount"/> chunks with the player's session, placed at
/// <see cref="State"/>. The server answers <see cref="WFMappingTemplateUploadReadyEvent"/> or refuses.
/// </summary>
[Serializable, NetSerializable]
public sealed class WFMappingTemplateUploadBeginRequest(int bytes, int chunkCount, WFMappingTemplateState state)
    : EntityEventArgs
{
    public readonly int Bytes = bytes;
    public readonly int ChunkCount = chunkCount;
    public readonly WFMappingTemplateState State = state;
}

/// <summary>Server to client: the share is accepted; send its chunks under this transfer id.</summary>
[Serializable, NetSerializable]
public sealed class WFMappingTemplateUploadReadyEvent(int transferId) : EntityEventArgs
{
    public readonly int TransferId = transferId;
}

/// <summary>Client to server: one chunk of the PNG being shared.</summary>
[Serializable, NetSerializable]
public sealed class WFMappingTemplateUploadChunkEvent(int transferId, int chunk, byte[] data) : EntityEventArgs
{
    public readonly int TransferId = transferId;
    public readonly int Chunk = chunk;
    public readonly byte[] Data = data;
}

/// <summary>Server to client: a chunk arrived; the client sends the next.</summary>
[Serializable, NetSerializable]
public sealed class WFMappingTemplateUploadAckEvent(int transferId, int chunk) : EntityEventArgs
{
    public readonly int TransferId = transferId;
    public readonly int Chunk = chunk;
}

/// <summary>Server to client: the PNG is now the session's shared template <see cref="TemplateId"/>.</summary>
[Serializable, NetSerializable]
public sealed class WFMappingTemplateUploadDoneEvent(int transferId, int templateId, WFMappingTemplateState state)
    : EntityEventArgs
{
    public readonly int TransferId = transferId;
    public readonly int TemplateId = templateId;
    public readonly WFMappingTemplateState State = state;
}

/// <summary>Stops the player's running share.</summary>
[Serializable, NetSerializable]
public sealed class WFMappingTemplateUploadCancelRequest(int transferId) : EntityEventArgs
{
    public readonly int TransferId = transferId;
}

/// <summary>
/// Server to client: the share was refused or stopped; 0 for a refused begin. A <see cref="WFMappingResultEvent"/>
/// says why.
/// </summary>
[Serializable, NetSerializable]
public sealed class WFMappingTemplateUploadCancelledEvent(int transferId) : EntityEventArgs
{
    public readonly int TransferId = transferId;
}

/// <summary>Server to client: one chunk of the session's shared template, <see cref="Bytes"/> bytes in all.</summary>
[Serializable, NetSerializable]
public sealed class WFMappingTemplateDownloadChunkEvent(int templateId, int bytes, int chunkCount, int chunk,
    byte[] data) : EntityEventArgs
{
    public readonly int TemplateId = templateId;
    public readonly int Bytes = bytes;
    public readonly int ChunkCount = chunkCount;
    public readonly int Chunk = chunk;
    public readonly byte[] Data = data;
}

/// <summary>Client to server: a chunk of the shared template arrived.</summary>
[Serializable, NetSerializable]
public sealed class WFMappingTemplateDownloadAckEvent(int templateId, int chunk) : EntityEventArgs
{
    public readonly int TemplateId = templateId;
    public readonly int Chunk = chunk;
}

/// <summary>Moves, scales or turns the session's shared template; the server clamps it.</summary>
[Serializable, NetSerializable]
public sealed class WFMappingTemplateTransformRequest(int templateId, WFMappingTemplateState state) : EntityEventArgs
{
    public readonly int TemplateId = templateId;
    public readonly WFMappingTemplateState State = state;
}

/// <summary>Server to client: where the shared template sits now.</summary>
[Serializable, NetSerializable]
public sealed class WFMappingTemplateTransformEvent(int templateId, WFMappingTemplateState state) : EntityEventArgs
{
    public readonly int TemplateId = templateId;
    public readonly WFMappingTemplateState State = state;
}

/// <summary>Removes the session's shared template for everyone, if it is still <see cref="TemplateId"/>.</summary>
[Serializable, NetSerializable]
public sealed class WFMappingTemplateUnshareRequest(int templateId) : EntityEventArgs
{
    public readonly int TemplateId = templateId;
}
