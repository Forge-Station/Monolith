using System.Linq;
using Content.Server.Administration.Logs;
using Content.Server.Station.Systems;
using Content.Shared._WF.RoundSetup;
using Content.Shared.Chat;
using Content.Shared.Database;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Server._WF.RoundSetup;

public sealed partial class WFRoundSetupSystem
{
    [Dependency] private IAdminLogManager _postingLog = default!;
    [Dependency] private INetConfigurationManager _postingConfig = default!;
    [Dependency] private StationSystem _crewStations = default!;

    private static readonly Color PostingColor = Color.FromHex("#7FD3C4");

    /// <summary>Each host's next allowed posting, so a new crew doesn't reset the cooldown.</summary>
    private readonly Dictionary<NetUserId, TimeSpan> _postingCooldowns = new();

    /// <summary>Free slots; members besides the host who joined outside any opening, such as by company rank, fill one too.</summary>
    private static int Openings(Lobby lobby)
    {
        var free = lobby.Roles.Values.Sum(role => Math.Max(0, role.Capacity - RoleOccupancy(lobby, role.Id)));
        var unassigned = lobby.Members.Count(pair => pair.Key != lobby.Host && (pair.Value.RoleId is not { } id || !lobby.Roles.ContainsKey(id)));
        return Math.Max(0, free - unassigned);
    }

    private static bool ShowsRecruiting(Lobby lobby) => WFRoundSetupPolicy.ShowsRecruiting(lobby.Recruiting, lobby.Access,
        lobby.Started, lobby.AllowLateJoin, lobby.Members.Count, Openings(lobby));

    /// <summary>Broadcasts the host's open invitation to every player who could take one of its openings.</summary>
    private bool PostCrew(Lobby lobby, ICommonSession host, string? input, out string error)
    {
        error = "wf-round-setup-posting-invalid";
        if (!WFRoundSetupPolicy.TryNormalizePosting(input, out var posting))
            return false;
        error = "wf-round-setup-posting-too-soon";
        if (_postingCooldowns.TryGetValue(host.UserId, out var hostNext) && hostNext > _timing.CurTime)
            lobby.NextPosting = hostNext;
        if (lobby.NextPosting is { } next && next > _timing.CurTime)
            return false;
        lobby.Recruiting = true;
        error = "wf-round-setup-posting-closed";
        if (!ShowsRecruiting(lobby))
            return false;
        lobby.NextPosting = _postingCooldowns[host.UserId] = _timing.CurTime + WFRoundSetupPolicy.PostingCooldown;

        var crew = TaggedCrewName(lobby);
        var hostName = lobby.Members[lobby.Host].Name;
        var openings = Openings(lobby);
        _postingLog.Add(LogType.Chat, LogImpact.Low, $"{host:player} posted for crew {crew} ({lobby.Id}): {posting}");
        var recipients = _players.Sessions.Where(session => session.Status == SessionStatus.InGame && FindLobby(session.UserId) == null &&
            CanAdmit(lobby, session) && _postingConfig.GetClientCVar(session.Channel, WFRoundSetupCVars.ShowPostings)).ToList();
        RaiseNetworkEvent(new WFCrewPostingEvent(lobby.Id, crew, hostName, lobby.Scenario, lobby.Access, openings, posting),
            Filter.Empty().AddPlayers(recipients));
        // The host sees the same line, so they know how it read.
        recipients.Add(host);
        var wrapped = Loc.GetString("wf-round-setup-posting-chat", ("crew", FormattedMessage.EscapeText(crew)),
            ("host", FormattedMessage.EscapeText(hostName)), ("openings", openings), ("message", FormattedMessage.EscapeText(posting)));
        _crewChat.ChatMessageToMany(ChatChannel.Radio, posting, wrapped, default, false, true,
            recipients.Select(session => session.Channel), PostingColor);
        return true;
    }

    /// <summary>Stops a crew recruiting and withdraws its cards from every client.</summary>
    public bool RevokePosting(int lobbyId)
    {
        if (!_lobbies.TryGetValue(lobbyId, out var lobby))
            return false;
        lobby.Recruiting = false;
        RaiseNetworkEvent(new WFCrewPostingRevokedEvent(lobbyId));
        Broadcast();
        return true;
    }
}
