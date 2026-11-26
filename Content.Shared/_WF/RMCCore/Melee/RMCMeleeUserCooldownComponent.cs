using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._WF.RMCCore.Melee;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(fieldDeltas: true), AutoGenerateComponentPause]
public sealed partial class RMCMeleeUserCooldownComponent : Component
{
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField]
    [AutoPausedField]
    public TimeSpan NextAttack;
}
