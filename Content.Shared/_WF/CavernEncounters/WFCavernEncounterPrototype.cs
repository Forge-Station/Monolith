using Content.Shared._WF.Planets;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.CavernEncounters;

/// <summary>One encounter found in a cavern: a cleared room of the cavern's own floor, its props and its people or creatures.</summary>
[Prototype("wfCavernEncounter")]
public sealed partial class WFCavernEncounterPrototype : IPrototype
{
    /// <inheritdoc/>
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>Radius in tiles of the room cleared of rock round the spot.</summary>
    [DataField]
    public int Radius = 5;

    /// <summary>What is laid out in the room, in order; each entry takes a free tile of its own.</summary>
    [DataField(required: true)]
    public List<WFCavernEncounterEntry> Entries = new();

    /// <summary>
    /// Rings the room's centre with rock from the cavern's own walls, so whoever stands there is walled in until a
    /// player digs through (the trapped miner).
    /// </summary>
    [DataField]
    public bool Collapse;
}

/// <summary>One kind of thing an encounter lays out: a count picked from one or more prototypes, on tiles near the centre.</summary>
[DataDefinition]
public sealed partial class WFCavernEncounterEntry
{
    /// <summary>The prototypes to pick from, one pick per thing.</summary>
    [DataField]
    public List<EntProtoId> Ids = new();

    /// <summary>
    /// A prototype another module provides that may not exist yet: spawned only when it does. Kept as a plain id so
    /// the linter doesn't fail on it before then.
    /// </summary>
    [DataField]
    public string? Pending;

    /// <summary>The fewest to lay out.</summary>
    [DataField]
    public int Min = 1;

    /// <summary>The most to lay out.</summary>
    [DataField]
    public int Max = 1;

    /// <summary>The farthest from the centre, in tiles, a thing may go; 0 puts it on the centre.</summary>
    [DataField]
    public float Distance;

    /// <summary>The nearest to the centre, in tiles, a thing may go, such as lights at the room's edge.</summary>
    [DataField]
    public float MinDistance;

    /// <summary>Switches a handheld light on as it is laid out, such as a lantern left burning.</summary>
    [DataField]
    public bool Lit;

    /// <summary>A table set on a free tile beside each one, which it turns to face: a trader's counter.</summary>
    [DataField]
    public EntProtoId? Table;
}

/// <summary>
/// Which encounters a world's cavern holds and how many: a count that grows with the cavern's area, picked by weight,
/// each kind up to its limit.
/// </summary>
[Prototype("wfCavernEncounterTable")]
public sealed partial class WFCavernEncounterTablePrototype : IPrototype
{
    /// <inheritdoc/>
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>The surface whose cavern this is.</summary>
    [DataField(required: true)]
    public ProtoId<WFPlanetSurfacePrototype> Surface;

    /// <summary>Encounters per 10,000 square tiles of the ring they are planned in.</summary>
    [DataField]
    public float PerArea = 1.4f;

    /// <summary>The fewest encounters the cavern gets, however small.</summary>
    [DataField]
    public int MinCount = 3;

    /// <summary>The most encounters the cavern gets, however big.</summary>
    [DataField]
    public int MaxCount = 64;

    /// <summary>How many of the first spots go near the gate, where most players come down.</summary>
    [DataField]
    public int NearGate = 2;

    /// <summary>The encounters and their weights.</summary>
    [DataField(required: true)]
    public Dictionary<ProtoId<WFCavernEncounterPrototype>, float> Encounters = new();

    /// <summary>The most of one encounter a cavern holds; kinds not listed have no limit.</summary>
    [DataField]
    public Dictionary<ProtoId<WFCavernEncounterPrototype>, int> Limits = new();
}
