using Content.Shared.Decals;
using Content.Shared.Maps;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.PlanetPois;

/// <summary>What stands on and beside a world's roads: kerbs, lane lines, street lights and scattered traffic.</summary>
[Prototype("wfRoadDressing")]
public sealed partial class WFRoadDressingPrototype : IPrototype
{
    /// <inheritdoc/>
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>Shoulder tiles by where the road lies from them; a case left out keeps the network's shoulder tile.</summary>
    [DataField]
    public Dictionary<WFRoadKerb, ProtoId<ContentTileDefinition>> Kerbs = new();

    /// <summary>Decal down the middle of a straight road running north to south, and the one for east to west.</summary>
    [DataField]
    public ProtoId<DecalPrototype>? LineNorthSouth;

    [DataField]
    public ProtoId<DecalPrototype>? LineEastWest;

    /// <summary>Every how many tiles the middle line is drawn; 2 gives a dashed line.</summary>
    [DataField]
    public int LineEvery = 2;

    /// <summary>The street light, set on the shoulder on alternating sides.</summary>
    [DataField]
    public EntProtoId? Light;

    /// <summary>What stands in for a light that has failed, and how often one has.</summary>
    [DataField]
    public EntProtoId? BrokenLight;

    [DataField]
    public float BrokenLightChance = 0.1f;

    /// <summary>Tiles of road between one street light and the next.</summary>
    [DataField]
    public int LightSpacing = 12;

    /// <summary>What stands on the corners where three or more roads meet.</summary>
    [DataField]
    public EntProtoId? JunctionLight;

    /// <summary>Things dropped at random along the straight stretches.</summary>
    [DataField]
    public List<WFRoadScatter> Scatter = new();

    [DataField]
    public List<WFRoadDecalScatter> Decals = new();
}

/// <summary>Where the road lies from a shoulder tile: on one side, on two that meet, or only across a corner.</summary>
public enum WFRoadKerb : byte
{
    North, South, East, West,
    NorthEast, NorthWest, SouthEast, SouthWest,
    InnerNorthEast, InnerNorthWest, InnerSouthEast, InnerSouthWest,
}

/// <summary>
/// Part of the road a piece goes on: any lane but the middle one, the lane against the kerb, or the shoulder. A piece
/// too wide for the kerb lane alone takes it and as many lanes inward as it needs.
/// </summary>
public enum WFRoadSpot : byte { Lane, Kerb, Shoulder }

/// <summary>The way a stretch of road runs.</summary>
public enum WFRoadAxis : byte { Any, NorthSouth, EastWest }

/// <summary>One kind of thing scattered along the roads.</summary>
[DataDefinition]
public sealed partial class WFRoadScatter
{
    /// <summary>What may be placed; one is picked each time.</summary>
    [DataField(required: true)]
    public List<EntProtoId> Prototypes = new();

    [DataField]
    public WFRoadSpot Where = WFRoadSpot.Lane;

    /// <summary>The only way a road may run for this to be placed on it, for sprites drawn from one side.</summary>
    [DataField]
    public WFRoadAxis Axis = WFRoadAxis.Any;

    /// <summary>Whether it is turned to point along the road.</summary>
    [DataField]
    public bool Align;

    /// <summary>How many per hundred tiles of straight road.</summary>
    [DataField]
    public float Per100 = 1f;

    /// <summary>Whether it is in the way: such pieces are kept apart, so the road always stays open.</summary>
    [DataField]
    public bool Blocks = true;
}

/// <summary>One kind of decal scattered along the roads.</summary>
[DataDefinition]
public sealed partial class WFRoadDecalScatter
{
    [DataField(required: true)]
    public List<ProtoId<DecalPrototype>> Decals = new();

    /// <summary>Lane here takes in the whole carriageway, the middle too; Kerb is the gutter, the lane's outer edge.</summary>
    [DataField]
    public WFRoadSpot Where = WFRoadSpot.Lane;

    [DataField]
    public WFRoadAxis Axis = WFRoadAxis.Any;

    [DataField]
    public float Per100 = 2f;
}
