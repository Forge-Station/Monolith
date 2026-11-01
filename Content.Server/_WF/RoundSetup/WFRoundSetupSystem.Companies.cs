using System.Linq;
using Content.Server._WF.Companies;
using Content.Shared._WF.Companies;
using Content.Shared._WF.RoundSetup;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._WF.RoundSetup;

public sealed partial class WFRoundSetupSystem
{
    [Dependency] private WFCompanySystem _companies = default!;

    /// <summary>Grids currently marked with a crew's company, so a change of ship or company unmarks the old ones.</summary>
    private readonly HashSet<EntityUid> _companyShips = new();

    /// <summary>Flies the crew under the host's company, or under none.</summary>
    private bool SetCrewCompany(Lobby lobby, ICommonSession host, int? companyId)
    {
        if (companyId is { } id && !_companies.CanFieldCrew(host, id))
            return false;
        lobby.CompanyId = companyId;
        if (companyId == null && WFRoundSetupPolicy.IsCompanyAccess(lobby.Access))
            lobby.Access = WFLobbyAccess.Requests;
        SyncCompanyShips();
        return true;
    }

    /// <summary>The crew's company identity, dropping a company that no longer exists.</summary>
    private (string Name, string Tag, Color Colour)? CrewCompany(Lobby lobby)
    {
        if (lobby.CompanyId is not { } id)
            return null;
        if (_companies.CompanyIdentity(id) is { } identity)
            return identity;
        lobby.CompanyId = null;
        if (WFRoundSetupPolicy.IsCompanyAccess(lobby.Access))
            lobby.Access = WFLobbyAccess.Requests;
        return null;
    }

    /// <summary>The crew name with its company tag, for postings.</summary>
    private string TaggedCrewName(Lobby lobby) =>
        CrewCompany(lobby) is { } company ? $"[{company.Tag}] {CrewName(lobby)}" : CrewName(lobby);

    /// <summary>Companies the session's character may run this crew under.</summary>
    private WFRoundSetupCompany[] FieldCompanies(ICommonSession session)
    {
        if (_companies.CompanyIdOf(session) is not { } id || !_companies.CanFieldCrew(session, id) ||
            _companies.CompanyIdentity(id) is not { } identity)
            return Array.Empty<WFRoundSetupCompany>();
        return new[] { new WFRoundSetupCompany(id, identity.Name, identity.Tag) };
    }

    /// <summary>Whether the session's character belongs to the company this crew flies under.</summary>
    private bool InCrewCompany(Lobby lobby, ICommonSession session) =>
        lobby.CompanyId != null && _companies.CompanyIdOf(session) == lobby.CompanyId;

    /// <summary>The rank a member must play in this crew: a crew under a company fields its members as their ranks. Faction crews keep faction ranks.</summary>
    private (string Title, string Job)? LockedRank(Lobby lobby, ICommonSession session) =>
        lobby.Scenario != WFRoundScenario.FactionShip && InCrewCompany(lobby, session) && JoinsAsRank(lobby, session) ? _companies.RankFor(session) : null;

    /// <summary>The member's rank in the crew's company, for the icon that stands in for their job's.</summary>
    private int CrewRankId(Lobby lobby, NetUserId user, Member member)
    {
        if (_players.TryGetSessionById(user, out var session))
            member.RankId = lobby.CompanyId is { } id ? _companies.RankIdFor(session, id) : 0;
        return member.RankId;
    }

    /// <summary>Keeps unspawned crews in step with their company: a host who can't field it drops it, members in it play
    /// their ranks, and anyone no longer in it goes back to choosing a job. True when anything changed.</summary>
    private bool SyncCompanyLocks()
    {
        var changed = false;
        foreach (var lobby in _lobbies.Values)
        {
            if (lobby.Started)
                continue;
            if (lobby.HangarLicence == null && lobby.CompanyId is { } id && _players.TryGetSessionById(lobby.Host, out var host) && !_companies.CanFieldCrew(host, id))
            {
                SetCrewCompany(lobby, host, null);
                changed = true;
            }
            foreach (var (user, member) in lobby.Members)
            {
                if (member.Spawned || !_players.TryGetSessionById(user, out var session))
                    continue;
                if (LockedRank(lobby, session) is { } rank)
                {
                    if (member.RankLocked && member.Job == rank.Job && member.Title == rank.Title && member.RoleId == null)
                        continue;
                    if (member.Job != rank.Job)
                        UnreadyMember(user, member);
                    member.RankLocked = true;
                    member.RoleId = null;
                    member.Job = rank.Job;
                    member.Station = null;
                    member.Title = rank.Title;
                    changed = true;
                }
                else if (member.RankLocked)
                {
                    UnreadyMember(user, member);
                    member.RankLocked = false;
                    member.Job = null;
                    member.Station = null;
                    member.Title = string.Empty;
                    changed = true;
                }
            }
        }
        return changed;
    }

