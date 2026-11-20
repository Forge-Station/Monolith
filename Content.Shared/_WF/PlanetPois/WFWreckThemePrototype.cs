using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared.EntityTable.EntitySelectors;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.PlanetPois;

/// <summary>How a world reclaims the ships that crashed on it: what creeps over their decks and what lives in them.</summary>
[Prototype("wfWreckTheme")]
public sealed partial class WFWreckThemePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>The world's ground, creeping over the deck.</summary>
    [DataField]
    public string? FloorTile;

    /// <summary>Share of deck tiles the ground has taken.</summary>
    [DataField]
    public float FloorChance = 0.3f;

    /// <summary>What grows or settles on the deck.</summary>
    [DataField]
    public List<EntProtoId> Overgrowth = new();

    /// <summary>Share of open deck tiles with something grown on them.</summary>
    [DataField]
    public float OvergrowthChance = 0.08f;

    /// <summary>The wildlife that moved in.</summary>
    [DataField]
    public EntityTableSelector? Fauna;

    [DataField]
    public int MinFauna = 1;

    [DataField]
    public int MaxFauna = 3;

    /// <summary>What's left of the crew, risen as zombies; one pick per survivor.</summary>
    [DataField]
    public EntityTableSelector? Crew;

    [DataField]
    public int MinCrew = 1;

    [DataField]
    public int MaxCrew = 3;

    /// <summary>What may still be in the hold, such as salvaged ship guns; one pick, at <see cref="LootChance"/>.</summary>
    [DataField]
    public EntityTableSelector? Loot;

    [DataField]
    public float LootChance = 0.5f;
}

/// <summary>The crashed ships a world gets each round.</summary>
[DataDefinition]
public sealed partial class WFPlanetWrecks
{
    [DataField(required: true)]
    public ProtoId<WFWreckThemePrototype> Theme;

    [DataField]
    public int MinCount = 1;

    [DataField]
    public int MaxCount = 2;

    /// <summary>How close to the world's edge a wreck may lie, in tiles; the table's edgeMargin when unset.</summary>
    [DataField]
    public int? EdgeMargin;

    /// <summary>Which shipyard sizes crash; large ships don't fit between the sites.</summary>
    [DataField]
    public List<VesselSize> Sizes = new() { VesselSize.Small, VesselSize.Medium };

    /// <summary>Share of the hull torn away where it hit, as a fraction of its shorter side.</summary>
    [DataField]
    public float Breach = 0.3f;
}
