using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._Forge.Genetics.Mutations;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentPause]
public sealed partial class GeneticEpilepsyComponent : Component
{
    [DataField]
    public float MinInterval = 40f;

    [DataField]
    public float MaxInterval = 120f;

    [DataField]
    public TimeSpan SeizureDuration = TimeSpan.FromSeconds(4);

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextIncident;
}
