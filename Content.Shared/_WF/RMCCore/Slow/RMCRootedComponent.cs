using Robust.Shared.GameStates;

namespace Content.Shared._WF.RMCCore.Slow;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class RMCRootedComponent : Component
{
    [DataField, AutoNetworkedField]
    public TimeSpan ExpiresAt;
}
