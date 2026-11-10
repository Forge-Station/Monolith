using Content.Shared.Chemistry.Reagent;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Remade.Corruption;

/// <summary>A tile of corruption, grown from a Monolith.</summary>
[RegisterComponent]
public sealed partial class WFCorruptionComponent : Component
{
    [ViewVariables]
    public EntityUid? Source;

    /// <summary>Chance per spread update that this tile grows into its free neighbours, before the Monolith's stage speeds it up.</summary>
    [DataField]
    public float SpreadChance = 0.2f;

    /// <summary>The node feeding this tile, if one is. Null means the Monolith pays for it.</summary>
    [ViewVariables]
    public EntityUid? Node;

    [ViewVariables]
    public EntityUid? Grid;

    /// <summary>When this tile dies after its Monolith has gone, if it has.</summary>
    [ViewVariables]
    public TimeSpan? DecayAt;

    /// <summary>Reagents a puddle on this tile loses to it.</summary>
    [DataField]
    public HashSet<ProtoId<ReagentPrototype>> Drinks = new();

    /// <summary>Units a second this tile drinks from a puddle on it.</summary>
    [DataField]
    public float DrinkRate = 0.5f;

    /// <summary>Biomass the Director gets for each unit drunk.</summary>
    [DataField]
    public float BiomassPerUnit = 0.1f;
}
