using Content.Server._WF.Companies;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WF.RoundSetup;
using Robust.Shared.Player;

namespace Content.Server._WF.RoundSetup;

public sealed partial class WFRoundSetupSystem
{
    /// <summary>Starts a ship crew for a company hangar licence: the design is fixed and the company pays instead of the crew.</summary>
    public bool CreateHangarCrew(ICommonSession session, int companyId, int licenceId, string vessel, string shipName, out string error)
    {
        error = "wf-company-error-in-crew";
        if (FindLobby(session.UserId) != null)
            return false;
        error = "wf-company-error-join-closed";
        if (!CanEnter(session) || _lobbies.Count >= 64)
            return false;
        var name = _prototypes.TryIndex<VesselPrototype>(vessel, out var prototype) ? prototype.Name : string.Empty;
        var lobby = new Lobby
        {
            Id = ++_nextId, Host = session.UserId, Scenario = WFRoundScenario.Ship, Vessel = vessel, CompanyId = companyId,
            // Company members walk on; anyone else asks the host.
            HangarLicence = licenceId, Code = NewCode(), Access = WFLobbyAccess.CompanyFirst, ShipName = shipName,
            Title = WFRoundSetupPolicy.TryNormalizeTitle(shipName.Length > 0 ? shipName : name, out var title) ? title : string.Empty,
        };
        _lobbies.Add(lobby.Id, lobby);
        RegisterMember(lobby, session, new Member { Name = session.Name });
        SyncCompanyLocks();
        SendState(session);
        Broadcast();
        return true;
    }

    /// <summary>A hangar crew's only design, shown free because the company pays.</summary>
    private WFRoundSetupVessel[] HangarVessels(Lobby lobby) =>
        lobby.Vessel != null && _prototypes.TryIndex<VesselPrototype>(lobby.Vessel, out var vessel)
            ? new[] { new WFRoundSetupVessel(vessel.ID, vessel.Name, vessel.Description, 0) }
            : Array.Empty<WFRoundSetupVessel>();

    /// <summary>Charges the company for a hangar launch and lets the ship scenario hand out the deed.</summary>
    private bool ChargeHangar(Lobby lobby, ICommonSession host, out string error)
    {
        if (!_companies.ChargeLaunch(lobby.HangarLicence!.Value, host, out error))
            return false;
        _ships.MarkCompanyPaid(lobby.Id, host);
        return true;
    }

    /// <summary>Undoes a half-finished ship launch, refunding a hangar launch's fees.</summary>
    private void RollbackShip(Lobby lobby)
    {
        _ships.Rollback(lobby.Id);
        if (lobby.HangarLicence is { } licence)
            _companies.RefundLaunch(licence);
    }

    /// <summary>Marks a launched hangar ship as company property.</summary>
    private void MarkHangarShip(Lobby lobby)
    {
        if (lobby.CompanyId is not { } company)
            return;
        foreach (var grid in _ships.GetActiveGrids(lobby.Id))
        {
            EnsureComp<WFCompanyHangarShipComponent>(grid).CompanyId = company;
            _companies.MarkCompanyProperty(grid, company);
        }
    }
}
