using Content.Shared.Humanoid;
using Content.Shared.Roles;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Remade;

/// <summary>One kind of Remade: what the Director pays for it and how often a corpse rises as it.</summary>
[Prototype("wfRemadeMob")]
public sealed partial class WFRemadeMobPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public EntProtoId Entity;

    /// <summary>Biomass the Director pays to send one.</summary>
    [DataField]
    public float Cost = 50f;

    /// <summary>The first stage the Director sends it at.</summary>
    [DataField]
    public WFMonolithStage MinStage = WFMonolithStage.Growth;

    /// <summary>Weight in the Director's pick. 0 means the Director never sends it.</summary>
    [DataField]
    public float SpawnWeight;

    /// <summary>The most of it one Monolith keeps at once. 0 means no limit.</summary>
    [DataField]
    public int MaxAlive;

    /// <summary>Only send it when corpses lie near the prey.</summary>
    [DataField]
    public bool NeedsCorpses;

    /// <summary>Weight in a corpse's conversion. 0 means no corpse rises as it. Numbers from DS13-1.0's conversion.dm.</summary>
    [DataField]
    public float ConversionWeight;

    /// <summary>Conversion weight is multiplied by compatibility to this power: 1 favours good corpses, -1 poor ones.</summary>
    [DataField]
    public float CompatibilityPower = 1f;

    /// <summary>Only a corpse of this sex turns into it. A corpse with no sex counts as either.</summary>
    [DataField]
    public Sex? ConversionSex;

    /// <summary>Only a corpse in light armour (or none) that still has both legs turns into it.</summary>
    [DataField]
    public bool LightArmourAndLegs;

    /// <summary>Only a corpse with at least this much poison damage turns into it. 0 means any.</summary>
    [DataField]
    public float MinPoison;

    /// <summary>Only a corpse whose job is in one of these departments turns into it. Empty means any.</summary>
    [DataField]
    public List<ProtoId<DepartmentPrototype>> Departments = new();

    /// <summary>The conversion weight of a mob that needs a department, for a corpse whose job can't be found.</summary>
    [DataField]
    public float UnknownJobWeight;

    /// <summary>Weight in the conversion of a dead animal that weighs between <see cref="AnimalMinMass"/> and <see cref="AnimalMaxMass"/>. 0 means no animal rises as it.</summary>
    [DataField]
    public float AnimalWeight;

    /// <summary>The least an animal's fixtures weigh, in kilograms, for it to rise as this.</summary>
    [DataField]
    public float AnimalMinMass;

    /// <summary>The most an animal's fixtures weigh for it to rise as this. 0 means no limit.</summary>
    [DataField]
    public float AnimalMaxMass;

    /// <summary>Whether some corpses can't turn into it.</summary>
    public bool HasConversionConditions => ConversionSex != null || LightArmourAndLegs || MinPoison > 0f || Departments.Count > 0;
}
