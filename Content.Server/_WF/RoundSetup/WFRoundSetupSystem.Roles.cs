using System.Linq;
using Content.Server.GameTicking;
using Content.Shared._WF.RoundSetup;
using Content.Shared.GameTicking;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._WF.RoundSetup;

public sealed partial class WFRoundSetupSystem
{
    /// <summary>A lobby-local label and capacity backed by an ordinary eligible job.</summary>
    private sealed class LobbyRole
    {
        public int Id;
        public string Name = string.Empty;
        public string Job = string.Empty;
        public NetEntity? Station;
        public int Capacity = 1;
    }

    private static int RoleOccupancy(Lobby lobby, int roleId) =>
        lobby.Members.Values.Count(member => member.RoleId == roleId);

    /// <summary>Pregame Normal roles remain valid when their unspecified station becomes a live destination.</summary>
    private static bool RoleMatchesJob(Lobby lobby, LobbyRole role, WFRoundSetupJob job) =>
        role.Job == job.Id && (role.Station == job.Station || lobby.Scenario == WFRoundScenario.Normal && role.Station == null);

    /// <summary>Combines a role reservation with the recipient's current underlying job eligibility.</summary>
    private bool CanTakeRole(Lobby lobby, ICommonSession session, LobbyRole role)
    {
        return CanEnter(session) && IsRoleSelectable(lobby, session.UserId, role, GetJobs(session, lobby));
    }

    /// <summary>Reuses recipient eligibility when rendering or resolving several role openings.</summary>
    private static bool IsRoleSelectable(Lobby lobby, NetUserId user, LobbyRole role, IReadOnlyCollection<WFRoundSetupJob> jobs)
    {
        var selected = lobby.Members.TryGetValue(user, out var member) && member.RoleId == role.Id;
        return member?.Spawned != true &&
               WFRoundSetupPolicy.CanSelectRole(role.Capacity, RoleOccupancy(lobby, role.Id), selected) &&
               jobs.Any(job => RoleMatchesJob(lobby, role, job));
    }

    /// <summary>Resolves an explicit choice or the first eligible opening without accepting unknown role IDs.</summary>
    private bool TryGetRole(Lobby lobby, ICommonSession session, int? roleId, out LobbyRole role)
    {
        role = default!;
        if (!CanEnter(session))
            return false;
        var jobs = GetJobs(session, lobby);
        if (roleId is { } requested)
        {
            if (!lobby.Roles.TryGetValue(requested, out var found) || !IsRoleSelectable(lobby, session.UserId, found, jobs))
                return false;
            role = found;
            return true;
        }
        foreach (var candidate in lobby.Roles.Values)
        {
            if (!IsRoleSelectable(lobby, session.UserId, candidate, jobs))
                continue;
            role = candidate;
            return true;
        }
        return false;
    }

    /// <summary>Checks the existing reservation and job again before allocation or spawning.</summary>
    private bool CanSpawnRole(Lobby lobby, ICommonSession session)
    {
        if (!lobby.Members.TryGetValue(session.UserId, out var member) || member.Spawned || !CanEnter(session))
            return false;
        if (member.RoleId is not { } roleId)
            return GetJobs(session, lobby).Any(job => job.Id == member.Job &&
                (job.Station == member.Station || lobby.Scenario == WFRoundScenario.Normal && member.Station == null));
        return lobby.Roles.TryGetValue(roleId, out var role) && member.Job == role.Job && member.Station == role.Station &&
               CanTakeRole(lobby, session, role);
    }

    /// <summary>Updates a member's own reservation without changing anybody else's selected role.</summary>
    private bool SelectRole(Lobby lobby, ICommonSession session, int? roleId)
    {
        if (LockedRank(lobby, session) != null)
            return false;
        if (roleId == null && lobby.Host == session.UserId && lobby.Members.TryGetValue(session.UserId, out var host) && !host.Spawned)
        {
            host.RoleId = null;
            UnreadyMember(session.UserId, host);
            return true;
        }
        if (roleId == null || !lobby.Members.TryGetValue(session.UserId, out var member) || member.Spawned ||
            !TryGetRole(lobby, session, roleId, out var role))
            return false;
        if (lobby.Host == session.UserId && !CanLeadFaction(lobby, session.UserId, role.Job))
            return false;
        member.RoleId = role.Id;
        member.Job = role.Job;
        member.Station = role.Station;
        UnreadyMember(session.UserId, member);
        ReconcileFactionRoles(lobby);
        return true;
    }

