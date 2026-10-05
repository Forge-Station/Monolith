using Content.Shared.Maps;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Caverns;

/// <summary>How a cavern's mouths are placed, shaped and fitted out: the hole, its lip, the pad below and the climb point.</summary>
[DataDefinition]
public sealed partial class WFCavernMouthSpec
{
    /// <summary>Tiles per mouth cell; each cell holds at most one mouth.</summary>
    [DataField]
    public int CellSize = 96;

    /// <summary>How the hole is grown from its seed.</summary>
    [DataField]
    public WFCavernMouthStyle Style = WFCavernMouthStyle.Round;

    /// <summary>Fewest tiles a hole may have.</summary>
    [DataField]
    public int MinTiles = 4;

    /// <summary>Most tiles a hole may have.</summary>
    [DataField]
    public int MaxTiles = 9;

    /// <summary>Largest ratio of a round or blob hole's long axis to its short one; the axis turns at random.</summary>
    [DataField]
    public float Elongation = 1.2f;

    /// <summary>How far a round or blob hole's edge wanders in and out, as a fraction of its radius.</summary>
    [DataField]
    public float Roughness = 0.1f;

    /// <summary>A rift's widest stretch in tiles, 1 or 2.</summary>
    [DataField]
    public int RiftWidth = 2;

    /// <summary>The gate's first candidate, relative to the planet centre.</summary>
    [DataField]
    public Vector2i GateOffset = new(0, 24);

    /// <summary>Natural ground tiles a mouth may cut; every footprint tile must be one of them.</summary>
    [DataField(required: true)]
    public List<ProtoId<ContentTileDefinition>> GroundTiles = new();

    /// <summary>Natural ground entities a footprint must not touch, such as liquids and boulders.</summary>
    [DataField]
    public List<EntProtoId> Avoid = new();

    /// <summary>The cavern tile under the hole.</summary>
    [DataField(required: true)]
    public ProtoId<ContentTileDefinition> LandingTile;

    /// <summary>Anchored on every pad tile that is the landing tile or where the cavern grows it, such as a pool's water; open floor to the cavern check.</summary>
    [DataField]
    public EntProtoId? LandingEntity;

    /// <summary>How far the pinned, rock-free pad reaches around the hole in the cavern.</summary>
    [DataField]
    public int PadRadius = 3;

    /// <summary>The side of the hole whose lip holds the climb point.</summary>
    [DataField]
    public Direction ClimbSide = Direction.South;

    /// <summary>The unanchored pit entity over each hole tile.</summary>
    [DataField(required: true)]
    public EntProtoId Shade;

    /// <summary>The anchored climb entity in the cavern under the lip.</summary>
    [DataField(required: true)]
    public EntProtoId ClimbPoint;

    /// <summary>Decor anchored on random lip tiles, never on or beside the climb tile.</summary>
    [DataField]
    public List<EntProtoId> Rim = new();

    /// <summary>About how many rim decor entities a hole at the top of the size range gets; smaller ones get fewer, at least one.</summary>
    [DataField]
    public int RimCount = 3;

    /// <summary>Base climb-up time in seconds, before surface gravity scales it.</summary>
    [DataField]
    public float ClimbSeconds = 4f;

    /// <summary>Seconds a standard shovel takes to dig a shaft through ground it can't otherwise dig; a faster tool takes less.</summary>
    [DataField]
    public float ShaftSeconds = 15f;
}

/// <summary>How a mouth's hole is grown.</summary>
public enum WFCavernMouthStyle : byte
{
    /// <summary>A smooth ellipse: a moulin or a funnel.</summary>
    Round,

    /// <summary>An ellipse whose edge wanders in and out: a sinkhole or a skylight.</summary>
    Blob,

    /// <summary>A wandering crack one or two tiles wide.</summary>
    Rift,
}
