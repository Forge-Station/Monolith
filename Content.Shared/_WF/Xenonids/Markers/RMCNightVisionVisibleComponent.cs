using Robust.Shared.GameStates;

namespace Content.Shared._WF.Xenonids.Markers;

/// <summary>Seen through RMC night vision; a marker that keeps RMC's fields until night vision visibility is ported.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class RMCNightVisionVisibleComponent : Component
{
    [DataField]
    public int Priority;

    [DataField]
    public float? Transparency;
}
