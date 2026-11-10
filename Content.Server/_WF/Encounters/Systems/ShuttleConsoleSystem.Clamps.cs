using Content.Server._WF.Encounters.Systems;
using Content.Server.Shuttles.Components;

namespace Content.Server.Shuttles.Systems;

public sealed partial class ShuttleConsoleSystem
{
    /// <summary>The raider grid whose docking clamps hold a port's joint, for the docking screen.</summary>
    private NetEntity? WfClampedBy(Entity<DockingComponent> dock)
    {
        return EntityManager.System<WFDockingClampSystem>().Holder(dock) is { } raider ? GetNetEntity(raider) : null;
    }
}
