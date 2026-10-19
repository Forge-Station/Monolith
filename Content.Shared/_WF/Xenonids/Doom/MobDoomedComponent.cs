using Robust.Shared.GameStates;

namespace Content.Shared._WF.Xenonids.Doom;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class MobDoomedComponent : Component
{
    [DataField, AutoNetworkedField]
    public TimeSpan? EndsAt;
}