    /// <summary>Validates personal host choices while keeping crew selections within advertised openings.</summary>
    private bool SelectRoleByJob(Lobby lobby, ICommonSession session, string? job, NetEntity? station)
    {
        if (!lobby.Members.TryGetValue(session.UserId, out var member) || member.Spawned || !CanEnter(session) || LockedRank(lobby, session) != null)
            return false;
        if (lobby.Host == session.UserId || member.RoleId == null)
        {
            if (!GetJobs(session, lobby).Any(choice => choice.Id == job && choice.Station == station) ||
                lobby.Host == session.UserId && !CanLeadFaction(lobby, session.UserId, job))
                return false;
            member.RoleId = null;
            member.Job = job;
            member.Station = station;
            UnreadyMember(session.UserId, member);
            ReconcileFactionRoles(lobby);
            return true;
        }
        foreach (var role in lobby.Roles.Values)
        {
            if (role.Job == job && role.Station == station && CanTakeRole(lobby, session, role))
                return SelectRole(lobby, session, role.Id);
        }
        return false;
    }

    /// <summary>Recruiters may advertise ship professions they cannot personally play.</summary>
    private List<WFRoundSetupJob> GetRoleJobs(ICommonSession host, Lobby lobby) => lobby.Scenario == WFRoundScenario.FactionShip
        ? WFFactionCrewPolicy.Jobs(lobby.Faction).Where(job => IsFactionJob(lobby.Faction, job) && FactionJobAvailable(job) &&
                WFFactionCrewPolicy.CanRecruit(lobby.Faction, lobby.Members[lobby.Host].Job, job))
            .Select(job => new WFRoundSetupJob(job, null, string.Empty)).ToList()
        : WFRoundSetupPolicy.UsesShipRoles(lobby.Scenario)
        ? WFShipCrewJobs.Jobs.Where(job => _prototypes.HasIndex<Content.Shared.Roles.JobPrototype>(job))
            .Select(job => new WFRoundSetupJob(job, null, string.Empty)).ToList()
        : GetJobs(host, lobby);

    /// <summary>Adds missing preset openings without changing occupied roles or existing recruitment choices.</summary>
    private bool ApplyRoster(Lobby lobby, WFCrewRoster roster)
    {
        if (!WFRoundSetupPolicy.UsesShipRoles(lobby.Scenario) || !Enum.IsDefined(roster))
            return false;
        var additions = WFShipCrewJobs.Roster(roster)
            .Select(role => (Name: Loc.GetString("wf-round-setup-roster-role-" + role.Name), role.Job, role.Capacity))
            .Where(role => !lobby.Roles.Values.Any(existing => existing.Name == role.Name && existing.Job == role.Job)).ToArray();
        if (lobby.Roles.Count + additions.Length > WFRoundSetupPolicy.MaximumRoles ||
            additions.Any(role => !_prototypes.HasIndex<Content.Shared.Roles.JobPrototype>(role.Job)))
            return false;
        foreach (var role in additions)
        {
            var id = ++lobby.NextRoleId;
            lobby.Roles.Add(id, new LobbyRole { Id = id, Name = role.Name, Job = role.Job, Capacity = role.Capacity });
        }
        return true;
    }

    /// <summary>Allows bounded host edits without changing occupied jobs or spawned identities.</summary>
    private bool UpsertRole(Lobby lobby, ICommonSession host, WFRoundSetupRequest msg)
    {
        if (msg.Job == null || msg.Capacity is < 1 or > WFRoundSetupPolicy.MaximumMembers ||
            !WFRoundSetupPolicy.TryNormalizeRoleName(msg.RoleName, out var name))
            return false;
        if (lobby.Scenario == WFRoundScenario.FactionShip &&
            (name.Length != 0 || !WFFactionCrewPolicy.CanRecruit(lobby.Faction, lobby.Members[lobby.Host].Job, msg.Job)))
            return false;
        if (msg.RoleId is not { } roleId)
        {
            if (lobby.Roles.Count >= WFRoundSetupPolicy.MaximumRoles ||
                !GetRoleJobs(host, lobby).Any(job => job.Id == msg.Job && job.Station == msg.Station))
                return false;
            var role = new LobbyRole
            {
                Id = ++lobby.NextRoleId, Name = name, Job = msg.Job, Station = msg.Station, Capacity = msg.Capacity,
            };
            lobby.Roles.Add(role.Id, role);
            return true;
        }
        if (!lobby.Roles.TryGetValue(roleId, out var existing))
            return false;
        var occupants = lobby.Members.Values.Where(member => member.RoleId == roleId).ToArray();
        if (msg.Capacity < occupants.Length)
            return false;
        var changedJob = existing.Job != msg.Job || existing.Station != msg.Station;
        if (changedJob && occupants.Length > 0 ||
            (changedJob || existing.Name != name) && occupants.Any(member => member.Spawned))
            return false;
        if (changedJob && !GetRoleJobs(host, lobby).Any(job => job.Id == msg.Job && job.Station == msg.Station))
            return false;
        existing.Name = name;
        existing.Job = msg.Job;
        existing.Station = msg.Station;
        existing.Capacity = msg.Capacity;
        if (changedJob)
        {
            foreach (var request in lobby.Requests.Where(request => request.Value == roleId).Select(request => request.Key).ToArray())
                lobby.Requests.Remove(request);
        }
        return true;
    }

