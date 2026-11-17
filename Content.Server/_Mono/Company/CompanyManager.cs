using System.Linq;
using System.Threading.Tasks;
using Content.Server.Database;
using Content.Server.Preferences.Managers;
using Content.Shared._Forge.Company;
using Content.Shared._Mono.CCVar;
using Content.Shared._Mono.Company;
using Content.Shared.Preferences;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._Mono.Company;

public sealed partial class CompanyManager
{
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private IServerDbManager _db = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private ILogManager _log = default!;
    [Dependency] private IServerPreferencesManager _prefs = default!;

    private ISawmill _sawmill = default!;

    private readonly Dictionary<ProtoId<CompanyPrototype>, HashSet<CompanyMemberRecord>> _companies = new();

    public void Initialize()
    {
        _sawmill = _log.GetSawmill("company.manager");

        _net.RegisterNetMessage<MsgCompanyWhitelist>();
        _net.Connected += OnConnected;

        _ = LoadCompaniesData();
    }

    private async Task LoadCompaniesData()
    {
        var sw = new Stopwatch();
        sw.Start();

        var members = await _db.GetAllCompanyMembers();
        foreach (var proto in _proto.EnumeratePrototypes<CompanyPrototype>())
        {
            _companies.Add(proto.ID, new());
            _companies[proto.ID].UnionWith(members.Where(m => m.Company == proto.ID).ToHashSet());
        }

        await LoadOrganizationData();

        _sawmill.Info($"All company members data loaded in {sw.Elapsed.TotalSeconds:.2}s");
    }

    private void OnConnected(object? sender, NetChannelArgs e)
    {
        SendCompanyWhitelist(e.Channel);
    }

    public HashSet<CompanyMemberRecord> GetCompanyMembers(ProtoId<CompanyPrototype> company)
    {
        if (!_companies.TryGetValue(company, out var members))
            return new();
        return members;
    }

    public HashSet<CompanyMemberRecord> GetAllCompanyMembers()
    {
        return _companies.Values.SelectMany(v => v).ToHashSet();
    }

    public CompanyMemberRecord? GetCompanyMember(ProtoId<CompanyPrototype> company, NetUserId player)
    {
        if (!_companies.TryGetValue(company, out var members))
            return null;

        return members.FirstOrNull(m => m.PlayerUserId == player);
    }

    public CompanyMemberRecord? GetCompanyMember(ProtoId<CompanyPrototype> company, NetUserId player, int characterSlot)
    {
        if (!_companies.TryGetValue(company, out var members))
            return null;

        return members.FirstOrNull(m => m.PlayerUserId == player && m.CharacterSlot == characterSlot);
    }

    public CompanyMemberRecord? GetMemberForCharacter(NetUserId player, int characterSlot)
    {
        foreach (var members in _companies.Values)
        {
            var match = members.FirstOrNull(m => m.PlayerUserId == player && m.CharacterSlot == characterSlot);
            if (match != null)
                return match;
        }

        return null;
    }

    public string? GetLeaderCharacterName(ProtoId<CompanyPrototype> company)
    {
        if (!_companies.TryGetValue(company, out var members))
            return null;

        var leader = members.FirstOrNull(m => m.Owner);
        if (leader == null)
            return null;

        return ResolveCharacterDisplayName(leader.Value);
    }

    /// <summary>
    /// Prefer the profile character name for the bound slot; never show a ckey as the leader label.
    /// </summary>
    public string ResolveCharacterDisplayName(CompanyMemberRecord member)
    {
        if (_prefs.TryGetCachedPreferences(new NetUserId(member.PlayerUserId), out var prefs) &&
            prefs.Characters.TryGetValue(member.CharacterSlot, out var profile) &&
            profile is HumanoidCharacterProfile humanoid &&
            !string.IsNullOrWhiteSpace(humanoid.Name))
        {
            return humanoid.Name;
        }

        if (!string.IsNullOrWhiteSpace(member.CharacterName) &&
            !member.CharacterName.Contains('@') &&
            !member.CharacterName.StartsWith("Slot ", StringComparison.Ordinal) &&
            !member.CharacterName.StartsWith("localhost", StringComparison.OrdinalIgnoreCase))
        {
            return member.CharacterName;
        }

        return Loc.GetString("company-pda-leader-unknown");
    }

    public async Task<bool> AddMember(NetUserId player, ProtoId<CompanyPrototype> company, int characterSlot, string characterName)
    {
        // One company per character.
        if (GetMemberForCharacter(player, characterSlot) != null)
            return false;

        if (GetCompanyMembers(company).Count >= CompanyOrgLimits.MaxMembersPerCompany)
            return false;

        var added = await _db.AddCompanyMember(player, company, characterSlot, characterName);
        if (!added)
            return false;

        var member = await _db.GetCompanyMemberForCharacter(player, characterSlot);
        if (member != null)
            _companies[company].Add(member.Value);

        if (_player.TryGetSessionById(player, out var session))
            SendCompanyWhitelist(session.Channel);

        return true;
    }

