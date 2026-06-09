using Content.Shared._Forge.Physiology.Disease;
using Content.Shared._Forge.Physiology.Tolerance;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared._Forge.Physiology;

/// <summary>
/// Stores the doll's diseases and tolerances,
/// diseases with <see cref="Organ.Body"/> stored here
/// </summary>
[RegisterComponent]
public sealed partial class BodyPhysiologyComponent : Component, IPhysiologyHolder
{
    [DataField]
    public Dictionary<string, DiseaseData> Diseases { get; set; } = new();

    [DataField]
    public Dictionary<string, ToleranceData> Tolerances { get; set; } = new();
}
