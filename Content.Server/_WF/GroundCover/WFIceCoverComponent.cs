using Content.Shared._WF.GroundCover;

namespace Content.Server._WF.GroundCover;

/// <summary>Ice on a grid's standing water, by snow cover chunk; a chunk with no water is kept as null.</summary>
[RegisterComponent]
public sealed partial class WFIceCoverComponent : Component
{
    [ViewVariables]
    public readonly Dictionary<Vector2i, WFIceChunk?> Chunks = new();
}

/// <summary>One chunk's water and how far its ice has spread in from the shore, or thawed back out.</summary>
public sealed class WFIceChunk
{
    /// <summary>Each water tile's water entity, by its index in the chunk.</summary>
    public readonly Dictionary<int, EntityUid> Waters = new();

    /// <summary>Tiles from each water tile to the nearest shore: 1 at the edge, 0 where there's no water.</summary>
    public readonly byte[] Shore = new byte[WFSnowCover.ChunkArea];

    /// <summary>The freeze and thaw points of the chunk's water, taken from its first water tile.</summary>
    public float FreezeAt = WFFreezableWaterComponent.DefaultFreezeAt;

    public float ThawAt = WFFreezableWaterComponent.DefaultThawAt;

    /// <summary>Water tiles this far from the shore or nearer are frozen, unless thawed.</summary>
    public float Front;

    /// <summary>Frozen tiles this far from the shore or nearer have thawed again.</summary>
    public float Thaw;

    /// <summary>How thick the ice has grown, in degree-minutes of frost; warmth must melt it before the shore thaws.</summary>
    public float Thickness;

    /// <summary>Seconds the air has held below freezing, and above thawing.</summary>
    public float Cold;

    public float Warm;

    public TimeSpan Updated;

    /// <summary>Set when water spawns or goes, so the chunk is looked over again.</summary>
    public bool Stale;

    /// <summary>Holes broken in the ice and the seconds until each freezes over, by tile index.</summary>
    public Dictionary<int, float>? Holes;

    public bool IsFrozen(int index)
    {
        var shore = Shore[index];
        if (shore == 0 || shore > Front || shore <= Thaw)
            return false;

        return Holes == null || !Holes.ContainsKey(index);
    }
}
