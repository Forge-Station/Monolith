using Content.Server._WF.RoundSetup;

namespace Content.Server.Shuttles.Systems;

public sealed partial class ShuttleConsoleSystem
{
    /// <summary>Scenario hosts and reserved crew roles control admission even when the helm loses power.</summary>
    private bool WFHasCrewAdmission(EntityUid station) =>
        HasComp<WFCrashScenarioStationComponent>(station) || HasComp<WFShipScenarioStationComponent>(station) ||
        HasComp<WFDoomedScenarioStationComponent>(station);
}
