using Robust.Shared.GameStates;

namespace Content.Shared._WF.RMCCore.Storage;

// WOLFGATE: RMC stuns marines leaving storage; Wolfgate has no such stun, so this stays a marker for ported prototypes.
[RegisterComponent, NetworkedComponent]
public sealed partial class NoStunOnExitComponent : Component;
