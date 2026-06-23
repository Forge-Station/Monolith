using Robust.Shared.GameObjects;
using Robust.Shared.Serialization.Manager.Attributes;
using Content.Shared._Forge.Body.Syndromes;

namespace Content.Shared._Forge.Body.Components;

/// <summary>
/// Stores the disease and tolerance of a <see cref="Organ"/>
/// </summary>
[RegisterComponent]
public sealed partial class OrganPhysiologyComponent : Component, IPhysiologyHolder
{
    // Organ type to match with <see cref="Organ"/> in the disease prototype.
    [DataField(required: true)]
    public Organ Organ { get; set; } = Organ.None;

    [DataField]
    public Dictionary<string, SyndromeData> Syndromes { get; set; } = new();

    [DataField]
    public Dictionary<string, ToleranceData> Tolerances { get; set; } = new();
}
