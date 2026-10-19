using Robust.Shared.GameStates;

namespace Content.Shared._WF.Xenonids.Charge;

[RegisterComponent, NetworkedComponent]
[Access(typeof(XenoToggleChargingSystem))]
public sealed partial class XenoToggleChargingStopComponent : Component;