    /// <summary>Removes only empty roles and invalidates requests for the retired opening.</summary>
    private static bool RemoveRole(Lobby lobby, int? roleId)
    {
        if (roleId is not { } selected || RoleOccupancy(lobby, selected) > 0 ||
            !lobby.Roles.Remove(selected))
            return false;
        foreach (var request in lobby.Requests.Where(request => request.Value == selected).Select(request => request.Key).ToArray())
            lobby.Requests.Remove(request);
        return true;
    }

    /// <summary>Publishes openings without exposing character choices or bypassing recipient restrictions.</summary>
    private static WFRoundSetupRole[] SnapshotRoles(Lobby lobby, NetUserId user, bool canEnter, bool live, IReadOnlyCollection<WFRoundSetupJob> jobs)
    {
        return lobby.Roles.Values.Select(role => new WFRoundSetupRole(role.Id, role.Name, role.Job, role.Station,
            role.Capacity, RoleOccupancy(lobby, role.Id),
            canEnter && IsRoleSelectable(lobby, user, role, jobs) || live && IsLiveRoleOpen(lobby, role))).ToArray();
    }

    /// <summary>Requires the player to confirm choices or purchase responsibility again.</summary>
    private void UnreadyMember(NetUserId user, Member member)
    {
        member.Ready = false;
        member.PurchaseVessel = null;
        member.PurchaseCharacterSlot = null;
        member.PurchaseRevision = null;
        if (_ticker.RunLevel == GameRunLevel.PreRoundLobby && _players.TryGetSessionById(user, out var session))
            _ticker.ToggleReady(session, false);
    }

    /// <summary>Changes to the selected ship invalidate each unspawned crew member's confirmation.</summary>
    private void UnreadyMembers(Lobby lobby)
    {
        lobby.PaymentRevision++;
        foreach (var (user, member) in lobby.Members)
        {
            if (!member.Spawned)
                UnreadyMember(user, member);
        }
    }

    /// <summary>Only explicit setup readiness grants consent to buy this vessel for this selected character.</summary>
    private bool HasShipPurchaseConsent(Lobby lobby, ICommonSession session)
    {
        return WFRoundSetupPolicy.IsShip(lobby.Scenario) && lobby.Vessel != null &&
               lobby.Members.TryGetValue(session.UserId, out var member) && IsReady(session.UserId, member) &&
               member.PurchaseVessel == lobby.Vessel && member.PurchaseRevision == lobby.PaymentRevision &&
               member.PurchaseCharacterSlot == _preferences.GetPreferences(session.UserId).SelectedCharacterIndex;
    }
    /// <summary>Ship hosts appear ready only after confirming the pending purchase in Round Setup.</summary>
    private bool IsReadyForLobby(Lobby lobby, NetUserId user, Member member)
    {
        if (!lobby.Started && WFRoundSetupPolicy.IsShip(lobby.Scenario) && (lobby.Host == user || member.Contribution > 0))
            return _players.TryGetSessionById(user, out var session) && HasShipPurchaseConsent(lobby, session);
        return IsReady(user, member);
    }
    /// <summary>A successor must consent before becoming responsible for an unpurchased ship.</summary>
    private void RequireHostConsent(Lobby lobby)
    {
        if (!lobby.Started && WFRoundSetupPolicy.IsShip(lobby.Scenario))
            ResetContributions(lobby);
    }
}
