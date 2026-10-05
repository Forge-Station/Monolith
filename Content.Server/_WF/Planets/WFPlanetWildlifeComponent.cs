using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server._WF.Planets;

/// <summary>Ambient animals that may retire only while wild, untouched and far from every observer.</summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class WFPlanetWildlifeComponent : Component
{
    [DataField] public EntityUid Ground;
    [DataField] public bool Protected;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan LastNearby;
}
