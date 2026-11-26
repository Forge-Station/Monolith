using Content.Shared._WF.RoundSetup;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._WF.RoundSetup;

public sealed partial class WFRoundSetupSystem
{
    /// <summary>Player-run companies are not on this fork. A crew never flies under one.</summary>
    private bool SetCrewCompany(Lobby lobby, ICommonSession host, int? companyId)
    {
        _ = host;
        if (companyId != null)
            return false;

        lobby.CompanyId = null;
        if (WFRoundSetupPolicy.IsCompanyAccess(lobby.Access))
            lobby.Access = WFLobbyAccess.Requests;
        return true;
    }

    private (string Name, string Tag, Color Colour)? CrewCompany(Lobby lobby)
    {
        _ = lobby;
        return null;
    }

    private string TaggedCrewName(Lobby lobby) => CrewName(lobby);

    private static WFRoundSetupCompany[] FieldCompanies(ICommonSession session)
    {
        _ = session;
        return Array.Empty<WFRoundSetupCompany>();
    }

    private static bool InCrewCompany(Lobby lobby, ICommonSession session)
    {
        _ = lobby;
        _ = session;
        return false;
    }

    private static (string Title, string Job)? LockedRank(Lobby lobby, ICommonSession session)
    {
        _ = lobby;
        _ = session;
        return null;
    }

    private static int CrewRankId(Lobby lobby, NetUserId user, Member member)
    {
        _ = lobby;
        _ = user;
        return member.RankId;
    }

    /// <summary>Drops a company a crew was filed under, and unlocks ranks that company used to force.</summary>
    private bool SyncCompanyLocks()
    {
        var changed = false;
        foreach (var lobby in _lobbies.Values)
        {
            if (lobby.Started)
                continue;

            if (lobby.CompanyId != null)
            {
                lobby.CompanyId = null;
                if (WFRoundSetupPolicy.IsCompanyAccess(lobby.Access))
                    lobby.Access = WFLobbyAccess.Requests;
                changed = true;
            }

            foreach (var (user, member) in lobby.Members)
            {
                if (!member.RankLocked || member.Spawned)
                    continue;

                UnreadyMember(user, member);
                member.RankLocked = false;
                member.Job = null;
                member.Station = null;
                member.Title = string.Empty;
                changed = true;
            }
        }

        return changed;
    }

    private static string RosterTitle(Lobby lobby, NetUserId user, Member member)
    {
        _ = user;
        if (member.Title.Length > 0)
            return member.Title;
        if (member.RoleId is { } roleId && lobby.Roles.TryGetValue(roleId, out var role) && role.Name.Length > 0)
            return role.Name;
        return string.Empty;
    }

    /// <summary>The request and membership role id of a member who joined as their company rank.</summary>
    private const int RankRole = -1;

    private static bool JoinsAsRank(Lobby lobby, ICommonSession session)
    {
        _ = lobby;
        _ = session;
        return false;
    }

    private static bool AddRankMember(Lobby lobby, ICommonSession session)
    {
        _ = lobby;
        _ = session;
        return false;
    }

    private static void SyncCompanyShips()
    {
    }
}
