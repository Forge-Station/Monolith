using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._Forge.Genetics.Mutations;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentPause]
public sealed partial class GeneticCoughComponent : Component
{
    [DataField]
    public float MinInterval = 12f;

    [DataField]
    public float MaxInterval = 35f;

    [DataField]
    public string Emote = "Cough";

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextIncident;
}
