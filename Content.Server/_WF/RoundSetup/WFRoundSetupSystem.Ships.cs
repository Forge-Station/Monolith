using System.Linq;
using Content.Server.Shuttles.Components;
using Content.Shared._Mono.Company;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._WF.RoundSetup;
using Content.Shared._WF.ShipAccess;
using Content.Server.GameTicking;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Network;
using Robust.Shared.Enums;

namespace Content.Server._WF.RoundSetup;

public sealed partial class WFRoundSetupSystem
{
    private sealed record ShipChange(int Id, NetUserId Host, NetUserId Owner, EntityUid Ship);
    private int _nextShipChangeId;

    /// <summary>Uses an explicit replacement before either original scenario's ship.</summary>
    private IReadOnlyCollection<EntityUid> CrewGrids(Lobby lobby) => _ships.HasAssignedShip(lobby.Id) || WFRoundSetupPolicy.IsShip(lobby.Scenario) ||
        WFRoundSetupPolicy.IsFormed(lobby.Scenario)
        ? _ships.GetActiveGrids(lobby.Id)
        : lobby.Scenario == WFRoundScenario.Doomed ? _doomed.GetGrids(lobby.Id) : _crash.GetGrids(lobby.Id);

    private WFRoundSetupShip? CurrentShip(Lobby lobby) => CrewGrids(lobby)
        .Select(grid => _ships.SnapshotShip(grid)).FirstOrDefault(ship => ship != null);

    /// <summary>Only a spawned member's worn ID may offer a replacement, using the existing deed as authority.</summary>
    private bool TryAssignedShip(Lobby lobby, ICommonSession session, out EntityUid grid, out string error)
    {
        grid = default;
        error = "wf-round-setup-assign-ship-leader";
        if (!lobby.Started || _ticker.RunLevel != GameRunLevel.InRound || session.Status != SessionStatus.InGame ||
            !lobby.Members.TryGetValue(session.UserId, out var member) || !member.Spawned)
            return false;
        error = "wf-round-setup-assign-ship-deed";
        if (session.AttachedEntity is not { } body || !_shipCredentials.TryGetWornCard(body, out var card) ||
            !TryComp<ShuttleDeedComponent>(card, out var deed) || deed.ShuttleUid is not { } assigned)
            return false;
        error = "wf-round-setup-assign-ship-gone";
        if (TerminatingOrDeleted(assigned) || !HasComp<ShuttleComponent>(assigned) || !HasComp<MapGridComponent>(assigned))
            return false;
        grid = assigned;
        error = "wf-round-setup-assign-ship-current";
        if (CrewGrids(lobby).Contains(grid))
            return false;
        error = "wf-round-setup-assign-ship-other-crew";
        if (_lobbies.Values.Any(other => other.Id != lobby.Id && CrewGrids(other).Contains(assigned)) ||
            TryComp<WFShipAccessComponent>(grid, out var access) && access.CrewId is { } otherId && otherId != lobby.Id && _lobbies.ContainsKey(otherId))
            return false;
        error = "wf-round-setup-assign-ship-faction";
        if (lobby.Faction == WFCrewFaction.None ? _shipCredentials.IsFactionGrid(grid, out _) :
            !TryComp<CompanyComponent>(grid, out var company) || company.CompanyName.Id != WFFactionCrewPolicy.Company(lobby.Faction))
            return false;
        error = string.Empty;
        return true;
    }

    /// <summary>Lists crew-held deeds with reasons for unavailable ships, using character names only.</summary>
    private WFRoundSetupShipChoice[] CrewShipChoices(Lobby lobby)
    {
        var ships = new List<WFRoundSetupShipChoice>();
        foreach (var (user, member) in lobby.Members)
        {
            if (!_players.TryGetSessionById(user, out var owner))
                continue;
            var allowed = TryAssignedShip(lobby, owner, out var grid, out var reason);
            if (_ships.SnapshotShip(grid) is { } ship)
                ships.Add(new WFRoundSetupShipChoice(user, member.Name, ship, allowed ? null : Loc.GetString(reason)));
        }
        return ships.ToArray();
    }

