using Content.Shared.FixedPoint;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._Forge.Genetics.Mutations;

/// <summary>
/// Pressure-adapted skin, halved blood volume, and a biological air sac on the back.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentPause]
public sealed partial class GeneticOrganAdaptComponent : Component
{
    [DataField]
    public EntProtoId AirSacPrototype = "ClothingBackGeneticAirSac";

    [DataField]
    public float BloodVolumeFactor = 0.5f;

    [DataField]
    public FixedPoint2? OriginalBloodMaxVolume;

    [DataField]
    public EntityUid? AirSacEntity;

    [DataField]
    public bool AddedPressureImmunity;

    [DataField]
    public bool ConnectedInternals;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextInternalsAttempt;
}
