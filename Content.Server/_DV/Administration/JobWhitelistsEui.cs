using System.Linq;  // Forge-Change: company whitelist
using System.Threading.Tasks;
using Content.Server.Administration.Managers;
using Content.Server.Database;
using Content.Server.EUI;
using Content.Server.Players.JobWhitelist;
using Content.Shared.Administration;
using Content.Shared._DV.Administration;
using Content.Shared.Eui;
using Content.Shared.Ghost.Roles; // Frontier
using Content.Shared.Roles;
using Content.Server._Mono.Company; // Forge-Change: company whitelist
using Content.Shared._Mono.Company; // Forge-Change: company whitelist
using Robust.Server.Player;
using Robust.Shared.Log;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Server._DV.Administration;

public sealed partial class JobWhitelistsEui : BaseEui
{
    [Dependency] private readonly IAdminManager _admin = default!;
    [Dependency] private readonly ILogManager _log = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IServerDbManager _db = default!;
    [Dependency] private readonly JobWhitelistManager _jobWhitelist = default!;
    [Dependency] private readonly CompanyManager _companyManager = default!;
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly IEntitySystemManager _entitySystems = default!;

    private readonly ISawmill _sawmill;

    public NetUserId PlayerId;
    public string PlayerName;

    public HashSet<ProtoId<JobPrototype>> Whitelists = new();
    public HashSet<ProtoId<GhostRolePrototype>> GhostRoleWhitelists = new(); // Frontier
    public HashSet<ProtoId<CompanyPrototype>> CompanyWhitelists = new(); // Forge-Change: company whitelist
    public HashSet<ProtoId<CompanyPrototype>> CompanyOwners = new(); // Forge-Change: company owners
    public bool GlobalWhitelist = false;

    public JobWhitelistsEui(NetUserId playerId, string playerName)
    {
        IoCManager.InjectDependencies(this);

        _sawmill = _log.GetSawmill("admin.job_whitelists_eui");

        PlayerId = playerId;
        PlayerName = playerName;
    }

    public async void LoadWhitelists()
    {
        var jobs = await _db.GetJobWhitelists(PlayerId.UserId);
        foreach (var id in jobs)
        {
            if (_proto.HasIndex<JobPrototype>(id))
                Whitelists.Add(id);
            else if (_proto.HasIndex<GhostRolePrototype>(id)) // Frontier
                GhostRoleWhitelists.Add(id); // Frontier
        }

        GlobalWhitelist = await _db.GetWhitelistStatusAsync(PlayerId); // Frontier: get global whitelist
        CompanyWhitelists = _companyManager.GetPlayerCompanies(PlayerId); // Forge-Change: company whitelist
        CompanyOwners = new HashSet<ProtoId<CompanyPrototype>>();
        foreach (var company in CompanyWhitelists)
        {
            var member = _companyManager.GetCompanyMember(company, PlayerId);
            if (member is { Owner: true })
                CompanyOwners.Add(company);
        }

        StateDirty();
    }

    public override EuiStateBase GetNewState()
    {
        return new JobWhitelistsEuiState(
            PlayerName,
            Whitelists,
            GhostRoleWhitelists,
            CompanyWhitelists.Select(c => c).ToHashSet(),
            CompanyOwners.Select(c => c).ToHashSet(),
            GlobalWhitelist);
    }

