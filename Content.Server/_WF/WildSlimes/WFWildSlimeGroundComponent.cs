using System.Numerics;
using Content.Shared._WF.WildSlimes;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.WildSlimes;

/// <summary>A planet ground's wild slime patches, found from its biome when the world is built.</summary>
[RegisterComponent, UnsavedComponent]
public sealed partial class WFWildSlimeGroundComponent : Component
{
    [ViewVariables]
    public ProtoId<WFWildSlimeHabitatPrototype> Habitat;

    /// <summary>The cavern map under the ground, if the world has one.</summary>
    [ViewVariables]
    public EntityUid? Cavern;

    [ViewVariables]
    public List<WFWildSlimePatch> Patches = new();

    [ViewVariables]
    public TimeSpan NextUpdate;
}

/// <summary>One surface patch, the grotto under it and the slimes that live there.</summary>
public sealed class WFWildSlimePatch
{
    /// <summary>The patch tile nearest its middle; the shaft down opens here.</summary>
    public Vector2i Centre;

    /// <summary>Rough radius in tiles.</summary>
    public float Radius;

    /// <summary>Every patch tile on the ground.</summary>
    public List<Vector2i> Tiles = new();

    /// <summary>Every grotto tile in the cavern under and round it.</summary>
    public List<Vector2i> Grotto = new();

    /// <summary>The slimes on the patch.</summary>
    public List<WFWildSlimeSlot> Surface = new();

    /// <summary>The slimes in the grotto.</summary>
    public List<WFWildSlimeSlot> Below = new();

    /// <summary>When nobody was last near, or null while someone is.</summary>
    public TimeSpan? IdleSince;

    public Vector2 Middle => new(Centre.X + 0.5f, Centre.Y + 0.5f);
}

/// <summary>One place for a wild slime: who fills it, or when it may be filled again.</summary>
public sealed class WFWildSlimeSlot
{
    public EntityUid? Slime;
    public TimeSpan ReadyAt;
}
