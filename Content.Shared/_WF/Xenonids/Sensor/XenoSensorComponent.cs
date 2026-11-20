using Robust.Shared.GameStates;

namespace Content.Shared._WF.Xenonids.Sensor;

/// <summary>A hive structure whose mass scanner only xenos of its own hive can open.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class XenoSensorComponent : Component;
