using Content.Shared.Decals;
using Content.Shared.Maps;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype.Array;

namespace Content.Shared._WF.Encounters;

/// <summary>
/// Pirate retrofitting: what a crew of raiders does to a hull they took. Random guns on its hardpoints, and grime,
/// rust, cobwebs and dead lights through its decks.
/// </summary>
[Prototype("wfPirateRetrofit")]
public sealed partial class WFPirateRetrofitPrototype : IPrototype, IInheritingPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [ParentDataField(typeof(AbstractPrototypeIdArraySerializer<WFPirateRetrofitPrototype>))]
    public string[]? Parents { get; private set; }

    [NeverPushInheritance]
    [AbstractDataField]
    public bool Abstract { get; private set; }

    /// <summary>
    /// Ship guns it may mount, each only on a hardpoint of its class and size or larger. A mapped gun not listed is
    /// pulled. Empty leaves the hull's guns as they are.
    /// </summary>
    [DataField]
    public List<EntProtoId> Guns = new();

    /// <summary>How many guns it ends up with, picked between the two, as far as its hardpoints and fire control allow.</summary>
    [DataField]
    public int MinGuns;

    [DataField]
    public int MaxGuns;

    [DataField]
    public List<WFPirateRetrofitTileSwap> TileSwaps = new();

    /// <summary>Walls swapped for their rusted version.</summary>
    [DataField]
    public Dictionary<EntProtoId, EntProtoId> WallSwaps = new();

    /// <summary>The share of swappable walls that rust.</summary>
    [DataField]
    public float WallChance;

    [DataField]
    public List<WFPirateRetrofitDecal> Decals = new();

    /// <summary>Decals laid per floor tile, on average.</summary>
    [DataField]
    public float DecalDensity;

    /// <summary>The chance a floor tile in the corner of two walls gets a cobweb.</summary>
    [DataField]
    public float CobwebChance;

    /// <summary>Cobwebs for a corner with walls north and west, and north and east; turned for the corners below.</summary>
    [DataField]
    public EntProtoId CobwebWest = "Cobweb1";

    [DataField]
    public EntProtoId CobwebEast = "Cobweb2";

    /// <summary>The share of light fixtures left without a bulb.</summary>
    [DataField]
    public float LightsOut;

    [DataField]
    public List<EntProtoId> Litter = new();

    /// <summary>Pieces of litter per floor tile, on average.</summary>
    [DataField]
    public float LitterDensity;
}

/// <summary>Floor tiles of some kinds, each swapped by a chance for one of the worn ones.</summary>
[DataDefinition]
public sealed partial class WFPirateRetrofitTileSwap
{
    [DataField(required: true)]
    public List<ProtoId<ContentTileDefinition>> From = new();

    [DataField(required: true)]
    public List<ProtoId<ContentTileDefinition>> To = new();

    [DataField]
    public float Chance = 0.3f;
}

/// <summary>A decal laid at random, with its tint and its weight against the others.</summary>
[DataDefinition]
public sealed partial class WFPirateRetrofitDecal
{
    [DataField(required: true)]
    public ProtoId<DecalPrototype> Id;

    [DataField]
    public Color? Color;

    [DataField]
    public float Weight = 1f;
}
