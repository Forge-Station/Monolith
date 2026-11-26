using System.Numerics;
using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.MappingTools;

/// <summary>
/// A mapping-tools selection on one grid: a tile area (right and top exclusive), single cells picked on top of it
/// and/or loose entities. A cell holds its tile and everything on it, as the area does.
/// </summary>
[Serializable, NetSerializable]
public sealed class MappingSelection
{
    public NetEntity Grid;
    public Box2i? Area;
    public List<Vector2i> Cells = new();
    public List<NetEntity> Entities = new();
}

/// <summary>
/// Moves the selection by whole tiles and quarter turns clockwise around its centre.
/// </summary>
[Serializable, NetSerializable]
public sealed class MappingToolsMoveEvent(MappingSelection selection, Vector2i offset, int turns) : EntityEventArgs
{
    public readonly MappingSelection Selection = selection;
    public readonly Vector2i Offset = offset;
    public readonly int Turns = turns;
}

/// <summary>
/// Mirrors the selection in place, left-right or with <see cref="Vertical"/> top-bottom.
/// </summary>
[Serializable, NetSerializable]
public sealed class MappingToolsMirrorEvent(MappingSelection selection, bool vertical) : EntityEventArgs
{
    public readonly MappingSelection Selection = selection;
    public readonly bool Vertical = vertical;
}

/// <summary>
/// Copies the selection to the sender's clipboard, deleting it when <see cref="Cut"/> is set.
/// </summary>
[Serializable, NetSerializable]
public sealed class MappingToolsCopyEvent(MappingSelection selection, bool cut) : EntityEventArgs
{
    public readonly MappingSelection Selection = selection;
    public readonly bool Cut = cut;
}

/// <summary>
/// Pastes the sender's clipboard centred on <see cref="Target"/>, turned clockwise by <see cref="Turns"/>.
/// </summary>
[Serializable, NetSerializable]
public sealed class MappingToolsPasteEvent(NetCoordinates target, int turns) : EntityEventArgs
{
    public readonly NetCoordinates Target = target;
    public readonly int Turns = turns;
}

/// <summary>
/// Deletes the selection, tiles included.
/// </summary>
[Serializable, NetSerializable]
public sealed class MappingToolsDeleteEvent(MappingSelection selection) : EntityEventArgs
{
    public readonly MappingSelection Selection = selection;
}

/// <summary>
/// Undoes (or with <see cref="Redo"/>, redoes) the sender's last mapping edit.
/// </summary>
[Serializable, NetSerializable]
public sealed class MappingToolsHistoryEvent(bool redo) : EntityEventArgs
{
    public readonly bool Redo = redo;
}

/// <summary>
/// Server to client: what the clipboard holds, for the paste preview. Positions are relative to its bottom-left.
/// A paste off every grid lands on <see cref="SourceGrid"/>, the grid it was copied from.
/// </summary>
[Serializable, NetSerializable]
public sealed class MappingToolsClipboardEvent(
    NetEntity sourceGrid,
    Vector2i size,
    List<Vector2i> tiles,
    List<MappingClipEntity> entities)
    : EntityEventArgs
{
    public readonly NetEntity SourceGrid = sourceGrid;
    public readonly Vector2i Size = size;
    public readonly List<Vector2i> Tiles = tiles;
    public readonly List<MappingClipEntity> Entities = entities;
}

/// <summary>
/// Server to client: replaces the client's selection, sent after a paste.
/// </summary>
[Serializable, NetSerializable]
public sealed class MappingToolsSelectEvent(MappingSelection selection) : EntityEventArgs
{
    public readonly MappingSelection Selection = selection;
}

/// <summary>
/// One root entity in the clipboard preview.
/// </summary>
[Serializable, NetSerializable]
public readonly record struct MappingClipEntity(string? Prototype, Vector2 Position, Angle Rotation);

/// <summary>What a <see cref="MappingToolsDrawEvent"/> does to its cells.</summary>
[Serializable, NetSerializable]
public enum MappingDrawOp : byte
{
    /// <summary>Lays <see cref="MappingToolsDrawEvent.TileId"/> on every cell; Space clears them.</summary>
    SetTiles,

    /// <summary>Spawns <see cref="MappingToolsDrawEvent.Prototype"/> at every cell's centre.</summary>
    SpawnEntities,

    /// <summary>Removes the decals whose centre falls in a cell.</summary>
    EraseDecals,

    /// <summary>Lays a pipe along the cells, a path in order, as <see cref="MappingToolsDrawEvent.Pipes"/> says.</summary>
    Pipes,

    /// <summary>Paints the pipe network at the one cell with the colour in <see cref="MappingToolsDrawEvent.Pipes"/>.</summary>
    PipeColor,

    /// <summary>
    /// Removes the entities whose position falls in a cell, never players, ghosts or observers. With no grid the
    /// cells are map tiles on <see cref="MappingToolsDrawEvent.Map"/>.
    /// </summary>
    EraseEntities,
    /// <summary>
    /// Places the atmos device <see cref="MappingToolsDrawEvent.Prototype"/>, one of
    /// <see cref="MappingToolsPipeDevices.Ids"/>, on the one cell as <see cref="MappingToolsDrawEvent.Pipes"/> says.
    /// </summary>
    PipeDevice,
}

/// <summary>
/// A shape, fill or area erase from the mapping screen: one operation over grid-local cells, checked and undone as
/// one edit. With no grid, <see cref="MappingDrawOp.SetTiles"/> makes a new grid on <see cref="Map"/> over those map
/// cells.
/// </summary>
[Serializable, NetSerializable]
public sealed class MappingToolsDrawEvent(
    NetEntity? grid,
    MappingDrawOp op,
    List<Vector2i> cells,
    string? tileId,
    byte tileRotationMirroring,
    string? prototype,
    Angle rotation)
    : EntityEventArgs
{
    public readonly NetEntity? Grid = grid;
    public readonly MappingDrawOp Op = op;
    public readonly List<Vector2i> Cells = cells;
    public readonly string? TileId = tileId;
    public readonly byte TileRotationMirroring = tileRotationMirroring;
    public readonly string? Prototype = prototype;
    public readonly Angle Rotation = rotation;

    /// <summary>The map a draw with no grid goes on.</summary>
    public NetEntity? Map;

    /// <summary>The layer, colour and joining of a pipe draw or network paint.</summary>
    public WFMappingPipeSpec? Pipes;
}
