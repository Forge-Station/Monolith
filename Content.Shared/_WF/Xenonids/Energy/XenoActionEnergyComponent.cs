using Robust.Shared.GameStates;

namespace Content.Shared._WF.Xenonids.Energy;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(XenoEnergySystem))]
public sealed partial class XenoActionEnergyComponent : Component
{
    [DataField(required: true), AutoNetworkedField]
    public int Cost;
}
