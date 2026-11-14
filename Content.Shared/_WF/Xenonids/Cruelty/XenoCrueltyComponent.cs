using Robust.Shared.GameStates;

namespace Content.Shared._WF.Xenonids.Cruelty;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class XenoCrueltyComponent : Component
{
    [DataField, AutoNetworkedField]
    public TimeSpan CooldownReduction = TimeSpan.FromSeconds(3);
}
