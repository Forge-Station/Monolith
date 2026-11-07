using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._Forge.Genetics.Mutations;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentPause]
public sealed partial class GeneticRegenComponent : Component
{
    [DataField]
    public float HealPerTick = 1f;

    [DataField]
    public TimeSpan Interval = TimeSpan.FromSeconds(3);

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextHeal;
}
