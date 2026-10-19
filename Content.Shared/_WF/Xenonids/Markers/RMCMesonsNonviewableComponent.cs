using Robust.Shared.GameStates;

namespace Content.Shared._WF.Xenonids.Markers;

/// <summary>Hidden from RMC mesons; a marker that keeps RMC's field until mesons are ported.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class RMCMesonsNonviewableComponent : Component
{
    [DataField]
    public bool XenoVisible;
}
