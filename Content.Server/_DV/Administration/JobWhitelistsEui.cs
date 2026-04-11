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
using Robust.Shared.Log;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Server._DV.Administration;

public sealed class JobWhitelistsEui : BaseEui
{
    [Dependency] private readonly IAdminManager _admin = default!;
    [Dependency] private readonly ILogManager _log = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IServerDbManager _db = default!;
    [Dependency] private readonly JobWhitelistManager _jobWhitelist = default!;
    [Dependency] private readonly CompanyManager _companyManager = default!;

    private readonly ISawmill _sawmill;

    public NetUserId PlayerId;
    public string PlayerName;

    public HashSet<ProtoId<JobPrototype>> Whitelists = new();
    public HashSet<ProtoId<GhostRolePrototype>> GhostRoleWhitelists = new(); // Frontier
    public HashSet<ProtoId<CompanyPrototype>> CompanyWhitelists = new(); // Forge-Change: company whitelist
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

        StateDirty();
    }

    public override EuiStateBase GetNewState()
    {
        return new JobWhitelistsEuiState(PlayerName, Whitelists, GhostRoleWhitelists, CompanyWhitelists.Select(c => c).ToHashSet(), GlobalWhitelist); // Forge-Change: company whitelist
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
                var companyArgs = (SetCompanyWhitelistedMessage)msg;
                if (!_proto.HasIndex<CompanyPrototype>(companyArgs.CompanyId))
                    return;

                added = companyArgs.Whitelisting;
                role = $"company:{companyArgs.CompanyId}";
                if (added)
                {
                    _companyManager.AddMember(PlayerId, companyArgs.CompanyId);
                    CompanyWhitelists.Add(companyArgs.CompanyId);
                }
                else
                {
                    _ = _companyManager.RemoveMember(PlayerId, companyArgs.CompanyId);
                    CompanyWhitelists.Remove(companyArgs.CompanyId);
                }
                break;
            // Forge-Change-end: company whitelist
            default:
                return;
        }

        var verb = added ? "added" : "removed";
        _sawmill.Info($"{Player.Name} ({Player.UserId}) {verb} whitelist for {role} to player {PlayerName} ({PlayerId.UserId})");
        // End Frontier

        StateDirty();
    }
}