    /// <summary>A crewmate's shown title: their own, their opening's, or their company rank if their job says nothing else.</summary>
    private string RosterTitle(Lobby lobby, NetUserId user, Member member)
    {
        if (member.Title.Length > 0)
            return member.Title;
        if (member.RoleId is { } roleId && lobby.Roles.TryGetValue(roleId, out var role) && role.Name.Length > 0)
            return role.Name;
        return lobby.Scenario != WFRoundScenario.FactionShip && _players.TryGetSessionById(user, out var session) &&
            _companies.RankFor(session) is { } rank && (member.Job == rank.Job || member.Job == WFCompanyPolicy.OverflowJob) ? rank.Title : string.Empty;
    }

    /// <summary>The request and membership role id of a member who joined as their company rank.</summary>
    private const int RankRole = -1;

    /// <summary>Whether the session's character may join this crew as their company rank, in any crew that can field it.</summary>
    private bool JoinsAsRank(Lobby lobby, ICommonSession session)
    {
        if (_companies.CompanyIdOf(session) == null || _companies.RankFor(session) is not { } rank)
            return false;
        // A live member keeps their job; a new spawn needs the rank's job to be one this crew can field.
        return !CanEnter(session) || ScenarioJobs(lobby.Scenario, lobby.Faction).Contains(rank.Job) && IsJobAllowed(session, rank.Job);
    }

    /// <summary>Adds a company member as their rank: its base job and its title on their ID, without taking an opening.</summary>
    private bool AddRankMember(Lobby lobby, ICommonSession session)
    {
        if (FindLobby(session.UserId) != null || lobby.Members.Count >= WFRoundSetupPolicy.MaximumMembers || !JoinsAsRank(lobby, session) ||
            _companies.RankFor(session) is not { } rank)
            return false;
        if (!CanEnter(session))
        {
            if (!CanJoinLive(session) || !WFRoundSetupPolicy.AllowsLiveMembers(lobby.Scenario, lobby.Started))
                return false;
            RegisterLiveMember(lobby, session, null);
        }
        else
            RegisterMember(lobby, session, new Member { Name = session.Name, Job = rank.Job, Title = rank.Title,
                RankLocked = lobby.Scenario != WFRoundScenario.FactionShip && InCrewCompany(lobby, session) });
        NotifyCrewJoined(lobby, session.UserId);
        return true;
    }

    /// <summary>How many crews flew under a company this round.</summary>
    public int CompanyCrewCount(int companyId) => _lobbies.Values.Count(lobby => lobby.Started && lobby.CompanyId == companyId);

    /// <summary>Marks every company crew's ship for IFF and unmarks ships that left it.</summary>
    private void SyncCompanyShips()
    {
        var marked = new HashSet<EntityUid>();
        foreach (var lobby in _lobbies.Values.Where(lobby => lobby.Started))
        {
            if (CrewCompany(lobby) is not { } company)
                continue;
            foreach (var grid in CrewGrids(lobby))
            {
                if (TerminatingOrDeleted(grid))
                    continue;
                marked.Add(grid);
                var ship = EnsureComp<WFCompanyShipComponent>(grid);
                if (ship.CompanyId == lobby.CompanyId && ship.Name == company.Name && ship.Tag == company.Tag && ship.Colour == company.Colour)
                    continue;
                ship.CompanyId = lobby.CompanyId!.Value;
                ship.Name = company.Name;
                ship.Tag = company.Tag;
                ship.Colour = company.Colour;
                Dirty(grid, ship);
            }
        }
        foreach (var grid in _companyShips.Where(grid => !marked.Contains(grid)))
        {
            if (!TerminatingOrDeleted(grid))
                RemComp<WFCompanyShipComponent>(grid);
        }
        _companyShips.Clear();
        _companyShips.UnionWith(marked);
    }
}
