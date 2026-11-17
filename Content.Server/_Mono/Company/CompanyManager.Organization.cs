using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.Database;
using Content.Shared._Forge.Company;
using Content.Shared._Mono.Company;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._Mono.Company;

public readonly record struct CompanyPeaceOutcome(bool Ok, bool Concluded, int Percent, int Amount);

public enum CompanyAllianceFail : byte
{
    None,
    Invalid,
    AtWar,
    Cooldown,
    NoFunds,
    TheirNoFunds,
}

public readonly record struct CompanyAllianceOutcome(bool Ok, bool Concluded, bool AlreadyOffered, CompanyAllianceFail Fail);

public readonly record struct CompanyRelationResult(bool Ok, IReadOnlyList<string> JoinedAllies);

public sealed partial class CompanyManager
{
    private readonly Dictionary<string, List<CompanyRole>> _roles = new();
    private readonly Dictionary<string, int> _bankBalances = new();
    private readonly Dictionary<string, string> _bulletins = new();
    private readonly Dictionary<string, List<CompanyRelation>> _relations = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _bankLocks = new();
    private readonly Dictionary<(string Company, Guid Actor), TimeSpan> _inviteCooldowns = new();
    private readonly Dictionary<(string Company, Guid Actor), TimeSpan> _relationCooldowns = new();
    private readonly Dictionary<(string Company, string Other), (bool Free, int Percent)> _peace = new();
    private readonly HashSet<(string From, string To)> _allianceOffers = new();
    private readonly HashSet<string> _noAutoJoinAllyWars = new();
    private readonly SemaphoreSlim _diplomacy = new(1, 1);

    [Dependency] private readonly IGameTiming _timing = default!;

    private SemaphoreSlim GetBankLock(string companyId) =>
        _bankLocks.GetOrAdd(companyId, static _ => new SemaphoreSlim(1, 1));

    private bool CheckCooldown(Dictionary<(string Company, Guid Actor), TimeSpan> map, string company, Guid actor, int seconds)
    {
        var key = (company, actor);
        var now = _timing.CurTime;
        if (map.TryGetValue(key, out var next) && now < next)
            return false;

        map[key] = now + TimeSpan.FromSeconds(seconds);
        return true;
    }

    private async Task LoadOrganizationData()
    {
        foreach (var proto in _proto.EnumeratePrototypes<CompanyPrototype>())
        {
            if (proto.ID == "None")
                continue;

            await _db.EnsureDefaultCompanyRole(proto.ID);
            await _db.EnsureCompanyBankAccount(proto.ID);

            _roles[proto.ID] = await _db.GetCompanyRoles(proto.ID);
            if (_companies.TryGetValue(proto.ID, out var companyMembers))
            {
                foreach (var member in companyMembers.Where(member => member.Owner).ToList())
                    ApplyPresetRole(proto.ID, member, CompanyRolePresets.Leader);
            }

            _bankBalances[proto.ID] = await _db.GetCompanyBankBalance(proto.ID);
            _bulletins[proto.ID] = await _db.GetCompanyBulletin(proto.ID);
            _relations[proto.ID] = await _db.GetCompanyRelations(proto.ID);
        }

        await _db.EnsureCompanyPeaceTable();
        foreach (var term in await _db.GetCompanyPeaceTerms())
            _peace[(term.CompanyId, term.OtherId)] = (term.FreeOffer, term.RansomPercent);

        await _db.EnsureCompanyAllianceTable();
        foreach (var offer in await _db.GetCompanyAllianceOffers())
            _allianceOffers.Add((offer.CompanyId, offer.OtherId));

        await _db.EnsureCompanyAllyWarTable();
        foreach (var row in await _db.GetCompanyAllyWars())
        {
            if (!row.AutoJoin)
                _noAutoJoinAllyWars.Add(row.CompanyId);
        }

        await LoadDevelopment();
        await LoadJobs();

        // Clean leftover resolved invite rows from older builds.
        await _db.PurgeResolvedCompanyInvitations();
    }

    public CompanyPermission GetPermissions(ICommonSession session, ProtoId<CompanyPrototype> company)
    {
        // Strict character-slot isolation: never inherit another character's rights on this account.
        if (!TryGetCharacterContext(session.UserId, out var slot, out _))
            return CompanyPermission.None;

        if (IsOwnerCharacter(session.UserId, slot, company))
            return CompanyPermission.All;

        var bySlot = GetCompanyMember(company, session.UserId, slot);
        if (bySlot == null)
            return CompanyPermission.None;

        var role = ResolveRole(company, bySlot.Value.RoleId);
        return role == null
            ? CompanyPermission.MemberDefault
            : (CompanyPermission)role.Permissions;
    }

    public bool HasPermission(ICommonSession session, ProtoId<CompanyPrototype> company, CompanyPermission permission)
    {
        return (GetPermissions(session, company) & permission) == permission;
    }

    public CompanyAccessTier GetAccessTier(NetUserId userId, ProtoId<CompanyPrototype> company)
    {
        if (!TryGetCharacterContext(userId, out var slot, out _))
            return CompanyAccessTier.None;

        var bySlot = GetCompanyMember(company, userId, slot);
        if (bySlot == null)
            return CompanyAccessTier.None;

        if (bySlot.Value.Owner)
            return CompanyAccessTier.High;

        var role = ResolveRole(company, bySlot.Value.RoleId);
        return role == null
            ? CompanyAccessTier.Low
            : (CompanyAccessTier)role.AccessTier;
    }

