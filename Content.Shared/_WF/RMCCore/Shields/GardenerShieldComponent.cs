using Robust.Shared.GameStates;

namespace Content.Shared._WF.RMCCore.Shields;

// The shield itself is handled by the base system and component
// This component exists solely to display the pop-up when the gardener's shield ends
[RegisterComponent, NetworkedComponent]
public sealed partial class GardenerShieldComponent : Component { }