    public override void HandleMessage(EuiMessageBase msg)
    {
        base.HandleMessage(msg);

        if (!_admin.HasAdminFlag(Player, AdminFlags.WhitelistManager))  // Forge-Change: company whitelist
        {
            _sawmill.Warning($"{Player.Name} ({Player.UserId}) tried to change role whitelists for {PlayerName} without whitelists flag");
            return;
        }

        // Frontier: handle ghost role whitelist requests
        bool added;
        string role;
        switch (msg)
        {
            case SetJobWhitelistedMessage:
                var jobArgs = (SetJobWhitelistedMessage)msg;
                if (!_proto.HasIndex(jobArgs.Job))
                    return;

                added = jobArgs.Whitelisting;
                role = jobArgs.Job;
                if (added)
                {
                    _jobWhitelist.AddWhitelist(PlayerId, jobArgs.Job);
                    Whitelists.Add(jobArgs.Job);
                }
                else
                {
                    _jobWhitelist.RemoveWhitelist(PlayerId, jobArgs.Job);
                    Whitelists.Remove(jobArgs.Job);
                }
                break;
            case SetGhostRoleWhitelistedMessage:
                var ghostRoleArgs = (SetGhostRoleWhitelistedMessage)msg;
                if (!_proto.HasIndex(ghostRoleArgs.Role))
                    return;

                added = ghostRoleArgs.Whitelisting;
                role = ghostRoleArgs.Role;
                if (added)
                {
                    _jobWhitelist.AddWhitelist(PlayerId, ghostRoleArgs.Role);
                    GhostRoleWhitelists.Add(ghostRoleArgs.Role);
                }
                else
                {
                    _jobWhitelist.RemoveWhitelist(PlayerId, ghostRoleArgs.Role);
                    GhostRoleWhitelists.Remove(ghostRoleArgs.Role);
                }
                break;
            case SetGlobalWhitelistMessage:
                var globalArgs = (SetGlobalWhitelistMessage)msg;

                added = globalArgs.Whitelisting;
                role = "all roles";
                if (added)
                {
                    _jobWhitelist.AddGlobalWhitelist(PlayerId);
                    GlobalWhitelist = true;
                }
                else
                {
                    _jobWhitelist.RemoveGlobalWhitelist(PlayerId);
                    GlobalWhitelist = false;
                }
                break;
            // Forge-Change-start: company whitelist
            case SetCompanyWhitelistedMessage:
                HandleCompanyWhitelist((SetCompanyWhitelistedMessage)msg);
                return;
            case SetCompanyOwnerMessage:
                HandleCompanyOwner((SetCompanyOwnerMessage)msg);
                return;
            // Forge-Change-end: company whitelist
            default:
                return;
        }

        var verb = added ? "added" : "removed";
        _sawmill.Info($"{Player.Name} ({Player.UserId}) {verb} whitelist for {role} to player {PlayerName} ({PlayerId.UserId})");
        // End Frontier

        StateDirty();
    }

    private async void HandleCompanyWhitelist(SetCompanyWhitelistedMessage companyArgs)
    {
        if (!_proto.HasIndex<CompanyPrototype>(companyArgs.CompanyId))
            return;

        var added = companyArgs.Whitelisting;
        var role = $"company:{companyArgs.CompanyId}";
        if (added)
        {
            var ok = await _companyManager.AddMemberForPlayer(PlayerId, companyArgs.CompanyId);
            if (ok)
            {
                CompanyWhitelists.Add(companyArgs.CompanyId);
                TryApplyCompanyToOnlinePlayer();
            }
            else
            {
                added = false;
                _sawmill.Warning($"Failed to add company membership for {PlayerName}: character already in a company or no free slot");
            }
        }
        else
        {
            await _companyManager.RemoveMember(PlayerId, companyArgs.CompanyId);
            CompanyWhitelists.Remove(companyArgs.CompanyId);
            CompanyOwners.Remove(companyArgs.CompanyId);
        }

        var verb = added ? "added" : "removed";
        _sawmill.Info($"{Player.Name} ({Player.UserId}) {verb} whitelist for {role} to player {PlayerName} ({PlayerId.UserId})");
        StateDirty();
    }

    private async void HandleCompanyOwner(SetCompanyOwnerMessage ownerArgs)
    {
        if (!_proto.HasIndex<CompanyPrototype>(ownerArgs.CompanyId))
            return;

        if (!_companyManager.IsMember(PlayerId, ownerArgs.CompanyId))
        {
            if (!ownerArgs.Owner)
                return;
            var joined = await _companyManager.AddMemberForPlayer(PlayerId, ownerArgs.CompanyId);
            if (!joined)
            {
                _sawmill.Warning($"Failed to add company membership for {PlayerName} before owner assign");
                return;
            }

            CompanyWhitelists.Add(ownerArgs.CompanyId);
            TryApplyCompanyToOnlinePlayer();
        }

        var added = ownerArgs.Owner;
        var role = $"company-owner:{ownerArgs.CompanyId}";
        if (_companyManager.SetOwner(ownerArgs.CompanyId, PlayerId, ownerArgs.Owner))
        {
            if (ownerArgs.Owner)
                CompanyOwners.Add(ownerArgs.CompanyId);
            else
                CompanyOwners.Remove(ownerArgs.CompanyId);
            TryApplyCompanyToOnlinePlayer();
        }

        var verb = added ? "added" : "removed";
        _sawmill.Info($"{Player.Name} ({Player.UserId}) {verb} whitelist for {role} to player {PlayerName} ({PlayerId.UserId})");
        StateDirty();
    }

    private void TryApplyCompanyToOnlinePlayer()
    {
        if (!_players.TryGetSessionById(PlayerId, out var session))
            return;

        _entitySystems.GetEntitySystem<CompanySystem>().TryApplyMembershipToSession(session);
    }
}