    /// <summary>
    /// Admin helper: add membership for the first character without a company, or a specific slot.
    /// </summary>
    public async Task<bool> AddMemberForPlayer(NetUserId player, ProtoId<CompanyPrototype> company, int? preferredSlot = null)
    {
        if (!_prefs.TryGetCachedPreferences(player, out var prefs))
        {
            // Offline admin add — use slot 0.
            var slot = preferredSlot ?? 0;
            return await AddMember(player, company, slot, $"Slot {slot}");
        }

        if (preferredSlot != null)
        {
            if (!prefs.Characters.TryGetValue(preferredSlot.Value, out var profile))
                return false;

            var name = profile is HumanoidCharacterProfile humanoid
                ? humanoid.Name
                : $"Slot {preferredSlot.Value}";
            return await AddMember(player, company, preferredSlot.Value, name);
        }

        foreach (var (slot, profile) in prefs.Characters.OrderBy(c => c.Key))
        {
            if (GetMemberForCharacter(player, slot) != null)
                continue;

            var name = profile is HumanoidCharacterProfile humanoid
                ? humanoid.Name
                : $"Slot {slot}";
            return await AddMember(player, company, slot, name);
        }

        return false;
    }

    public bool IsAllowed(ICommonSession session, ProtoId<CompanyPrototype> company)
    {
        if (!_config.GetCVar(MonoCVars.CompanyWhitelist))
            return true;

        if (!_proto.TryIndex(company, out var companyPrototype) ||
            !companyPrototype.Whitelisted)
        {
            return true;
        }

        return IsMember(session.UserId, company);
    }

    public bool IsOwner(ICommonSession session, ProtoId<CompanyPrototype> company)
    {
        return IsOwner(session.UserId, company);
    }

    public bool IsOwner(NetUserId player, ProtoId<CompanyPrototype> company)
    {
        if (!_companies.TryGetValue(company, out var members))
            return false;

        return members.Any(m => m.PlayerUserId == player && m.Owner);
    }

    public bool IsOwnerCharacter(NetUserId player, int characterSlot, ProtoId<CompanyPrototype> company)
    {
        var member = GetCompanyMember(company, player, characterSlot);
        return member is { Owner: true };
    }

    public bool SetOwner(ProtoId<CompanyPrototype> company, NetUserId userId, bool owner, int? characterSlot = null)
    {
        CompanyMemberRecord? target = null;

        if (characterSlot != null)
            target = GetCompanyMember(company, userId, characterSlot.Value);
        else
            target = GetCompanyMember(company, userId);

        if (target is not { } member)
            return false;

        if (owner == member.Owner)
            return true;

        // Player may only have one leader character for this company.
        if (owner && _companies.TryGetValue(company, out var members))
        {
            foreach (var existing in members.Where(m => m.Owner).ToList())
            {
                _db.SetCompanyOwner(company, existing.PlayerUserId, existing.CharacterSlot, false);
                _companies[company].RemoveWhere(w =>
                    w.PlayerUserId == existing.PlayerUserId && w.CharacterSlot == existing.CharacterSlot);
                var cleared = existing;
                cleared.Owner = false;
                _companies[company].Add(cleared);
            }
        }

        _db.SetCompanyOwner(company, userId, member.CharacterSlot, owner);

        _companies[company].RemoveWhere(w =>
            w.PlayerUserId == userId && w.CharacterSlot == member.CharacterSlot);
        member.Owner = owner;
        _companies[company].Add(member);

        var label = string.IsNullOrWhiteSpace(member.CharacterName)
            ? member.LastSeenUserName
            : member.CharacterName;
        _ = AddLog(company, Content.Shared._Forge.Company.CompanyLogType.OwnerChanged,
            owner ? $"{label} became owner" : $"{label} lost ownership",
            null);
        return true;
    }

    public bool IsMember(NetUserId player, ProtoId<CompanyPrototype> company)
    {
        return GetCompanyMember(company, player) != null;
    }

    public async Task RemoveMember(NetUserId player, ProtoId<CompanyPrototype> company, int? characterSlot = null)
    {
        if (characterSlot != null)
            _companies[company].RemoveWhere(w => w.PlayerUserId == player && w.CharacterSlot == characterSlot);
        else
            _companies[company].RemoveWhere(w => w.PlayerUserId == player);

        await _db.RemoveCompanyMember(player, company, characterSlot);

        if (_player.TryGetSessionById(player, out var session))
            SendCompanyWhitelist(session.Channel);
    }

    public HashSet<ProtoId<CompanyPrototype>> GetPlayerCompanies(NetUserId player)
    {
        var res = new HashSet<ProtoId<CompanyPrototype>>();

        foreach (var (key, members) in _companies)
        {
            if (members.Any(m => m.PlayerUserId == player))
                res.Add(key);
        }

        return res;
    }

    public void SendCompanyWhitelist(INetChannel player)
    {
        var msg = new MsgCompanyWhitelist
        {
            Whitelist = GetPlayerCompanies(player.UserId)
        };

        _net.ServerSendMessage(msg, player);
    }

    public bool TryGetCharacterContext(NetUserId player, out int slot, out string characterName)
    {
        slot = 0;
        characterName = string.Empty;

        if (!_prefs.TryGetCachedPreferences(player, out var prefs))
            return false;

        slot = prefs.SelectedCharacterIndex;
        if (prefs.Characters.TryGetValue(slot, out var profile) &&
            profile is HumanoidCharacterProfile humanoid)
        {
            characterName = humanoid.Name;
            return true;
        }

        characterName = $"Slot {slot}";
        return true;
    }
}
