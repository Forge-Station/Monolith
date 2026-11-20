using System.Numerics;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Drydock;

/// <summary>What a drydock work item does to the ship.</summary>
public enum WFDrydockWorkKind : byte
{
    Tile,
    Entity,
    Heal,
    Upgrade,
    Break,
}

/// <summary>
/// One unit of drydock work: a hull tile, a missing entity, a heal, a refit tier, or a hull tile to break.
/// </summary>
public sealed class WFDrydockWorkItem
{
    /// <summary>What this item does.</summary>
    public WFDrydockWorkKind Kind;

    /// <summary>Ship grid index of a hull tile.</summary>
    public Vector2i TileIndex;

    /// <summary>Stored tile type to restore.</summary>
    public int TileType;

    /// <summary>Repair snapshot chunk of a rebuild's entity spec.</summary>
    public Vector2i ChunkPos;

    /// <summary>Key of the entity spec in its chunk.</summary>
    public int SpecId;

    /// <summary>Entity to heal or refit.</summary>
    public EntityUid Target;

    /// <summary>Refit prototype id of a refit item.</summary>
    public string? Upgrade;

    /// <summary>Tier a refit item brings its target to.</summary>
    public int Tier;

    /// <summary>Prototype a rebuild or heal works on, for its construction materials.</summary>
    public EntProtoId? Prototype;

    /// <summary>Whether a tile or rebuild restores what a hull break stripped, so it costs its full materials.</summary>
    public bool Broken;

    /// <summary>Whether a rebuild mounts on a hardpoint, so it is built after the other parts.</summary>
    public bool Mounted;

    /// <summary>Share of a full rebuild a rebuild or heal takes, after any repair cap.</summary>
    public float Share = 1f;

    /// <summary>Ship-local position used for effects and crane targeting.</summary>
    public Vector2 LocalPos;

    /// <summary>Credits charged for this item.</summary>
    public int Cost;

    /// <summary>Share of the cost for refits, charged outside the repair cap: all of a refit item's, or a rebuild's restore.</summary>
    public int RefitCost;

    /// <summary>Tier materials of that refit share, for a console that pays in materials.</summary>
    public Dictionary<string, int>? RefitMaterials;

    /// <summary>Refit units this item pays for in full, which parts from a parts silo can pay for instead of materials.</summary>
    public List<WFDrydockRefitUnits>? RefitUnits;

    /// <summary>Material bill when quoted by a console that pays in materials, or null.</summary>
    public Dictionary<string, int>? Materials;

    /// <summary>Parts taken from a parts silo in place of materials, by prototype, or null.</summary>
    public Dictionary<string, int>? Parts;

    /// <summary>Seconds one crane spends on this item, set when the job starts.</summary>
    public float WorkTime;
}

/// <summary>Units of a refit line an item brings to a tier, paid in full, by the tier each reaches now.</summary>
public sealed record WFDrydockRefitUnits(string Refit, int Tier, int[] Full);
