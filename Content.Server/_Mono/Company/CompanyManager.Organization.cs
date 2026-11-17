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

public sealed partial class CompanyManager
{
    private readonly Dictionary<string, List<CompanyRole>> _roles = new();
    private readonly Dictionary<string, int> _bankBalances = new();
    private readonly Dictionary<string, string> _bulletins = new();
    private readonly Dictionary<string, List<CompanyRelation>> _relations = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _bankLocks = new();
    private readonly Dictionary<(string Company, Guid Actor), TimeSpan> _inviteCooldowns = new();
    private readonly Dictionary<(string Company, Guid Actor), TimeSpan> _relationCooldowns = new();

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
            _bankBalances[proto.ID] = await _db.GetCompanyBankBalance(proto.ID);
            _bulletins[proto.ID] = await _db.GetCompanyBulletin(proto.ID);
            _relations[proto.ID] = await _db.GetCompanyRelations(proto.ID);
        }

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
        if (role == null || role.IsDefault)
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

    public async Task<bool> TryWithdrawBank(ProtoId<CompanyPrototype> company, int amount, NetUserId? actor = null)
    {
        if (amount <= 0 || amount > CompanyOrgLimits.MaxBankTransaction)
            return false;

        var gate = GetBankLock(company);
        await gate.WaitAsync();
        try
        {
            var balance = GetBankBalance(company);
            if (balance < amount)
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
        if (!_relations.TryGetValue(company, out var relations))
            return new();

        var result = new List<CompanyRelationData>();
        foreach (var rel in relations)
        {
            var otherId = rel.CompanyAId == company.Id ? rel.CompanyBId : rel.CompanyAId;
            if (!_proto.TryIndex<CompanyPrototype>(otherId, out var other))
                continue;

            result.Add(new CompanyRelationData
            {
                OtherCompany = otherId,
                OtherCompanyName = other.Name,
                Kind = (CompanyRelationKind)rel.RelationType,
                IconId = other.Icon?.Id,
            });
        }

        return result;
    }

    public async Task<bool> SetRelation(ProtoId<CompanyPrototype> company, ProtoId<CompanyPrototype> other, CompanyRelationKind kind, NetUserId? actor = null)
    {
        if (!TryValidateRelationTarget(company, other))
            return false;

        if (actor != null && !CheckCooldown(_relationCooldowns, company, actor.Value.UserId, CompanyOrgLimits.RelationCooldownSeconds))
            return false;

        if (kind == CompanyRelationKind.Neutral)
            return await ClearRelationInternal(company, other, actor, skipCooldown: true);

        await _db.SetCompanyRelation(company, other, (int)kind);
        _relations[company] = await _db.GetCompanyRelations(company);
        _relations[other.Id] = await _db.GetCompanyRelations(other);

        var logType = kind == CompanyRelationKind.War ? CompanyLogType.WarDeclared : CompanyLogType.AllianceDeclared;
        var otherName = _proto.TryIndex(other, out var proto) ? proto.Name : other.Id;
        await AddLog(company, logType, $"{kind} with {otherName}", actor);
        return true;
    }

    public async Task<bool> ClearRelation(ProtoId<CompanyPrototype> company, ProtoId<CompanyPrototype> other, NetUserId? actor = null)
    {
        if (!TryValidateRelationTarget(company, other))
            return false;

        if (actor != null && !CheckCooldown(_relationCooldowns, company, actor.Value.UserId, CompanyOrgLimits.RelationCooldownSeconds))
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
        return true;
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

        if (GetCompanyMembers(company).Count >= CompanyOrgLimits.MaxMembersPerCompany)
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
