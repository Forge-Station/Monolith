using Robust.Shared.Physics.Components;

namespace Content.Server.Shuttles.Systems;

public sealed partial class ShuttleSystem
{
    /// <summary>
    /// Total mass of a shuttle and every grid that FTLs with it, so a light drive pod can't jump a heavy ship on its own mass.
    /// </summary>
    public float GetFtlGroupMass(EntityUid shuttleUid)
    {
        var group = new HashSet<EntityUid>();
        GetAllDockedShuttles(shuttleUid, group);

        var mass = 0f;
        foreach (var grid in group)
        {
            if (TryComp<PhysicsComponent>(grid, out var body))
                mass += body.Mass;
        }

        return mass;
    }
}
