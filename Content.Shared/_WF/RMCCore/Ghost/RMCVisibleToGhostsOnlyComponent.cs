using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.RMCCore.Ghost;

[RegisterComponent, NetworkedComponent]
public sealed partial class RMCVisibleToGhostsOnlyComponent : Component;

[Serializable, NetSerializable]
public enum RMCGhostVisibleOnlyVisualLayers
{
    Base,
}
