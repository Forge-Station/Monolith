using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using Content.Server._Mono.Company;
using Content.Server._NF.Bank;
using Content.Server.Administration;
using Content.Server.CartridgeLoader;
using Content.Shared._Forge.Company;
using Content.Shared._Mono.Company;
using Content.Shared._NF.Bank.Components;
using Content.Shared.CartridgeLoader;
using Content.Shared.IdentityManagement;
using Content.Shared.PDA;
using Content.Shared.UserInterface;
using Robust.Server.Player;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._Forge.Company;

public sealed class CompanyManagementCartridgeSystem : EntitySystem
{
    [Dependency] private CartridgeLoaderSystem _cartridgeLoader = default!;
    [Dependency] private CompanyManager _company = default!;
    [Dependency] private BankSystem _bank = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IPlayerLocator _locator = default!;
    [Dependency] private CompanySystem _companySystem = default!;
    [Dependency] private IPlayerManager _player = default!;

    /// <summary>
    /// Serializes personal↔company bank transfers per player to prevent double-spend races.
    /// </summary>
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _actorBankLocks = new();

    private SemaphoreSlim GetActorBankLock(Guid userId) =>
        _actorBankLocks.GetOrAdd(userId, static _ => new SemaphoreSlim(1, 1));

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<CompanyManagementCartridgeComponent, CartridgeMessageEvent>(OnUiMessage);
        SubscribeLocalEvent<CompanyManagementCartridgeComponent, CartridgeUiReadyEvent>(OnUiReady);
    }

    private void OnUiReady(EntityUid uid, CompanyManagementCartridgeComponent component, CartridgeUiReadyEvent args)
    {
        if (!TryGetLoaderActor(args.Loader, out var actor))
            return;

        if (TryGetActorSession(actor, out var session))
            _companySystem.TryApplyMembershipToSession(session);

        UpdateUi(uid, args.Loader, actor);
    }

    private async void OnUiMessage(EntityUid uid, CompanyManagementCartridgeComponent component, CartridgeMessageEvent args)
    {
        if (args is not CompanyManagementUiMessageEvent msg)
            return;

        if (!TryGetActorSession(args.Actor, out var session))
            return;

        var company = ResolveActorCompany(args.Actor);

        switch (msg.Action)
        {
            case CompanyManagementUiAction.Refresh:
                break;

            case CompanyManagementUiAction.SetBulletin:
                if (company != null && msg.Text != null &&
                    _company.HasPermission(session, company.Value, CompanyPermission.EditBulletin))
                {
                    await _company.SetBulletin(company.Value, msg.Text, session.UserId);
                }
                break;

            case CompanyManagementUiAction.BankDeposit:
                if (company != null &&
                    _company.HasPermission(session, company.Value, CompanyPermission.BankDeposit) &&
                    msg.Amount > 0 &&
                    msg.Amount <= CompanyOrgLimits.MaxBankTransaction)
                {
                    var bankLock = GetActorBankLock(session.UserId.UserId);
                    await bankLock.WaitAsync();
                    try
                    {
                        if (_bank.TryBankWithdraw(args.Actor, msg.Amount))
                        {
                            if (!await _company.TryDepositBank(company.Value, msg.Amount, session.UserId))
                                _bank.TryBankDeposit(args.Actor, msg.Amount, tax: false);
                        }
                    }
                    finally
                    {
                        bankLock.Release();
                    }
                }
                break;

            case CompanyManagementUiAction.BankWithdraw:
                if (company != null &&
                    _company.HasPermission(session, company.Value, CompanyPermission.BankWithdraw) &&
                    msg.Amount > 0 &&
                    msg.Amount <= CompanyOrgLimits.MaxBankTransaction)
                {
                    var bankLock = GetActorBankLock(session.UserId.UserId);
                    await bankLock.WaitAsync();
                    try
                    {
                        if (await _company.TryWithdrawBank(company.Value, msg.Amount, session.UserId))
                        {
                            if (!_bank.TryBankDeposit(args.Actor, msg.Amount, tax: false))
                                await _company.TryDepositBank(company.Value, msg.Amount, session.UserId);
                        }
                    }
                    finally
                    {
                        bankLock.Release();
                    }
                }
                break;

            case CompanyManagementUiAction.SetRelation:
                if (company != null && msg.CompanyId != null &&
                    ((msg.RelationKind == CompanyRelationKind.War &&
                      _company.HasPermission(session, company.Value, CompanyPermission.DeclareWar)) ||
                     (msg.RelationKind == CompanyRelationKind.Alliance &&
                      _company.HasPermission(session, company.Value, CompanyPermission.DeclareAlliance))))
                {
                    await _company.SetRelation(company.Value, msg.CompanyId, msg.RelationKind, session.UserId);
                }
                break;

            case CompanyManagementUiAction.ClearRelation:
                if (company != null && msg.CompanyId != null &&
                    (_company.HasPermission(session, company.Value, CompanyPermission.DeclareWar) ||
                     _company.HasPermission(session, company.Value, CompanyPermission.DeclareAlliance)))
                {
                    await _company.ClearRelation(company.Value, msg.CompanyId, session.UserId);
                }
                break;

            case CompanyManagementUiAction.InvitePlayer:
                if (company != null && msg.Text != null &&
                    _company.HasPermission(session, company.Value, CompanyPermission.Invite))
                {
                    var lookup = await _locator.LookupIdByNameAsync(msg.Text);
                    if (lookup != null)
                        await _company.InvitePlayer(company.Value, lookup.UserId, session.UserId);
                }
                break;

            case CompanyManagementUiAction.AcceptInvite:
                if (await _company.AcceptInvite(msg.GuidValue, session.UserId) &&
                    session.AttachedEntity is { } joinedMob)
                {
                    var joinedCompany = _company.GetMemberForCharacter(session.UserId,
                        _company.TryGetCharacterContext(session.UserId, out var slot, out _) ? slot : 0);
                    if (joinedCompany != null)
                        _companySystem.ApplyCompanyToMob(joinedMob, joinedCompany.Value.Company, session);
                }
                break;

            case CompanyManagementUiAction.DeclineInvite:
            case CompanyManagementUiAction.CancelInvite:
                await _company.DeclineInvite(msg.GuidValue, session.UserId);
                break;

            case CompanyManagementUiAction.KickMember:
                if (company != null &&
                    _company.HasPermission(session, company.Value, CompanyPermission.Kick))
                {
                    await _company.KickMember(company.Value, new NetUserId(msg.GuidValue), session.UserId, msg.CharacterSlot);
                }
                break;

            case CompanyManagementUiAction.CreateRole:
                if (company != null && msg.Text != null &&
                    _company.HasPermission(session, company.Value, CompanyPermission.ManageRoles))
                {
                    var grantable = _company.IsOwnerSession(session, company.Value)
                        ? CompanyPermission.All
                        : _company.GetPermissions(session, company.Value);
                    var perms = msg.Permissions & grantable;
                    var tier = _company.ClampGrantableAccessTier(session, company.Value, msg.AccessTier);
                    await _company.CreateRole(company.Value, msg.Text, perms, tier);
                }
                break;

            case CompanyManagementUiAction.UpdateRole:
                if (company != null && msg.Text != null &&
                    _company.HasPermission(session, company.Value, CompanyPermission.ManageRoles))
                {
                    var grantable = _company.IsOwnerSession(session, company.Value)
                        ? CompanyPermission.All
                        : _company.GetPermissions(session, company.Value);
                    var perms = msg.Permissions & grantable;
                    var tier = _company.ClampGrantableAccessTier(session, company.Value, msg.AccessTier);
                    await _company.UpdateRole(company.Value, msg.GuidValue, msg.Text, perms, tier);
                }
                break;

            case CompanyManagementUiAction.DeleteRole:
                if (company != null &&
                    _company.HasPermission(session, company.Value, CompanyPermission.ManageRoles))
                {
                    await _company.DeleteRole(company.Value, msg.GuidValue);
                }
                break;

            case CompanyManagementUiAction.AssignRole:
                if (company != null &&
                    (_company.HasPermission(session, company.Value, CompanyPermission.AssignAccess) ||
                     _company.HasPermission(session, company.Value, CompanyPermission.ManageRoles)) &&
                    msg.GuidValue2 is { } roleId &&
                    _company.CanAssignRole(session, company.Value, roleId))
                {
                    var target = new NetUserId(msg.GuidValue);
                    if (await _company.AssignRole(company.Value, target, roleId, session.UserId, msg.CharacterSlot))
                    {
                        if (_player.TryGetSessionById(target, out var targetSession) &&
                            targetSession.AttachedEntity is { } mob)
                        {
                            _companySystem.ApplyCompanyAccessTags(mob, company.Value, targetSession);
                        }
                    }
                }
                break;
        }

        UpdateUi(uid, GetEntity(args.LoaderUid), args.Actor);
    }

    private async void UpdateUi(EntityUid cartridge, EntityUid loader, EntityUid actor)
    {
        if (!TryGetActorSession(actor, out var session))
            return;

        var company = ResolveActorCompany(actor);
        var state = new CompanyManagementUiState();

        // Always show incoming invites
        state.Invitations = await _company.GetInvitationDatas(session.UserId, company);

        if (company == null || !_proto.TryIndex(company.Value, out var companyProto))
        {
            state.HasCompany = false;
            _cartridgeLoader.UpdateCartridgeUiState(loader, state);
            return;
        }

        // For non-whitelisted companies, auto-treat profile affiliation as enough for read-only if not member
        var isMember = false;
        var isOwner = false;
        if (_company.TryGetCharacterContext(session.UserId, out var charSlot, out _))
        {
            var charMember = _company.GetCompanyMember(company.Value, session.UserId, charSlot);
            isMember = charMember != null;
            isOwner = charMember is { Owner: true };
        }
        else
        {
            // No selected character context — deny membership/owner for UI.
            isMember = false;
            isOwner = false;
        }

        // Whitelisted companies require membership for the selected character.
        if (!isMember && companyProto.Whitelisted)
        {
            state.HasCompany = false;
            _cartridgeLoader.UpdateCartridgeUiState(loader, state);
            return;
        }

        state.HasCompany = true;
        state.CompanyId = company.Value;
        state.CompanyName = companyProto.Name;
        state.IsOwner = isOwner;
        state.LeaderName = _company.GetLeaderCharacterName(company.Value)
            ?? Loc.GetString("company-pda-leader-none");
        state.Permissions = isMember
            ? _company.GetPermissions(session, company.Value)
            : CompanyPermission.None;
        state.AccessTier = isMember
            ? _company.GetAccessTier(session.UserId, company.Value)
            : CompanyAccessTier.None;
        state.Bulletin = _company.GetBulletin(company.Value);
        state.BankBalance = _company.GetBankBalance(company.Value);
        state.PersonalBalance = TryComp<BankAccountComponent>(actor, out var bank) ? bank.Balance : 0;
        state.MemberCount = _company.GetCompanyMembers(company.Value).Count;
        state.Relations = _company.GetRelationDatas(company.Value);
        state.Roles = _company.GetRoleDatas(company.Value);
        state.Logs = (state.Permissions & CompanyPermission.ViewLogs) != 0 || isOwner
            ? await _company.GetLogDatas(company.Value)
            : new();
        state.Roster = BuildRoster(company.Value);
        state.InviteableCompanies = _proto.EnumeratePrototypes<CompanyPrototype>()
            .Where(c => c.ID != "None" && c.ID != company.Value.Id && !c.Hidden)
            .OrderBy(c => c.Name)
            .Select(c => new CompanyPickerEntry
            {
                Id = c.ID,
                Name = c.Name,
                IconId = c.Icon?.Id,
            })
            .ToList();

        _cartridgeLoader.UpdateCartridgeUiState(loader, state);
    }

    private List<CompanyMemberData> BuildRoster(ProtoId<CompanyPrototype> company)
    {
        var roster = new List<CompanyMemberData>();
        var onlineByUser = new Dictionary<Guid, (string CharName, bool Online)>();

        var query = EntityQueryEnumerator<CompanyComponent, ActorComponent, MetaDataComponent>();
        while (query.MoveNext(out var uid, out var companyComp, out var actor, out var meta))
        {
            if (companyComp.CompanyName != company)
                continue;

            onlineByUser[actor.PlayerSession.UserId.UserId] = (
                Identity.Name(uid, EntityManager),
                true);
        }

        foreach (var member in _company.GetCompanyMembers(company))
        {
            var role = _company.ResolveRole(company, member.RoleId);
            onlineByUser.TryGetValue(member.PlayerUserId, out var online);
            roster.Add(new CompanyMemberData
            {
                PlayerUserId = member.PlayerUserId,
                CharacterSlot = member.CharacterSlot,
                PlayerName = member.LastSeenUserName,
                Owner = member.Owner,
                RoleId = member.RoleId ?? role?.Id,
                RoleName = role?.Name ?? "Member",
                AccessTier = member.Owner
                    ? CompanyAccessTier.High
                    : (CompanyAccessTier)(role?.AccessTier ?? (int)CompanyAccessTier.Low),
                Online = online.Online,
                CharacterName = string.IsNullOrWhiteSpace(online.CharName)
                    ? member.CharacterName
                    : online.CharName,
            });
        }

        // Also include online affiliated players who may not be in whitelist (open companies)
        foreach (var (userId, info) in onlineByUser)
        {
            if (roster.Any(r => r.PlayerUserId == userId))
                continue;

            roster.Add(new CompanyMemberData
            {
                PlayerUserId = userId,
                PlayerName = info.CharName,
                Owner = false,
                RoleName = "Affiliate",
                AccessTier = CompanyAccessTier.Low,
                Online = true,
                CharacterName = info.CharName,
            });
        }

        return roster;
    }

    private ProtoId<CompanyPrototype>? ResolveActorCompany(EntityUid actor)
    {
        // Prefer character-bound membership so PDA works even before the doll was synced.
        if (TryComp<ActorComponent>(actor, out var actorComp) &&
            _company.TryGetCharacterContext(actorComp.PlayerSession.UserId, out var slot, out _))
        {
            var membership = _company.GetMemberForCharacter(actorComp.PlayerSession.UserId, slot);
            if (membership != null && membership.Value.Company != "None")
                return membership.Value.Company;
        }

        if (!TryComp<CompanyComponent>(actor, out var company) || company.CompanyName == "None")
            return null;
        return company.CompanyName;
    }

    private bool TryGetActorSession(EntityUid actor, out ICommonSession session)
    {
        session = default!;
        if (!TryComp<ActorComponent>(actor, out var actorComp))
            return false;
        session = actorComp.PlayerSession;
        return true;
    }

    private bool TryGetLoaderActor(EntityUid loader, out EntityUid actor)
    {
        actor = default;
        if (TryComp<PdaComponent>(loader, out var pda) && pda.PdaOwner is { } owner && Exists(owner))
        {
            actor = owner;
            return true;
        }

        if (TryComp<ActivatableUIComponent>(loader, out var ui) && ui.CurrentSingleUser is { } user)
        {
            actor = user;
            return true;
        }

        return false;
    }
}
