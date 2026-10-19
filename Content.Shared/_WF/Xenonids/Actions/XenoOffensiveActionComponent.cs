using Robust.Shared.GameStates;

namespace Content.Shared._WF.Xenonids.Actions;

[RegisterComponent, NetworkedComponent]
[Access(typeof(XenoActionsSystem))]
public sealed partial class XenoOffensiveActionComponent : Component;