    /// <summary>
    /// Whether the player's currently selected character is the company owner.
    /// Prefer this over account-wide <see cref="IsOwner"/> for gameplay authz.
    /// </summary>
    public bool IsOwnerSession(ICommonSession session, ProtoId<CompanyPrototype> company)
    {
        if (!TryGetCharacterContext(session.UserId, out var slot, out _))
            return false;

        return IsOwnerCharacter(session.UserId, slot, company);
    }

    public CompanyAccessTier GetMaxGrantableAccessTier(ICommonSession session, ProtoId<CompanyPrototype> company)
    {
        if (IsOwnerSession(session, company))
            return CompanyAccessTier.High;

        var tier = GetAccessTier(session.UserId, company);
        return tier < CompanyAccessTier.Low ? CompanyAccessTier.Low : tier;
    }

    public CompanyAccessTier ClampGrantableAccessTier(ICommonSession session, ProtoId<CompanyPrototype> company, CompanyAccessTier requested)
    {
        var max = GetMaxGrantableAccessTier(session, company);
        if (requested < CompanyAccessTier.Low)
            requested = CompanyAccessTier.Low;
        return requested > max ? max : requested;
    }

    public bool CanAssignRole(ICommonSession session, ProtoId<CompanyPrototype> company, Guid roleId)
    {
        if (IsOwnerSession(session, company))
            return true;

        if (!_roles.TryGetValue(company, out var roles))
            return false;

        var role = roles.FirstOrDefault(r => r.Id == roleId);
        if (role == null)
            return false;

        var actorPerms = GetPermissions(session, company);
        var actorTier = GetAccessTier(session.UserId, company);
        var rolePerms = (CompanyPermission)role.Permissions;
        var roleTier = (CompanyAccessTier)role.AccessTier;

        if ((rolePerms & ~actorPerms) != CompanyPermission.None)
            return false;
        if (roleTier > actorTier)
            return false;

        return true;
    }

    public Guid? PresetRoleId(ProtoId<CompanyPrototype> company, string presetName)
    {
        if (!_roles.TryGetValue(company.Id, out var roles))
            return null;

        return roles.FirstOrDefault(role => role.Name == presetName)?.Id;
    }

    public void ApplyPresetRole(ProtoId<CompanyPrototype> company, CompanyMemberRecord member, string presetName)
    {
        var roleId = PresetRoleId(company, presetName);
        if (roleId == null || member.RoleId == roleId)
            return;

        if (!_companies.TryGetValue(company, out var members))
            return;

        members.RemoveWhere(item => item.PlayerUserId == member.PlayerUserId && item.CharacterSlot == member.CharacterSlot);
        member.RoleId = roleId;
        members.Add(member);
        _ = _db.SetCompanyMemberRole(company, member.PlayerUserId, member.CharacterSlot, roleId);
    }

    public CompanyRole? ResolveRole(ProtoId<CompanyPrototype> company, Guid? roleId)
    {
        if (!_roles.TryGetValue(company.Id, out var roles))
            return null;

        if (roleId != null)
        {
            var match = roles.FirstOrDefault(r => r.Id == roleId);
            if (match != null)
                return match;
        }

        return roles.FirstOrDefault(r => r.IsDefault) ?? roles.FirstOrDefault();
    }

    public List<CompanyRoleData> GetRoleDatas(ProtoId<CompanyPrototype> company)
    {
        if (!_roles.TryGetValue(company.Id, out var roles))
            return new();

        return roles.Select(r => new CompanyRoleData
        {
            RoleId = r.Id,
            Name = r.Name,
            Permissions = (CompanyPermission)r.Permissions,
            AccessTier = (CompanyAccessTier)r.AccessTier,
            IsDefault = r.IsDefault,
        }).ToList();
    }

    public async Task<CompanyRoleData?> CreateRole(ProtoId<CompanyPrototype> company, string name, CompanyPermission permissions, CompanyAccessTier tier)
    {
        name = name.Trim();
        if (name.Length is < 1 or > CompanyOrgLimits.MaxRoleNameLength)
            return null;

        if (!_roles.TryGetValue(company, out var roles))
        {
            roles = new List<CompanyRole>();
            _roles[company] = roles;
        }

        if (roles.Count >= CompanyOrgLimits.MaxRolesPerCompany)
            return null;

        if (roles.Any(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase)))
            return null;

        if (tier < CompanyAccessTier.Low)
            tier = CompanyAccessTier.Low;
        if (tier > CompanyAccessTier.High)
            tier = CompanyAccessTier.High;

        var role = new CompanyRole
        {
            Id = Guid.NewGuid(),
            CompanyId = company,
            Name = name,
            Permissions = (long)permissions,
            AccessTier = (int)tier,
            IsDefault = false,
        };

        await _db.UpsertCompanyRole(role);
        roles.Add(role);

