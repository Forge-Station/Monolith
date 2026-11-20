using Robust.Shared.GameStates;

namespace Content.Shared._WF.Xenonids.Construction.ResinWhisper;

[RegisterComponent, NetworkedComponent]
[Access(typeof(ResinWhispererSystem))]
public sealed partial class ResinDoorComponent : Component;
