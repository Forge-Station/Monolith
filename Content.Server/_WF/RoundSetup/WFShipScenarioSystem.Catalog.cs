using System.Linq;
using Content.Shared._NF.Shipyard;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared.Access;
using Content.Shared.Roles;

namespace Content.Server._WF.RoundSetup;

public sealed partial class WFShipScenarioSystem
{
    /// <summary>Uses CC's civilian, expedition, medical, scrap and C-Sec catalogs with the selected job's ID access.</summary>
    private bool IsEligible(VesselPrototype vessel, string? job)
    {
        if (vessel.Abstract || !vessel.Purchasable || vessel.Price <= 0 ||
            vessel.Group is not (ShipyardConsoleUiKey.Shipyard or ShipyardConsoleUiKey.Expedition or
                ShipyardConsoleUiKey.Medical or ShipyardConsoleUiKey.Scrap or ShipyardConsoleUiKey.Sr))
            return false;
        if (string.IsNullOrEmpty(vessel.Access))
            return true;
        return job != null && _prototypes.TryIndex<JobPrototype>(job, out var role) &&
            (role.Access.Any(tag => tag.Id == vessel.Access) || role.AccessGroups.Any(group =>
                _prototypes.Index(group).Tags.Any(tag => tag.Id == vessel.Access)));
    }
}
