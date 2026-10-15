// Forge-Change-full: prototype for the entity-replace dungeon post-gen.
using Content.Shared.Maps;
using Robust.Shared.Prototypes;

namespace Content.Shared.Procedural.PostGeneration;

/// <summary>
/// Swaps anchored entities inside dungeon rooms for another prototype and can repaint the room floors.
/// </summary>
public sealed partial class EntityReplaceDunGen : IDunGenLayer
{
    [DataField(required: true)]
    public Dictionary<EntProtoId, EntProtoId> Replacements = new();

    /// <summary>
    /// Prototypes deleted instead of swapped. Used for grilles sitting under a window.
    /// </summary>
    [DataField]
    public List<EntProtoId> Removals = new();

    /// <summary>
    /// If set, every room tile is painted with this floor.
    /// </summary>
    [DataField]
    public ProtoId<ContentTileDefinition>? Tile;

    /// <summary>
    /// Spawned on each free corridor tile. Xenomorph growth you have to push through.
    /// </summary>
    [DataField]
    public EntProtoId? CorridorEntity;
}
