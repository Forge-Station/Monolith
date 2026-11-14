using System.Linq;
using Content.Server.GameTicking;
using Content.Shared._WF.RoundSetup;
using Content.Shared.GameTicking;
using Content.Shared.Ghost;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Preferences;
using Content.Shared.Roles.Jobs;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._WF.RoundSetup;

public sealed partial class WFRoundSetupSystem
{
    [Dependency] private SharedMindSystem _liveMinds = default!;
    [Dependency] private SharedJobSystem _liveJobs = default!;

    /// <summary>A player controlling a living character in the round, who can form or join a crew without respawning.</summary>
    private bool CanJoinLive(ICommonSession session) =>
        session.Status == SessionStatus.InGame && _ticker.RunLevel == GameRunLevel.InRound && _userDb.IsLoadComplete(session) &&
        _ticker.PlayerGameStatuses.GetValueOrDefault(session.UserId) == PlayerGameStatus.JoinedGame &&
        session.AttachedEntity is { } body && !TerminatingOrDeleted(body) && !HasComp<GhostComponent>(body) &&
        TryComp<MobStateComponent>(body, out var state) && state.CurrentState != MobState.Dead;

    /// <summary>Whether a player may enter this crew, by a fresh spawn or with the character they are playing.</summary>
    private bool CanAdmit(Lobby lobby, ICommonSession session)
    {
        if (CanEnter(session))
            return !WFRoundSetupPolicy.IsFormed(lobby.Scenario) || _ships.HasAssignedShip(lobby.Id);
        return CanJoinLive(session) && WFRoundSetupPolicy.AllowsLiveMembers(lobby.Scenario, lobby.Started);
    }

    /// <summary>A live member's role is only a label, so any opening with a free slot will do.</summary>
    private static bool TryGetLiveRole(Lobby lobby, int? roleId, out LobbyRole role)
    {
        role = default!;
        foreach (var candidate in lobby.Roles.Values)
        {
            if (roleId != null && candidate.Id != roleId ||
                !WFRoundSetupPolicy.CanSelectRole(candidate.Capacity, RoleOccupancy(lobby, candidate.Id), false))
                continue;
            role = candidate;
            return true;
        }
        return false;
    }

    private static bool IsLiveRoleOpen(Lobby lobby, LobbyRole role) =>
        WFRoundSetupPolicy.CanSelectRole(role.Capacity, RoleOccupancy(lobby, role.Id), false);

    /// <summary>Starts a Formed crew around the host's current character; it has no launch or purchase step.</summary>
    private bool CreateFormed(ICommonSession session, string title, string description)
    {
        var lobby = new Lobby { Id = ++_nextId, Host = session.UserId, Scenario = WFRoundScenario.Formed, Started = true,
            AllowLateJoin = true, Code = NewCode(), Title = title, Description = description };
        _lobbies.Add(lobby.Id, lobby);
        RegisterLiveMember(lobby, session, null);
        return true;
    }

    /// <summary>Joins with the character already in the round; ID, job and equipment stay as they are.</summary>
    private bool AddLiveMember(Lobby lobby, ICommonSession session, int? roleId)
    {
        if (FindLobby(session.UserId) != null || lobby.Members.Count >= WFRoundSetupPolicy.MaximumMembers ||
            !CanJoinLive(session) || !WFRoundSetupPolicy.AllowsLiveMembers(lobby.Scenario, lobby.Started) ||
            !TryGetLiveRole(lobby, roleId, out var role))
            return false;
        RegisterLiveMember(lobby, session, role);
        NotifyCrewJoined(lobby, session.UserId);
        return true;
    }

    private void RegisterLiveMember(Lobby lobby, ICommonSession session, LobbyRole? role)
    {
        var body = session.AttachedEntity!.Value;
        var mind = _liveMinds.GetMind(body);
        var name = mind != null && Comp<MindComponent>(mind.Value).CharacterName is { Length: > 0 } character ? character : Name(body);
        var member = new Member { Name = name, Live = true, Spawned = true, RoleId = role?.Id,
            Job = _liveJobs.MindTryGetJobId(mind, out var job) ? job?.Id : null };
        // The lobby portrait only matches when the character still selected is the one being played.
        if (_preferences.GetPreferencesOrNull(session.UserId)?.SelectedCharacter is HumanoidCharacterProfile profile && profile.Name == name)
            member.Portrait = new WFCrewPortrait(profile.Name, profile.Species.Id, profile.Sex, profile.Appearance.Clone());
        RegisterMember(lobby, session, member);
        CaptureCrewCard(member, session);
        SyncCrewShips();
    }

    /// <summary>Stations whose recruiting crew owns a ship there, for the ordinary late-join picker.</summary>
    private NetEntity[] RecruitingStations()
    {
        var stations = new HashSet<NetEntity>();
        foreach (var lobby in _lobbies.Values.Where(lobby => lobby.Started && ShowsRecruiting(lobby)))
        foreach (var grid in CrewGrids(lobby))
        {
            if (!TerminatingOrDeleted(grid) && _crewStations.GetOwningStation(grid) is { } station)
                stations.Add(GetNetEntity(station));
        }
        return stations.ToArray();
    }
}
