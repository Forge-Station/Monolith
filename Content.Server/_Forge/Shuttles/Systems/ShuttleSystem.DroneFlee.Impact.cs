using Content.Server._Forge.Drones.Components;
using Content.Server.Shuttles.Components;

namespace Content.Server.Shuttles.Systems;

public sealed partial class ShuttleSystem
{
    /// <summary>
    /// Forge-change: fleeing procedural drone grids skip shuttle impact processing.
    /// </summary>
    private bool ForgeIsDroneFleeGrid(EntityUid uid) =>
        EntityManager.HasComponent<DroneInnerZoneFleeGridComponent>(uid);
}
