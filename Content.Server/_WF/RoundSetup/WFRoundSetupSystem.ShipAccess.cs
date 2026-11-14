using System.Linq;
using Content.Server._WF.ShipAccess;
using Content.Shared._WF.ShipAccess;
using Robust.Shared.Player;

namespace Content.Server._WF.RoundSetup;

public sealed partial class WFRoundSetupSystem
{
    [Dependency] private WFShipAccessServerSystem _shipAccess = default!;
    [Dependency] private WFShipAccessSystem _shipCredentials = default!;

    private void InitializeShipAccess()
    {
        SubscribeLocalEvent<WFShipAccessComponent, WFShipCrewLinkEvent>(OnLinkCrew);
    }

    /// <summary>Only a spawned member can link a ship to their crew.</summary>
    private void OnLinkCrew(Entity<WFShipAccessComponent> ship, ref WFShipCrewLinkEvent args)
    {
        if (!TryComp<ActorComponent>(args.Actor, out var actor) || FindLobby(actor.PlayerSession.UserId) is not { } lobby ||
            !lobby.Members[actor.PlayerSession.UserId].Spawned)
            return;
        SyncCrewShip(ship, lobby);
    }

    /// <summary>Retains the issued card through disconnects, without trusting cards picked up later.</summary>
    private void CaptureCrewCard(Member member, ICommonSession session)
    {
        member.Body = session.AttachedEntity;
        if (session.AttachedEntity is { } body && _shipCredentials.TryGetWornCard(body, out var card) &&
            _shipCredentials.TryGetKey(card, out var key))
            member.AccessKey = key;
    }

    /// <summary>Immediately revokes departed members and periodically updates crew names.</summary>
    private void SyncCrewShips()
    {
        var ships = EntityQueryEnumerator<WFShipAccessComponent>();
        while (ships.MoveNext(out var uid, out var access))
        {
            if (access.CrewId is not { } id || TerminatingOrDeleted(uid))
                continue;
            _lobbies.TryGetValue(id, out var lobby);
            SyncCrewShip((uid, access), lobby);
        }
    }

    private void SyncCrewShip(Entity<WFShipAccessComponent> ship, Lobby? lobby)
    {
        var keys = lobby?.Members.Values.Where(member => member.Spawned && member.AccessKey != null)
            .Select(member => member.AccessKey!.Value) ?? Enumerable.Empty<WFShipAccessKey>();
        var name = lobby == null ? null : string.IsNullOrWhiteSpace(lobby.Title) ? lobby.Members[lobby.Host].Name : lobby.Title;
        _shipAccess.SetCrew(ship, lobby?.Id, name, keys);
    }
}
