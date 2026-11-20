using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Roadmap;

/// <summary>
/// One column of the roadmap window: a heading and its items, top to bottom.
/// </summary>
[Prototype]
public sealed partial class RoadmapColumnPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;

    [DataField(required: true)] public LocId Name;

    /// <summary>Column order, left to right.</summary>
    [DataField] public int Order;

    [DataField] public List<RoadmapEntry> Items = new();
}

/// <summary>
/// One roadmap item: a title, an optional description shown when expanded, and how far along it is.
/// </summary>
[DataDefinition]
public sealed partial class RoadmapEntry
{
    [DataField(required: true)] public LocId Name;

    /// <summary>Markup shown when the item is expanded.</summary>
    [DataField] public LocId? Description;

    [DataField] public RoadmapItemState State = RoadmapItemState.Planned;
}
