using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WF.RoundSetup;
using Robust.Shared.Player;

namespace Content.Server._WF.RoundSetup;

public sealed partial class WFRoundSetupSystem
{
    /// <summary>Company hangars are not on this fork.</summary>
    public bool CreateHangarCrew(ICommonSession session, int companyId, int licenceId, string vessel, string shipName, out string error)
    {
        _ = session;
        _ = companyId;
        _ = licenceId;
        _ = vessel;
        _ = shipName;
        error = "wf-round-setup-live-unavailable";
        return false;
    }

    /// <summary>A hangar crew's only design, shown free because the company pays.</summary>
    private WFRoundSetupVessel[] HangarVessels(Lobby lobby) =>
        lobby.Vessel != null && _prototypes.TryIndex<VesselPrototype>(lobby.Vessel, out var vessel)
            ? new[] { new WFRoundSetupVessel(vessel.ID, vessel.Name, vessel.Description, 0) }
            : Array.Empty<WFRoundSetupVessel>();

    private bool ChargeHangar(Lobby lobby, ICommonSession host, out string error)
    {
        _ = lobby;
        _ = host;
        error = "wf-round-setup-live-unavailable";
        return false;
    }

    /// <summary>Undoes a half-finished ship launch.</summary>
    private void RollbackShip(Lobby lobby)
    {
        _ships.Rollback(lobby.Id);
    }

    private static void MarkHangarShip(Lobby lobby)
    {
        _ = lobby;
    }
}
