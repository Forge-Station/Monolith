using Robust.Shared.GameStates;

namespace Content.Shared._WF.Xenonids.Parasite.Surgery;

/// <summary>Lists a surgery only while the patient carries a larva that is not yet bursting.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class XenoLarvaSurgeryConditionComponent : Component;

/// <summary>Step effect: cuts the larva's roots. Done once the infection reads as rootless.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class XenoLarvaCutRootsStepComponent : Component;

/// <summary>Step effect: pulls the larva out through <c>SharedXenoParasiteSystem.TryRemoveLarva</c>.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class XenoLarvaRemoveStepComponent : Component;