    private bool ValidShipChange(Lobby lobby, ShipChange pending) => pending.Host == lobby.Host &&
        _players.TryGetSessionById(pending.Owner, out var owner) && TryAssignedShip(lobby, owner, out var grid, out _) && grid == pending.Ship;

    private WFRoundSetupShipChoice? PendingShipFor(Lobby lobby, NetUserId user) =>
        lobby.PendingShip is { } pending && (user == lobby.Host || user == pending.Owner) && ValidShipChange(lobby, pending) &&
        _ships.SnapshotShip(pending.Ship) is { } ship
            ? new WFRoundSetupShipChoice(pending.Owner, lobby.Members[pending.Owner].Name, ship, RequestId: pending.Id) : null;

    /// <summary>The leader selects a current member's deed; only its holder may approve someone else's request.</summary>
    private bool RequestCrewShip(Lobby lobby, ICommonSession session, NetUserId? ownerId, NetEntity? requested, out string error)
    {
        error = "wf-round-setup-assign-ship-leader";
        if (session.UserId != lobby.Host || !lobby.Started || !lobby.Members[session.UserId].Spawned)
            return false;
        error = "wf-round-setup-assign-ship-deed";
        if (ownerId == null || !_players.TryGetSessionById(ownerId.Value, out var owner) ||
            !TryAssignedShip(lobby, owner, out var grid, out error))
            return false;
        error = "wf-round-setup-assign-ship-changed";
        if (requested != GetNetEntity(grid))
            return false;
        if (owner.UserId == session.UserId)
            return CompleteShipChange(lobby, owner, grid, out error);
        lobby.PendingShip = new ShipChange(++_nextShipChangeId, session.UserId, owner.UserId, grid);
        _crewChat.DispatchServerMessage(owner, Loc.GetString("wf-round-setup-assign-ship-requested",
            ("leader", lobby.Members[lobby.Host].Name), ("ship", Name(grid))));
        SendState(owner);
        return true;
    }

    /// <summary>Approval is tied to both the requesting leader and the owner's current deed.</summary>
    private bool ApproveCrewShip(Lobby lobby, ICommonSession owner, NetEntity? requested, int requestId, out string error)
    {
        error = "wf-round-setup-assign-ship-changed";
        if (lobby.PendingShip is not { } pending || pending.Owner != owner.UserId || pending.Id != requestId ||
            !ValidShipChange(lobby, pending) || requested != GetNetEntity(pending.Ship))
            return false;
        return CompleteShipChange(lobby, owner, pending.Ship, out error);
    }

    /// <summary>Switches spawning, automatic access and announcements only after the owner has consented.</summary>
    private bool CompleteShipChange(Lobby lobby, ICommonSession owner, EntityUid grid, out string error)
    {
        var previous = CrewGrids(lobby).ToArray();
        error = "wf-round-setup-assign-ship-gone";
        if (!_ships.AssignShip(lobby.Id, grid))
            return false;
        foreach (var old in previous)
        {
            if (TerminatingOrDeleted(old) || !TryComp<WFShipAccessComponent>(old, out var oldAccess) || oldAccess.CrewId != lobby.Id)
                continue;
            SyncCrewShip((old, oldAccess), null);
            if (oldAccess.Mode == WFShipAccessMode.Crew)
                _shipAccess.SetMode((old, oldAccess), WFShipAccessMode.Private);
        }
        var ship = TryComp<WFShipAccessComponent>(grid, out var currentAccess)
            ? new Entity<WFShipAccessComponent>(grid, currentAccess) : _shipAccess.SetupShip(grid, owner.AttachedEntity);
        CaptureCrewCard(lobby.Members[owner.UserId], owner);
        SyncCrewShip(ship, lobby);
        lobby.PendingShip = null;
        _shipAccess.SetMode(ship, WFShipAccessMode.Crew);
        NotifyCrew(lobby, Loc.GetString("wf-round-setup-ship-changed", ("ship", Name(grid))));
        return true;
    }
}
