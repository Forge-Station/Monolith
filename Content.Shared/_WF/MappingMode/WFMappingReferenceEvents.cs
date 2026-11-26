using System.Numerics;
using Content.Shared._WF.MappingTools;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.MappingMode;

/// <summary>Asks for the files a mapper can load as a reference grid.</summary>
[Serializable, NetSerializable]
public sealed class WFMappingReferenceListRequest : EntityEventArgs;

/// <summary>Server to client: the files a mapper can load as a reference grid.</summary>
[Serializable, NetSerializable]
public sealed class WFMappingReferenceListEvent(List<MappingMapFile> files) : EntityEventArgs
{
    public readonly List<MappingMapFile> Files = files;
}

/// <summary>Asks for a listed file's floor plan, read without loading the file.</summary>
[Serializable, NetSerializable]
public sealed class WFMappingReferencePreviewRequest(string path) : EntityEventArgs
{
    public readonly string Path = path;
}

/// <summary>One grid of a floor plan, placed as in the file; the rectangles are in its tile cells.</summary>
[Serializable, NetSerializable]
public sealed class WFMappingPreviewGrid(Vector2 offset, Angle rotation, List<Box2i> floor, List<Box2i> walls,
    List<Box2i> openings)
{
    public readonly Vector2 Offset = offset;
    public readonly Angle Rotation = rotation;
    public readonly List<Box2i> Floor = floor;
    public readonly List<Box2i> Walls = walls;

    /// <summary>Windows and doors.</summary>
    public readonly List<Box2i> Openings = openings;
}

/// <summary>A reference file's floor plan and size.</summary>
[Serializable, NetSerializable]
public sealed class WFMappingReferencePreview(List<WFMappingPreviewGrid> grids, int tiles, int entities, long bytes)
{
    public readonly List<WFMappingPreviewGrid> Grids = grids;
    public readonly int Tiles = tiles;
    public readonly int Entities = entities;
    public readonly long Bytes = bytes;
}

/// <summary>Server to client: a file's floor plan, or why there is none.</summary>
[Serializable, NetSerializable]
public sealed class WFMappingReferencePreviewEvent(string path, WFMappingReferencePreview? preview, string? error)
    : EntityEventArgs
{
    public readonly string Path = path;
    public readonly WFMappingReferencePreview? Preview = preview;
    public readonly string? Error = error;
}

/// <summary>Loads a listed file aside and asks for its footprint, to place it.</summary>
[Serializable, NetSerializable]
public sealed class WFMappingReferencePickRequest(string path) : EntityEventArgs
{
    public readonly string Path = path;
}

/// <summary>
/// Server to client: the footprint of a picked reference, relative to its anchor, under a token that confirms or
/// cancels it.
/// </summary>
[Serializable, NetSerializable]
public sealed class WFMappingReferenceFootprintEvent(int token, List<WFMappingFootprintGrid> grids) : EntityEventArgs
{
    public readonly int Token = token;
    public readonly List<WFMappingFootprintGrid> Grids = grids;
}

/// <summary>
/// Places the picked reference with its anchor at <see cref="Offset"/> on the mapper's level, turned clockwise by
/// <see cref="Turns"/> quarter turns.
/// </summary>
[Serializable, NetSerializable]
public sealed class WFMappingReferenceConfirmRequest(int token, Vector2 offset, int turns) : EntityEventArgs
{
    public readonly int Token = token;
    public readonly Vector2 Offset = offset;
    public readonly int Turns = turns;
}

/// <summary>Drops the picked reference.</summary>
[Serializable, NetSerializable]
public sealed class WFMappingReferenceCancelRequest(int token) : EntityEventArgs
{
    public readonly int Token = token;
}
