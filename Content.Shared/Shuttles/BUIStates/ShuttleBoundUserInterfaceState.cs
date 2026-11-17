using Content.Shared.Shuttles.UI.MapObjects;
using Content.Shared._Forge.Bss;
using Robust.Shared.Serialization;

namespace Content.Shared.Shuttles.BUIStates;

[Serializable, NetSerializable]
public sealed class ShuttleBoundUserInterfaceState : BoundUserInterfaceState
{
    public NavInterfaceState NavState;
    public ShuttleMapInterfaceState MapState;
    public DockingInterfaceState DockState;
    public BssSystemMapState SystemMapState;

    public ShuttleBoundUserInterfaceState(
        NavInterfaceState navState,
        ShuttleMapInterfaceState mapState,
        DockingInterfaceState dockState,
        BssSystemMapState? systemMapState = null)
    {
        NavState = navState;
        MapState = mapState;
        DockState = dockState;
        SystemMapState = systemMapState ?? BssSystemMapState.Empty();
    }
}
