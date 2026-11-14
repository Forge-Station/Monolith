using Content.Server._WF.Encounters.Systems;
using Content.Server.Shuttles.Components;

namespace Content.Server.Shuttles.Systems;

public sealed partial class DockingSystem
{
    /// <summary>Whether a raider's docking clamps refuse an undock asked for at a console.</summary>
    private bool WfClampHolds(EntityUid console, Entity<DockingComponent> dock)
    {
        return EntityManager.System<WFDockingClampSystem>().Refuses(console, dock);
    }
}
