using Content.Server._WF.Vehicles.Npc;

namespace Content.Server.NPC.Systems;

public sealed partial class NPCUtilitySystem
{
    /// <summary>A vehicle with someone alive inside is as alive a target as they are.</summary>
    private bool WfIsOccupiedVehicle(EntityUid target)
    {
        return HasComp<WFOccupiedVehicleComponent>(target);
    }
}
