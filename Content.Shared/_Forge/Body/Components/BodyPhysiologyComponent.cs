using Robust.Shared.GameObjects;
using Robust.Shared.Serialization.Manager.Attributes;
using Content.Shared._Forge.Body.Syndromes;

namespace Content.Shared._Forge.Body.Components;

/// <summary>
/// Stores the doll's diseases and tolerances,
/// diseases with <see cref="Organ.Body"/> stored here
/// </summary>
[RegisterComponent]
public sealed partial class BodyPhysiologyComponent : Component, IPhysiologyHolder
{
    [DataField]
    public Dictionary<string, SyndromeData> Syndromes { get; set; } = new();

    [DataField]
    public Dictionary<string, ToleranceData> Tolerances { get; set; } = new();
}
