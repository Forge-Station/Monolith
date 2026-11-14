using Content.Shared._WF.Encounters;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Remade.Sector;

/// <summary>A place a Monolith Scion is put into the sector: which encounter holds it, on which ship, and how far gone it is.</summary>
[Prototype("wfRemadeSite")]
public sealed partial class WFRemadeSitePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public ProtoId<WFEncounterPrototype> Encounter;

    /// <summary>Key of the encounter ship the Scion is put aboard.</summary>
    [DataField]
    public string Ship = "hulk";

    /// <summary>Key of the Choir's ship in the encounter, if it has one; it hunts whoever takes the Scion.</summary>
    [DataField]
    public string? Choir;

    [DataField]
    public WFRemadeSiteKind Kind;

    /// <summary>Weight among the sites the discovery rule picks from.</summary>
    [DataField]
    public float Weight = 1f;

    [DataField]
    public EntProtoId Scion = "WFMonolithScion";

    /// <summary>Resonance the Scion starts with.</summary>
    [DataField]
    public float Resonance = 1f;
}

public enum WFRemadeSiteKind : byte
{
    /// <summary>A dormant Scion the crew has to go and find.</summary>
    Discovery,

    /// <summary>A hull already deep in an outbreak, calling for help.</summary>
    Adrift,
}
