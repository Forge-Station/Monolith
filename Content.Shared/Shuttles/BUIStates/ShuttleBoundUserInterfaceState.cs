using Content.Shared.Shuttles.UI.MapObjects;
using Robust.Shared.Serialization;

namespace Content.Shared.Shuttles.BUIStates;

[Serializable, NetSerializable]
public sealed class ShuttleBoundUserInterfaceState : BoundUserInterfaceState
{
    public NavInterfaceState NavState;
    public ShuttleMapInterfaceState MapState;
    public DockingInterfaceState DockState;
    // WOLFGATE: Expose active tractor sources to the captured ship's helm.
    /// <summary>Names of the distinct ships currently applying an active tractor beam to this shuttle.</summary>
    public string[] TractorSources;

    public ShuttleBoundUserInterfaceState(NavInterfaceState navState, ShuttleMapInterfaceState mapState, DockingInterfaceState dockState,
        string[]? tractorSources = null) // WOLFGATE
    {
        NavState = navState;
        MapState = mapState;
        DockState = dockState;
        TractorSources = tractorSources ?? Array.Empty<string>(); // WOLFGATE
    }
}
