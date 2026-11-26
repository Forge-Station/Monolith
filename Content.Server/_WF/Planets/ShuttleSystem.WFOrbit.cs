using Content.Server._WF.Planets.Hop;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WF.Planets;

namespace Content.Server.Shuttles.Systems;

public sealed partial class ShuttleSystem
{
    /// <summary>True when FTL departure is refused: below orbit on a planet, mid-transit or mid-hop.</summary>
    // Checked from TrySetupFTL and FTLToDock, since the console's beacon and free-FTL paths skip CanFTLTo.
    public bool WfRefusesFtlDeparture(EntityUid shuttleUid)
    {
        if (HasComp<WFPlanetHopComponent>(shuttleUid))
            return true;

        if (Transform(shuttleUid).MapUid is not { } mapUid)
            return false;

        if (HasComp<CEZTransitMapComponent>(mapUid))
            return true;

        return HasComp<WFPlanetLayerComponent>(mapUid) && !HasComp<WFOrbitLayerComponent>(mapUid);
    }
}
