using Robust.Shared.GameStates;

namespace Content.Shared._WF.Xenonids.Charge;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(XenoToggleChargingSystem))]
public sealed partial class XenoToggleChargingParalyzeComponent : Component
{
    [DataField, AutoNetworkedField]
    public TimeSpan Duration = TimeSpan.FromSeconds(2);
    [DataField, AutoNetworkedField]
    public TimeSpan MaxStageDuration = TimeSpan.FromSeconds(4);
}