        await AddLog(company, CompanyLogType.RoleCreated, $"Role '{name}' created", null);
        return new CompanyRoleData
        {
            RoleId = role.Id,
            Name = role.Name,
            Permissions = permissions,
            AccessTier = tier,
            IsDefault = false,
        };
    }

    public async Task<bool> UpdateRole(ProtoId<CompanyPrototype> company, Guid roleId, string name, CompanyPermission permissions, CompanyAccessTier tier)
    {
        name = name.Trim();
        if (name.Length is < 1 or > CompanyOrgLimits.MaxRoleNameLength)
            return false;

        if (!_roles.TryGetValue(company, out var roles))
            return false;

        var role = roles.FirstOrDefault(r => r.Id == roleId);
        if (role == null)
            return false;

        if (roles.Any(r => r.Id != roleId && string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase)))
            return false;

        if (tier < CompanyAccessTier.Low)
            tier = CompanyAccessTier.Low;
        if (tier > CompanyAccessTier.High)
            tier = CompanyAccessTier.High;

        role.Name = name;
        role.Permissions = (long)permissions;
        role.AccessTier = (int)tier;
        await _db.UpsertCompanyRole(role);
        await AddLog(company, CompanyLogType.RoleUpdated, $"Role '{name}' updated", null);
        return true;
    }

    public async Task<bool> DeleteRole(ProtoId<CompanyPrototype> company, Guid roleId)
    {
        if (!_roles.TryGetValue(company, out var roles))
            return false;

        var role = roles.FirstOrDefault(r => r.Id == roleId);
        if (role == null || role.IsDefault || CompanyRolePresets.IsPreset(role.Name))
            return false;

        await _db.DeleteCompanyRole(roleId);
        roles.Remove(role);

        if (_companies.TryGetValue(company, out var members))
        {
            var updated = new HashSet<CompanyMemberRecord>();
            foreach (var m in members)
            {
                if (m.RoleId == roleId)
                {
                    var copy = m;
                    copy.RoleId = null;
                    updated.Add(copy);
                }
                else
                {
                    updated.Add(m);
                }
            }

            _companies[company] = updated;
        }

        await AddLog(company, CompanyLogType.RoleDeleted, $"Role '{role.Name}' deleted", null);
        return true;
    }

    public async Task<bool> AssignRole(ProtoId<CompanyPrototype> company, NetUserId player, Guid roleId, NetUserId? actor = null, int? characterSlot = null)
    {
        if (!_roles.TryGetValue(company, out var roles) || roles.All(r => r.Id != roleId))
            return false;

        var cached = characterSlot != null
            ? GetCompanyMember(company, player, characterSlot.Value)
            : GetCompanyMember(company, player);
        if (cached is not { } member)
            return false;

        await _db.SetCompanyMemberRole(company, player.UserId, member.CharacterSlot, roleId);
        _companies[company].RemoveWhere(w =>
            w.PlayerUserId == player && w.CharacterSlot == member.CharacterSlot);
        member.RoleId = roleId;
        _companies[company].Add(member);

        var roleName = roles.First(r => r.Id == roleId).Name;
        var label = string.IsNullOrWhiteSpace(member.CharacterName)
            ? member.LastSeenUserName
            : member.CharacterName;
        await AddLog(company, CompanyLogType.RoleAssigned, $"{label} assigned role '{roleName}'", actor);
        return true;
    }

    public int GetBankBalance(ProtoId<CompanyPrototype> company)
    {
        return _bankBalances.GetValueOrDefault(company.Id, 0);
    }

    public bool IsAtWar(ProtoId<CompanyPrototype> company)
    {
        if (!_relations.TryGetValue(company, out var relations))
            return false;

        foreach (var rel in relations)
        {
            if ((CompanyRelationKind)rel.RelationType == CompanyRelationKind.War)
                return true;
        }

        return false;
    }

    public int GetSpendableBalance(ProtoId<CompanyPrototype> company)
    {
        var balance = GetBankBalance(company);
        if (!IsAtWar(company))
            return balance;

        return Math.Max(0, balance - CompanyOrgLimits.WarTreasuryReserve);
    }

    public async Task<bool> TryDepositBank(ProtoId<CompanyPrototype> company, int amount, NetUserId? actor = null)
    {
        if (amount <= 0 || amount > CompanyOrgLimits.MaxBankTransaction)
            return false;

        var gate = GetBankLock(company);
        await gate.WaitAsync();
        try
        {
            var balance = GetBankBalance(company);
            var next = (long)balance + amount;
            if (next > CompanyOrgLimits.MaxBankBalance)
                return false;

            balance = (int)next;
            _bankBalances[company] = balance;
            await _db.SetCompanyBankBalance(company, balance);
            await AddLog(company, CompanyLogType.BankDeposit, $"Deposited {amount}", actor);
            return true;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<bool> TryWithdrawBank(ProtoId<CompanyPrototype> company, int amount, NetUserId? actor = null, bool ignoreWarFreeze = false)
    {
        if (amount <= 0 || amount > CompanyOrgLimits.MaxBankTransaction)
            return false;

        var gate = GetBankLock(company);
        await gate.WaitAsync();
        try
        {
            var balance = GetBankBalance(company);
            var available = !ignoreWarFreeze && IsAtWar(company)
                ? Math.Max(0, balance - CompanyOrgLimits.WarTreasuryReserve)
                : balance;
            if (available < amount)
                return false;

            balance -= amount;
            _bankBalances[company] = balance;
            await _db.SetCompanyBankBalance(company, balance);
            await AddLog(company, CompanyLogType.BankWithdraw, $"Withdrew {amount}", actor);
            return true;
        }
        finally
        {
            gate.Release();
        }
    }

    public string GetBulletin(ProtoId<CompanyPrototype> company)
    {
        return _bulletins.GetValueOrDefault(company.Id, string.Empty);
    }

    public async Task SetBulletin(ProtoId<CompanyPrototype> company, string text, NetUserId? actor = null)
    {
        text ??= string.Empty;
        if (text.Length > CompanyOrgLimits.MaxBulletinLength)
            text = text[..CompanyOrgLimits.MaxBulletinLength];

        var previous = GetBulletin(company);
        if (previous == text)
            return;

        _bulletins[company] = text;
        await _db.SetCompanyBulletin(company, text);
        await AddLog(company, CompanyLogType.BulletinUpdated, "Bulletin updated", actor);
    }

    public List<CompanyRelationData> GetRelationDatas(ProtoId<CompanyPrototype> company)
    {
        var relations = _relations.GetValueOrDefault(company) ?? new List<CompanyRelation>();

        var result = new List<CompanyRelationData>();
        foreach (var rel in relations)
        {
            var otherId = rel.CompanyAId == company.Id ? rel.CompanyBId : rel.CompanyAId;
            if (!_proto.TryIndex<CompanyPrototype>(otherId, out var other))
                continue;

            var ours = _peace.GetValueOrDefault((company.Id, otherId));
            var theirs = _peace.GetValueOrDefault((otherId, company.Id));
            var data = new CompanyRelationData
            {
                OtherCompany = otherId,
                OtherCompanyName = other.Name,
                Kind = (CompanyRelationKind)rel.RelationType,
                IconId = other.Icon?.Id,
                WeOfferFreePeace = ours.Free,
                OurRansomPercent = ours.Percent,
                TheyOfferFreePeace = theirs.Free,
                TheirRansomPercent = theirs.Percent,
                TheirRansomAmount = (int)((long)GetBankBalance(company) * theirs.Percent / 100),
            };
            ApplyAllianceOffer(data, company.Id, otherId);
            result.Add(data);
        }

        foreach (var (from, to) in _allianceOffers)
        {
            var otherId = from == company.Id ? to : to == company.Id ? from : null;
            if (otherId == null || result.Any(row => row.OtherCompany.Id == otherId))
                continue;
            if (!_proto.TryIndex<CompanyPrototype>(otherId, out var other))
                continue;

            var data = new CompanyRelationData
            {
                OtherCompany = otherId,
                OtherCompanyName = other.Name,
                Kind = CompanyRelationKind.Neutral,
                IconId = other.Icon?.Id,
            };
            ApplyAllianceOffer(data, company.Id, otherId);
            result.Add(data);
        }

        return result;
    }

    private void ApplyAllianceOffer(CompanyRelationData data, string companyId, string otherId)
    {
        data.WeOfferAlliance = _allianceOffers.Contains((companyId, otherId));
        data.TheyOfferAlliance = _allianceOffers.Contains((otherId, companyId));
    }

    public CompanyRelationKind GetRelation(ProtoId<CompanyPrototype> company, ProtoId<CompanyPrototype> other)
    {
        if (!_relations.TryGetValue(company, out var relations))
            return CompanyRelationKind.Neutral;

        foreach (var rel in relations)
        {
            var otherId = rel.CompanyAId == company.Id ? rel.CompanyBId : rel.CompanyAId;
            if (otherId == other.Id)
                return (CompanyRelationKind)rel.RelationType;
        }

        return CompanyRelationKind.Neutral;
    }

    public bool AutoJoinsAllyWars(ProtoId<CompanyPrototype> company)
    {
        return !_noAutoJoinAllyWars.Contains(company.Id);
    }

    public async Task SetAutoJoinAllyWars(ProtoId<CompanyPrototype> company, bool enabled)
    {
        if (enabled)
            _noAutoJoinAllyWars.Remove(company.Id);
        else
            _noAutoJoinAllyWars.Add(company.Id);

        await _db.SetCompanyAllyWar(company.Id, enabled);
    }

    public async Task<CompanyRelationResult> SetRelation(ProtoId<CompanyPrototype> company, ProtoId<CompanyPrototype> other, CompanyRelationKind kind, NetUserId? actor = null)
    {
        if (kind == CompanyRelationKind.Alliance)
            return new(false, Array.Empty<string>());

        await _diplomacy.WaitAsync();
        try
        {
            if (!await SetRelationUnlocked(company, other, kind, actor))
                return new(false, Array.Empty<string>());

            if (kind != CompanyRelationKind.War)
                return new(true, Array.Empty<string>());

            return new(true, await PullAlliesIntoWar(other, company));
        }
        finally
        {
            _diplomacy.Release();
        }
    }

    /// <summary>
    /// Allies of <paramref name="victim"/> that still have automatic defense enter the war against <paramref name="aggressor"/>.
    /// This wave does not pull anyone else in.
    /// </summary>
    private async Task<IReadOnlyList<string>> PullAlliesIntoWar(ProtoId<CompanyPrototype> victim, ProtoId<CompanyPrototype> aggressor)
    {
        var allies = new List<string>();
        foreach (var proto in _proto.EnumeratePrototypes<CompanyPrototype>())
        {
            if (proto.Hidden || proto.ID is "None" || proto.ID == victim.Id || proto.ID == aggressor.Id)
                continue;
            if (GetRelation(victim, proto.ID) != CompanyRelationKind.Alliance)
                continue;
            if (!AutoJoinsAllyWars(proto.ID))
                continue;
            if (GetRelation(proto.ID, aggressor) == CompanyRelationKind.War)
                continue;

            allies.Add(proto.ID);
        }

        var victimName = _proto.TryIndex(victim, out var victimProto) ? victimProto.Name : victim.Id;
        var aggressorName = _proto.TryIndex(aggressor, out var aggressorProto) ? aggressorProto.Name : aggressor.Id;
        var joined = new List<string>();
        foreach (var ally in allies)
        {
            if (!await SetRelationUnlocked(
                    ally,
                    aggressor,
                    CompanyRelationKind.War,
                    null,
                    skipCooldown: true,
                    logMessage: $"Joined the war against {aggressorName} after the attack on {victimName}"))
                continue;

            joined.Add(ally);
        }

        return joined;
    }

    private async Task<bool> SetRelationUnlocked(ProtoId<CompanyPrototype> company, ProtoId<CompanyPrototype> other, CompanyRelationKind kind, NetUserId? actor, bool skipCooldown = false, string? logMessage = null)
    {
        if (!TryValidateRelationTarget(company, other))
            return false;

        if (!skipCooldown && actor != null && !CheckCooldown(_relationCooldowns, company, actor.Value.UserId, RelationCooldownSeconds(company)))
            return false;

        if (kind == CompanyRelationKind.Neutral)
            return await ClearRelationInternal(company, other, actor, skipCooldown: true);

        await _db.SetCompanyRelation(company, other, (int)kind);
        _relations[company] = await _db.GetCompanyRelations(company);
        _relations[other.Id] = await _db.GetCompanyRelations(other);

        var logType = kind == CompanyRelationKind.War ? CompanyLogType.WarDeclared : CompanyLogType.AllianceDeclared;
        var otherName = _proto.TryIndex(other, out var proto) ? proto.Name : other.Id;
        await AddLog(company, logType, logMessage ?? $"{kind} with {otherName}", actor);
        await ClearPeaceTerms(company, other);
        await ClearAllianceOffers(company, other);
        return true;
    }

    public async Task<bool> ClearRelation(ProtoId<CompanyPrototype> company, ProtoId<CompanyPrototype> other, NetUserId? actor = null)
    {
        await _diplomacy.WaitAsync();
        try
        {
            return await ClearRelationUnlocked(company, other, actor);
        }
        finally
        {
            _diplomacy.Release();
        }
    }

    private async Task<bool> ClearRelationUnlocked(ProtoId<CompanyPrototype> company, ProtoId<CompanyPrototype> other, NetUserId? actor)
    {
        if (!TryValidateRelationTarget(company, other))
            return false;

        if (actor != null && !CheckCooldown(_relationCooldowns, company, actor.Value.UserId, RelationCooldownSeconds(company)))
            return false;

        return await ClearRelationInternal(company, other, actor, skipCooldown: true);
    }

    private async Task<bool> ClearRelationInternal(ProtoId<CompanyPrototype> company, ProtoId<CompanyPrototype> other, NetUserId? actor, bool skipCooldown)
    {
        await _db.ClearCompanyRelation(company, other);
        _relations[company] = await _db.GetCompanyRelations(company);
        _relations[other.Id] = await _db.GetCompanyRelations(other);
        var otherName = _proto.TryIndex(other, out var proto) ? proto.Name : other.Id;
        await AddLog(company, CompanyLogType.RelationCleared, $"Cleared relation with {otherName}", actor);
        await ClearPeaceTerms(company, other);
        await ClearAllianceOffers(company, other);
        return true;
    }

    public async Task<CompanyAllianceOutcome> TryOfferAlliance(ProtoId<CompanyPrototype> company, ProtoId<CompanyPrototype> other, NetUserId? actor)
    {
        await _diplomacy.WaitAsync();
        try
        {
            if (!TryValidateRelationTarget(company, other))
                return new(false, false, false, CompanyAllianceFail.Invalid);

            var current = GetRelation(company, other);
            if (current == CompanyRelationKind.Alliance)
                return new(false, false, false, CompanyAllianceFail.Invalid);
            if (current == CompanyRelationKind.War)
                return new(false, false, false, CompanyAllianceFail.AtWar);

            var ours = _allianceOffers.Contains((company.Id, other.Id));
            var theirs = _allianceOffers.Contains((other.Id, company.Id));
            if (ours && !theirs)
                return new(true, false, true, CompanyAllianceFail.None);

            if (theirs)
            {
                var fail = await TryPayAllianceFees(company, other, actor);
                if (fail != CompanyAllianceFail.None)
                    return new(false, false, false, fail);

                if (actor != null && !CheckCooldown(_relationCooldowns, company, actor.Value.UserId, RelationCooldownSeconds(company)))
                {
                    await RefundAllianceFees(company, other, actor);
                    return new(false, false, false, CompanyAllianceFail.Cooldown);
                }

                if (!await SetRelationUnlocked(company, other, CompanyRelationKind.Alliance, actor, skipCooldown: true))
                {
                    await RefundAllianceFees(company, other, actor);
                    return new(false, false, false, CompanyAllianceFail.Invalid);
                }

                var selfName = _proto.TryIndex(company, out var self) ? self.Name : company.Id;
                await AddLog(other, CompanyLogType.AllianceDeclared, $"Alliance with {selfName}", null);
                return new(true, true, false, CompanyAllianceFail.None);
            }

            if (actor != null && !CheckCooldown(_relationCooldowns, company, actor.Value.UserId, RelationCooldownSeconds(company)))
                return new(false, false, false, CompanyAllianceFail.Cooldown);

            await SaveAllianceOffer(company, other);
            var otherName = _proto.TryIndex(other, out var offered) ? offered.Name : other.Id;
            await AddLog(company, CompanyLogType.AllianceDeclared, $"Offered alliance to {otherName}", actor);
            return new(true, false, false, CompanyAllianceFail.None);
        }
        finally
        {
            _diplomacy.Release();
        }
    }

    public async Task<bool> TryWithdrawAllianceOffer(ProtoId<CompanyPrototype> company, ProtoId<CompanyPrototype> other, NetUserId? actor)
    {
        await _diplomacy.WaitAsync();
        try
        {
            if (!TryValidateRelationTarget(company, other))
                return false;
            if (!_allianceOffers.Remove((company.Id, other.Id)))
                return false;

            await _db.DeleteCompanyAllianceOffer(company.Id, other.Id);
            var otherName = _proto.TryIndex(other, out var proto) ? proto.Name : other.Id;
            await AddLog(company, CompanyLogType.RelationCleared, $"Withdrew alliance offer to {otherName}", actor);
            return true;
        }
        finally
        {
            _diplomacy.Release();
        }
    }

    private async Task SaveAllianceOffer(ProtoId<CompanyPrototype> company, ProtoId<CompanyPrototype> other)
    {
        _allianceOffers.Add((company.Id, other.Id));
        await _db.AddCompanyAllianceOffer(company.Id, other.Id);
    }

    private async Task ClearAllianceOffers(ProtoId<CompanyPrototype> company, ProtoId<CompanyPrototype> other)
    {
        _allianceOffers.Remove((company.Id, other.Id));
        _allianceOffers.Remove((other.Id, company.Id));
        await _db.DeleteCompanyAlliancePair(company.Id, other.Id);
    }

    private async Task<CompanyAllianceFail> TryPayAllianceFees(ProtoId<CompanyPrototype> company, ProtoId<CompanyPrototype> other, NetUserId? actor)
    {
        var ourCost = ScaleDiplomacy(company, CompanyOrgLimits.AllianceCost);
        var theirCost = ScaleDiplomacy(other, CompanyOrgLimits.AllianceCost);
        if (!await TryWithdrawBank(company, ourCost, actor))
            return CompanyAllianceFail.NoFunds;
        if (await TryWithdrawBank(other, theirCost, null))
            return CompanyAllianceFail.None;

        await TryDepositBank(company, ourCost, actor);
        return CompanyAllianceFail.TheirNoFunds;
    }

    private async Task RefundAllianceFees(ProtoId<CompanyPrototype> company, ProtoId<CompanyPrototype> other, NetUserId? actor)
    {
        await TryDepositBank(company, ScaleDiplomacy(company, CompanyOrgLimits.AllianceCost), actor);
        await TryDepositBank(other, ScaleDiplomacy(other, CompanyOrgLimits.AllianceCost), null);
    }

    public async Task<CompanyPeaceOutcome> TryOfferFreePeace(ProtoId<CompanyPrototype> company, ProtoId<CompanyPrototype> other, NetUserId? actor)
    {
        await _diplomacy.WaitAsync();
        try
        {
            if (!TryValidateRelationTarget(company, other) || GetRelation(company, other) != CompanyRelationKind.War)
                return default;

            var ours = _peace.GetValueOrDefault((company.Id, other.Id));
            var theirs = _peace.GetValueOrDefault((other.Id, company.Id));
            if (ours.Free && theirs.Free)
            {
                await ClearRelationInternal(company, other, actor, skipCooldown: true);
                return new CompanyPeaceOutcome(true, true, 0, 0);
            }

            if (actor != null && !CheckCooldown(_relationCooldowns, company, actor.Value.UserId, RelationCooldownSeconds(company)))
                return default;

            await SavePeaceTerm(company, other, free: true, percent: 0);
            if (_peace.GetValueOrDefault((other.Id, company.Id)).Free)
            {
                await ClearRelationInternal(company, other, actor, skipCooldown: true);
                return new CompanyPeaceOutcome(true, true, 0, 0);
            }

            await AddLog(company, CompanyLogType.RelationCleared, $"Offered free peace to {other.Id}", actor);
            return new CompanyPeaceOutcome(true, false, 0, 0);
        }
        finally
        {
            _diplomacy.Release();
        }
    }

    public async Task<CompanyPeaceOutcome> TrySetPeaceRansom(ProtoId<CompanyPrototype> company, ProtoId<CompanyPrototype> other, int percent, NetUserId? actor)
    {
        await _diplomacy.WaitAsync();
        try
        {
            if (percent < 1 || percent > CompanyOrgLimits.MaxPeaceRansomPercent)
                return default;
            if (!TryValidateRelationTarget(company, other) || GetRelation(company, other) != CompanyRelationKind.War)
                return default;
            if (actor != null && !CheckCooldown(_relationCooldowns, company, actor.Value.UserId, RelationCooldownSeconds(company)))
                return default;

            await SavePeaceTerm(company, other, free: false, percent: percent);
            await AddLog(company, CompanyLogType.RelationCleared, $"Set peace ransom at {percent}% for {other.Id}", actor);
            return new CompanyPeaceOutcome(true, false, percent, 0);
        }
        finally
        {
            _diplomacy.Release();
        }
    }

    public async Task<CompanyPeaceOutcome> TryAcceptPeaceRansom(ProtoId<CompanyPrototype> company, ProtoId<CompanyPrototype> other, NetUserId? actor)
    {
        await _diplomacy.WaitAsync();
        try
        {
            if (!TryValidateRelationTarget(company, other) || GetRelation(company, other) != CompanyRelationKind.War)
                return default;

            var demand = _peace.GetValueOrDefault((other.Id, company.Id));
            if (demand.Percent <= 0)
                return default;
            if (actor != null && !CheckCooldown(_relationCooldowns, company, actor.Value.UserId, RelationCooldownSeconds(company)))
                return default;

            var paid = await TryPayRansom(company, other, demand.Percent, actor);
            if (paid < 0)
                return default;

            await ClearRelationInternal(company, other, actor, skipCooldown: true);
            return new CompanyPeaceOutcome(true, true, demand.Percent, paid);
        }
        finally
        {
            _diplomacy.Release();
        }
    }

    private async Task SavePeaceTerm(ProtoId<CompanyPrototype> company, ProtoId<CompanyPrototype> other, bool free, int percent)
    {
        _peace[(company.Id, other.Id)] = (free, percent);
        await _db.UpsertCompanyPeaceTerm(company.Id, other.Id, free, percent);
    }

    private async Task ClearPeaceTerms(ProtoId<CompanyPrototype> company, ProtoId<CompanyPrototype> other)
    {
        _peace.Remove((company.Id, other.Id));
        _peace.Remove((other.Id, company.Id));
        await _db.DeleteCompanyPeaceTerms(company.Id, other.Id);
    }

    private async Task<int> TryPayRansom(ProtoId<CompanyPrototype> payer, ProtoId<CompanyPrototype> receiver, int percent, NetUserId? actor)
    {
        if (percent < 1 || percent > CompanyOrgLimits.MaxPeaceRansomPercent)
            return -1;

        var first = string.CompareOrdinal(payer.Id, receiver.Id) <= 0 ? payer.Id : receiver.Id;
        var second = first == payer.Id ? receiver.Id : payer.Id;
        var lockA = GetBankLock(first);
        var lockB = GetBankLock(second);
        await lockA.WaitAsync();
        try
        {
            await lockB.WaitAsync();
            try
            {
                var balance = GetBankBalance(payer);
                var amount = (int)((long)balance * percent / 100);
                if (amount <= 0)
                    return 0;

                balance -= amount;
                _bankBalances[payer.Id] = balance;
                await _db.SetCompanyBankBalance(payer, balance);
                await AddLog(payer, CompanyLogType.BankWithdraw, $"Peace ransom {amount} ({percent}%) to {receiver.Id}", actor);

                var receiverBalance = GetBankBalance(receiver);
                var room = CompanyOrgLimits.MaxBankBalance - receiverBalance;
                if (room > 0)
                {
                    var deposit = Math.Min(amount, room);
                    receiverBalance += deposit;
                    _bankBalances[receiver.Id] = receiverBalance;
                    await _db.SetCompanyBankBalance(receiver, receiverBalance);
                    await AddLog(receiver, CompanyLogType.BankDeposit, $"Peace ransom received {deposit}", actor);
                }

                return amount;
            }
            finally
            {
                lockB.Release();
            }
        }
        finally
        {
            lockA.Release();
        }
    }

    private bool TryValidateRelationTarget(ProtoId<CompanyPrototype> company, ProtoId<CompanyPrototype> other)
    {
        if (other == company || other == "None")
            return false;
        if (!_proto.TryIndex(other, out CompanyPrototype? proto) || proto.Hidden)
            return false;
        return true;
    }

    public async Task<CompanyInvitation?> InvitePlayer(ProtoId<CompanyPrototype> company, NetUserId target, NetUserId from)
    {
        if (IsMember(target, company))
            return null;

        if (GetCompanyMembers(company).Count >= MaxMembers(company))
            return null;

        if (!CheckCooldown(_inviteCooldowns, company, from.UserId, CompanyOrgLimits.InviteCooldownSeconds))
            return null;

        if (await _db.HasPendingCompanyInvitation(company, target.UserId))
            return null;

        if (await _db.CountPendingInvitationsForCompany(company) >= CompanyOrgLimits.MaxPendingInvitesPerCompany)
            return null;

        var invite = new CompanyInvitation
        {
            Id = Guid.NewGuid(),
            CompanyId = company,
            TargetUserId = target.UserId,
            FromUserId = from.UserId,
            CreatedAt = DateTime.UtcNow,
            Status = (int)CompanyInvitationStatus.Pending,
        };

        await _db.AddCompanyInvitation(invite);
        var targetName = target.UserId.ToString();
        if (_player.TryGetSessionById(target, out var session))
            targetName = session.Name;

        await AddLog(company, CompanyLogType.Invited, $"Invited {targetName}", from);
        return invite;
    }

    public async Task<bool> AcceptInvite(Guid inviteId, NetUserId acceptor)
    {
        var invite = await _db.GetCompanyInvitation(inviteId);
        if (invite == null || invite.Status != (int)CompanyInvitationStatus.Pending)
            return false;

        if (invite.TargetUserId != acceptor.UserId)
            return false;

        if (!TryGetCharacterContext(acceptor, out var slot, out var characterName))
            return false;

        if (GetMemberForCharacter(acceptor, slot) != null)
            return false;

        // Add membership first so a failed join does not burn the invite.
        if (!await AddMember(acceptor, invite.CompanyId, slot, characterName))
            return false;

        await _db.DeleteCompanyInvitation(inviteId);

        var defaultRole = ResolveRole(invite.CompanyId, null);
        if (defaultRole != null)
            await AssignRole(invite.CompanyId, acceptor, defaultRole.Id, acceptor, slot);

        await AddLog(invite.CompanyId, CompanyLogType.Joined, $"{characterName} joined company", acceptor);
        return true;
    }

    public async Task<bool> DeclineInvite(Guid inviteId, NetUserId user)
    {
        var invite = await _db.GetCompanyInvitation(inviteId);
        if (invite == null || invite.Status != (int)CompanyInvitationStatus.Pending)
            return false;

        if (invite.TargetUserId != user.UserId && invite.FromUserId != user.UserId)
            return false;

        // Drop resolved invites instead of keeping forever-growing history rows.
        await _db.DeleteCompanyInvitation(inviteId);
        return true;
    }

    public async Task<List<CompanyInvitationData>> GetInvitationDatas(NetUserId user, ProtoId<CompanyPrototype>? company)
    {
        var result = new List<CompanyInvitationData>();
        var incoming = await _db.GetPendingInvitationsForPlayer(user.UserId);
        foreach (var inv in incoming)
        {
            result.Add(ToInvitationData(inv, user, incoming: true));
        }

        if (company != null)
        {
            var outgoing = await _db.GetPendingInvitationsForCompany(company.Value);
            foreach (var inv in outgoing)
            {
                if (result.Any(r => r.InviteId == inv.Id))
                    continue;
                result.Add(ToInvitationData(inv, user, incoming: inv.TargetUserId == user.UserId));
            }
        }

        return result;
    }

    private CompanyInvitationData ToInvitationData(CompanyInvitation inv, NetUserId viewer, bool incoming)
    {
        var companyName = _proto.TryIndex<CompanyPrototype>(inv.CompanyId, out var c) ? c.Name : inv.CompanyId;
        return new CompanyInvitationData
        {
            InviteId = inv.Id,
            Company = inv.CompanyId,
            CompanyName = companyName,
            FromUserId = inv.FromUserId,
            FromUserName = inv.FromPlayer?.LastSeenUserName ?? inv.FromUserId.ToString(),
            TargetUserId = inv.TargetUserId,
            TargetUserName = inv.TargetPlayer?.LastSeenUserName ?? inv.TargetUserId.ToString(),
            CreatedAt = TimeSpan.Zero,
            Incoming = incoming,
        };
    }

    public async Task AddLog(ProtoId<CompanyPrototype> company, CompanyLogType type, string message, NetUserId? actor)
    {
        await _db.AddCompanyLog(new CompanyLog
        {
            CompanyId = company,
            Timestamp = DateTime.UtcNow,
            LogType = (int)type,
            Message = message,
            ActorUserId = actor?.UserId,
        });
    }

    public async Task<List<CompanyLogData>> GetLogDatas(ProtoId<CompanyPrototype> company, int limit = 100)
    {
        var logs = await _db.GetCompanyLogs(company, limit);
        return logs.Select(l => new CompanyLogData
        {
            Id = l.Id,
            LogType = (CompanyLogType)l.LogType,
            Message = l.Message,
            Timestamp = l.Timestamp.TimeOfDay,
            ActorName = null,
        }).ToList();
    }

    public async Task<bool> KickMember(ProtoId<CompanyPrototype> company, NetUserId target, NetUserId actor, int? characterSlot = null)
    {
        var targetMember = characterSlot != null
            ? GetCompanyMember(company, target, characterSlot.Value)
            : GetCompanyMember(company, target);
        if (targetMember is not { } member)
            return false;

        if (member.Owner)
            return false;

        var name = string.IsNullOrWhiteSpace(member.CharacterName)
            ? member.LastSeenUserName
            : member.CharacterName;
        await RemoveMember(target, company, member.CharacterSlot);
        await AddLog(company, CompanyLogType.Kicked, $"Kicked {name}", actor);
        return true;
    }
}
