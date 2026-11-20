using Content.Shared.Mining;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.AsteroidMining;

/// <summary>What a kind of asteroid can hold and how hard it is to crack.</summary>
[Prototype("wfAsteroidComposition")]
public sealed partial class WFAsteroidCompositionPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;

    [DataField(required: true)] public List<WFAsteroidCompositionEntry> Ores = new();

    [DataField] public int MinOres = 1;
    [DataField] public int MaxOres = 3;

    /// <summary>Share of the rock that holds no ore.</summary>
    [DataField] public float MinInert = 0.3f;
    [DataField] public float MaxInert = 0.6f;

    /// <summary>Multiplies the ore each vein drops, on top of the rock's own yield.</summary>
    [DataField] public float OreYield = 1f;

    [DataField] public float MinResistance = 0.05f;
    [DataField] public float MaxResistance = 0.35f;
    [DataField] public float MinInstability = 0.1f;
    [DataField] public float MaxInstability = 0.5f;

    /// <summary>Tint of the rock body, for kinds that should stand out from plain rock.</summary>
    [DataField] public Color RockColor = Color.White;

    /// <summary>Which of the rock's <see cref="WFAsteroidComponent.Looks"/> this kind uses.</summary>
    [DataField] public string? Look;

    /// <summary>Name for rocks of this kind, given the rock's own name as <c>$name</c>.</summary>
    [DataField] public LocId? Name;

    /// <summary>An extra examine line for rocks of this kind.</summary>
    [DataField] public LocId? Examine;
}

[DataDefinition]
public sealed partial class WFAsteroidCompositionEntry
{
    /// <summary>The ore vein each unit of mass yields, so asteroid yields match vein rocks.</summary>
    [DataField(required: true)] public ProtoId<OrePrototype> Ore;

    [DataField] public float Weight = 1f;

    /// <summary>Tint of the rock's veins and its radar blip when this ore leads.</summary>
    [DataField] public Color Color = Color.White;

    /// <summary>How much this ore feeds an overcharge blast, per share of the rock.</summary>
    [DataField] public float Volatility;
}
