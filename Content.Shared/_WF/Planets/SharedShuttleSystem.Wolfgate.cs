using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WF.Planets;

namespace Content.Shared.Shuttles.Systems;

public abstract partial class SharedShuttleSystem
{
    /// <summary>
    /// Blocks FTL into an orbit layer (the console's orbit button handles that) and out of any planet layer but orbit.
    /// </summary>
    protected bool WfAllowFTL(EntityUid shuttleUid, EntityUid targetMapUid)
    {
        // Inbound: the orbit layer is reached from the console's orbit button, which runs its own range check.
        if (HasComp<WFOrbitLayerComponent>(targetMapUid))
            return false;

        if (_xformQuery.GetComponent(shuttleUid).MapUid is not { } shuttleMapUid)
            return true;

        // Outbound: never from mid-transit, and never off a surface, air or cloud layer.
        if (HasComp<CEZTransitMapComponent>(shuttleMapUid))
            return false;

        return !HasComp<WFPlanetLayerComponent>(shuttleMapUid) || HasComp<WFOrbitLayerComponent>(shuttleMapUid);
    }
}
