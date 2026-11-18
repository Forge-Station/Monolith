using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.Chat.Systems;
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

public sealed partial class CompanyManagementCartridgeSystem : EntitySystem
{
    [Dependency] private CartridgeLoaderSystem _cartridgeLoader = default!;
    [Dependency] private CompanyManager _company = default!;
    [Dependency] private BankSystem _bank = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IPlayerLocator _locator = default!;
    [Dependency] private CompanySystem _companySystem = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private CompanyVaultSystem _vault = default!;

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
            case CompanyManagementUiAction.FoundOutpost:
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
                if (company != null && _company.IsAtWar(company.Value))
                {
                    Deny(args.Actor, "company-war-no-withdraw");
                    break;
                }

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
                    msg.RelationKind == CompanyRelationKind.War &&
                    _company.HasPermission(session, company.Value, CompanyPermission.DeclareWar))
                {
                    var before = _company.GetRelation(company.Value, msg.CompanyId);
                    if (before == CompanyRelationKind.War)
                        break;

                    var cost = _company.ScaleDiplomacy(company.Value, CompanyOrgLimits.WarCost);
                    if (!await TrySpend(args.Actor, company.Value, cost, session.UserId))
                        break;

                    var declared = await _company.SetRelation(company.Value, msg.CompanyId, CompanyRelationKind.War, session.UserId);
                    if (!declared.Ok)
                    {
                        await _company.TryDepositBank(company.Value, cost, session.UserId);
                        Deny(args.Actor, "company-spend-relation-failed");
                        break;
                    }

                    AnnounceRelation(company.Value, msg.CompanyId, "company-relation-announce-war", msg.Text);
                    if (declared.JoinedAllies.Count > 0)
                        AnnounceAllyWar(msg.CompanyId, company.Value, declared.JoinedAllies);
                }
                break;

            case CompanyManagementUiAction.SetAutoJoinAllyWars:
                if (company != null &&
                    (_company.HasPermission(session, company.Value, CompanyPermission.DeclareWar) ||
                     _company.HasPermission(session, company.Value, CompanyPermission.DeclareAlliance)))
                {
                    await _company.SetAutoJoinAllyWars(company.Value, msg.BoolValue);
                }
                break;

            case CompanyManagementUiAction.OfferAlliance:
                await HandleAlliance(args.Actor, session, company, msg, withdraw: false);
                break;

            case CompanyManagementUiAction.WithdrawAlliance:
                await HandleAlliance(args.Actor, session, company, msg, withdraw: true);
                break;

            case CompanyManagementUiAction.ClearRelation:
                if (company != null && msg.CompanyId != null &&
                    (_company.HasPermission(session, company.Value, CompanyPermission.DeclareWar) ||
                     _company.HasPermission(session, company.Value, CompanyPermission.DeclareAlliance)))
                {
                    var before = _company.GetRelation(company.Value, msg.CompanyId);
                    if (before != CompanyRelationKind.Alliance)
                        break;

                    var cost = _company.ScaleDiplomacy(company.Value, CompanyOrgLimits.BreakAllianceCost);
                    if (!await TrySpend(args.Actor, company.Value, cost, session.UserId))
                        break;

                    if (!await _company.ClearRelation(company.Value, msg.CompanyId, session.UserId))
                    {
                        await _company.TryDepositBank(company.Value, cost, session.UserId);
                        Deny(args.Actor, "company-spend-relation-failed");
                        break;
                    }

                    AnnounceRelation(company.Value, msg.CompanyId, "company-relation-announce-alliance-ended", msg.Text);
                }
                break;

            case CompanyManagementUiAction.OfferFreePeace:
                await HandlePeace(args.Actor, session, company, msg, offer: true);
                break;

            case CompanyManagementUiAction.SetPeaceRansom:
                await HandlePeace(args.Actor, session, company, msg, ransom: true);
                break;

            case CompanyManagementUiAction.AcceptPeaceRansom:
                await HandlePeace(args.Actor, session, company, msg, accept: true);
                break;

            case CompanyManagementUiAction.OpenVault:
                if (company != null)
                    await _vault.Open(args.Actor, session, company.Value.Id);
                break;

            case CompanyManagementUiAction.BuyProject:
                await BuyProject(args.Actor, session, company, msg.Text);
                break;

            case CompanyManagementUiAction.BuySupply:
                await BuySupply(args.Actor, session, company, msg.Text);
                break;

            case CompanyManagementUiAction.BuyShip:
                await BuyShip(args.Actor, session, company, msg.Text);
                break;

            case CompanyManagementUiAction.AcceptContract:
                await AcceptContract(args.Actor, session, company, msg.Text, funded: false);
                break;

            case CompanyManagementUiAction.FundContract:
                await AcceptContract(args.Actor, session, company, msg.Text, funded: true);
                break;

            case CompanyManagementUiAction.TurnInContract:
                Deny(args.Actor, "company-job-board-where");
                break;

            case CompanyManagementUiAction.PostCreditJob:
                await PostCreditJob(args.Actor, session, company, msg.Text, msg.Amount);
                break;

            case CompanyManagementUiAction.PostItemJob:
                await PostItemJob(args.Actor, session, company, msg.Text);
                break;

            case CompanyManagementUiAction.AcceptJob:
                await AcceptJob(args.Actor, session, company, msg.Text);
                break;

            case CompanyManagementUiAction.AbandonJob:
                await AbandonJob(args.Actor, session, company);
                break;

            case CompanyManagementUiAction.CancelJob:
                await CancelJob(args.Actor, session, company, msg.Text, args.Actor);
                break;

            case CompanyManagementUiAction.CancelContract:
                await CancelContract(args.Actor, session, company);
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
        state.VaultWidth = _vault.GetWidth(company.Value.Id);
        state.VaultHeight = _vault.GetHeight(company.Value.Id);
        state.AtWar = _company.IsAtWar(company.Value);
        state.SpendableBalance = _company.GetSpendableBalance(company.Value);
        state.WarReserve = CompanyOrgLimits.WarTreasuryReserve;
        state.PersonalBalance = TryComp<BankAccountComponent>(actor, out var bank) ? bank.Balance : 0;
        state.MemberCount = _company.GetCompanyMembers(company.Value).Count;
        state.Relations = _company.GetRelationDatas(company.Value);
        state.AutoJoinAllyWars = _company.AutoJoinsAllyWars(company.Value);
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
                Description = c.Description,
                Note = c.Note,
                Color = c.Color,
                IconId = c.Icon?.Id,
            })
            .ToList();

        FillDevelopment(state, company.Value, isOwner);
        state.Jobs = _company.VisibleJobs(company.Value.Id).ToList();

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

    private async Task HandlePeace(EntityUid actor, ICommonSession session, ProtoId<CompanyPrototype>? company, CompanyManagementUiMessageEvent msg, bool offer = false, bool ransom = false, bool accept = false)
    {
        if (company == null || msg.CompanyId == null ||
            !_company.HasPermission(session, company.Value, CompanyPermission.DeclareWar))
            return;

        if (ransom && (msg.Amount < 1 || msg.Amount > CompanyOrgLimits.MaxPeaceRansomPercent))
        {
            Deny(actor, "company-peace-percent", ("max", CompanyOrgLimits.MaxPeaceRansomPercent));
            return;
        }

        var outcome = offer
            ? await _company.TryOfferFreePeace(company.Value, msg.CompanyId, session.UserId)
            : ransom
                ? await _company.TrySetPeaceRansom(company.Value, msg.CompanyId, msg.Amount, session.UserId)
                : accept
                    ? await _company.TryAcceptPeaceRansom(company.Value, msg.CompanyId, session.UserId)
                    : default;

        if (!outcome.Ok)
        {
            Deny(actor, "company-peace-failed");
            return;
        }

        if (outcome.Concluded)
        {
            AnnouncePeace(company.Value, msg.CompanyId, msg.Text, outcome.Percent, outcome.Amount);
            _popup.PopupEntity(Loc.GetString("company-peace-done"), actor, actor);
            return;
        }

        if (ransom)
            _popup.PopupEntity(Loc.GetString("company-peace-ransom-set", ("percent", outcome.Percent)), actor, actor);
        else
            _popup.PopupEntity(Loc.GetString("company-peace-offered"), actor, actor);
    }

    private async Task HandleAlliance(EntityUid actor, ICommonSession session, ProtoId<CompanyPrototype>? company, CompanyManagementUiMessageEvent msg, bool withdraw)
    {
        if (company == null || msg.CompanyId == null ||
            !_company.HasPermission(session, company.Value, CompanyPermission.DeclareAlliance))
            return;

        if (withdraw)
        {
            if (await _company.TryWithdrawAllianceOffer(company.Value, msg.CompanyId, session.UserId))
                _popup.PopupEntity(Loc.GetString("company-alliance-withdrawn"), actor, actor);
            else
                Deny(actor, "company-alliance-failed");
            return;
        }

        var outcome = await _company.TryOfferAlliance(company.Value, msg.CompanyId, session.UserId);
        if (!outcome.Ok)
        {
            var otherName = _proto.TryIndex<CompanyPrototype>(msg.CompanyId, out var other)
                ? CompanyLabel(other.Name)
                : msg.CompanyId;
            var cost = CompanyNumbers.Format(_company.ScaleDiplomacy(company.Value, CompanyOrgLimits.AllianceCost));
            switch (outcome.Fail)
            {
                case CompanyAllianceFail.AtWar:
                    Deny(actor, "company-alliance-at-war");
                    break;
                case CompanyAllianceFail.NoFunds:
                    Deny(actor, "company-alliance-no-funds", ("cost", cost));
                    break;
                case CompanyAllianceFail.TheirNoFunds:
                    Deny(actor, "company-alliance-their-no-funds", ("other", otherName));
                    break;
                default:
                    Deny(actor, "company-alliance-failed");
                    break;
            }

            return;
        }

        if (outcome.Concluded)
        {
            AnnounceRelation(company.Value, msg.CompanyId, "company-relation-announce-alliance", msg.Text);
            _popup.PopupEntity(Loc.GetString("company-alliance-done"), actor, actor);
            return;
        }

        _popup.PopupEntity(Loc.GetString(outcome.AlreadyOffered
            ? "company-alliance-waiting-popup"
            : "company-alliance-offered"), actor, actor);
    }

    private void AnnouncePeace(ProtoId<CompanyPrototype> company, string otherId, string? statement, int percent, int amount)
    {
        if (!_proto.TryIndex<CompanyPrototype>(company, out var self) ||
            !_proto.TryIndex<CompanyPrototype>(otherId, out var other))
            return;

        var message = percent > 0
            ? Loc.GetString("company-relation-announce-peace-ransom",
                ("other", CompanyLabel(other.Name)),
                ("percent", percent),
                ("amount", amount))
            : Loc.GetString("company-relation-announce-peace-free", ("other", CompanyLabel(other.Name)));
        if (RelationStatement(statement) is { } words)
            message = Loc.GetString("company-relation-announce-statement", ("message", message), ("statement", words));

        _chat.DispatchGlobalAnnouncement(
            message,
            CompanyLabel(self.Name),
            playSound: true,
            colorOverride: Color.FromHex("#f5d76e"));
    }

    private void AnnounceAllyWar(string victimId, ProtoId<CompanyPrototype> aggressor, IReadOnlyList<string> allyIds)
    {
        if (!_proto.TryIndex<CompanyPrototype>(victimId, out var victim) ||
            !_proto.TryIndex(aggressor, out var aggressorProto))
            return;

        var names = new List<string>();
        foreach (var allyId in allyIds)
        {
            if (_proto.TryIndex<CompanyPrototype>(allyId, out var ally))
                names.Add(CompanyLabel(ally.Name));
        }

        if (names.Count == 0)
            return;

        _chat.DispatchGlobalAnnouncement(
            Loc.GetString("company-relation-announce-ally-war",
                ("aggressor", CompanyLabel(aggressorProto.Name)),
                ("allies", string.Join(", ", names))),
            CompanyLabel(victim.Name),
            playSound: true,
            colorOverride: Color.FromHex("#f5d76e"));
    }

    private void AnnounceRelation(ProtoId<CompanyPrototype> company, string otherId, string messageId, string? statement)
    {
        if (!_proto.TryIndex<CompanyPrototype>(company, out var self) ||
            !_proto.TryIndex<CompanyPrototype>(otherId, out var other))
            return;

        var message = Loc.GetString(messageId, ("other", CompanyLabel(other.Name)));
        if (RelationStatement(statement) is { } words)
            message = Loc.GetString("company-relation-announce-statement", ("message", message), ("statement", words));

        _chat.DispatchGlobalAnnouncement(
            message,
            CompanyLabel(self.Name),
            playSound: true,
            colorOverride: Color.FromHex("#f5d76e"));
    }

    private static string? RelationStatement(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var words = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (words.Length == 0)
            return null;
        if (words.Length > CompanyOrgLimits.MaxRelationStatementLength)
            words = words[..CompanyOrgLimits.MaxRelationStatementLength];
        return words;
    }

    private string CompanyLabel(string name)
    {
        return Loc.TryGetString(name, out var localized) && !string.IsNullOrWhiteSpace(localized)
            ? localized
            : name;
    }
}
