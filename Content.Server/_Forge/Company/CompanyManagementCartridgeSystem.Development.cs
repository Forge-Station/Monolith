using System.Linq;
using System.Threading.Tasks;
using Content.Server._NF.Shipyard.Components;
using Content.Server.Cargo.Components;
using Content.Server.Popups;
using Content.Server.Stack;
using Content.Shared._Forge.Company;
using Content.Shared._Mono.Company;
using Content.Shared._NF.Bank.Components;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Mobs.Components;
using Content.Shared.Stacks;
using Content.Shared.Storage;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.GameObjects;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._Forge.Company;

public sealed partial class CompanyManagementCartridgeSystem
{
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private StackSystem _stack = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedHandsSystem _hands = default!;

    /// <summary>Same reach cargo uses from a console to a buy pallet.</summary>
    private const float CargoBuyPadRange = 8f;

    private void Deny(EntityUid actor, string message, params (string, object)[] args)
    {
        _popup.PopupEntity(Loc.GetString(message, args), actor, actor);
    }

    private async Task<bool> TrySpend(EntityUid actor, ProtoId<CompanyPrototype> company, int amount, NetUserId user)
    {
        if (await _company.TryWithdrawBank(company, amount, user))
            return true;

        DenySpendFailed(actor, company, amount);
        return false;
    }

    private void DenySpendFailed(EntityUid actor, ProtoId<CompanyPrototype> company, int amount)
    {
        if (_company.IsAtWar(company))
        {
            var spendable = _company.GetSpendableBalance(company);
            Deny(actor, spendable <= 0 ? "company-war-frozen-all" : "company-war-frozen",
                ("reserve", CompanyOrgLimits.WarTreasuryReserve),
                ("spendable", spendable));
            return;
        }

        if (amount > 0)
            Deny(actor, "company-spend-no-funds", ("cost", CompanyNumbers.Format(amount)));
        else
            Deny(actor, "company-spend-no-funds-generic");
    }

    private async Task BuyProject(EntityUid actor, ICommonSession session, ProtoId<CompanyPrototype>? company, string? id)
    {
        if (company == null || id == null ||
            !_company.HasPermission(session, company.Value, CompanyPermission.FundProjects))
            return;

        if (_company.HasProject(company.Value, id))
        {
            Deny(actor, "company-spend-owned");
            return;
        }

        if (!await _company.TryUnlockProject(company.Value, id, session.UserId))
            DenySpendFailed(actor, company.Value, 0);
        else
            _popup.PopupEntity(Loc.GetString("company-spend-bought"), actor, actor);
    }

    private async Task BuySupply(EntityUid actor, ICommonSession session, ProtoId<CompanyPrototype>? company, string? id)
    {
        if (company == null || id == null ||
            !_company.HasPermission(session, company.Value, CompanyPermission.OrderSupplies) ||
            !_proto.TryIndex<CompanySupplyPrototype>(id, out var supply))
            return;

        if (!TryFindCargoBuyPad(actor, out var coords))
        {
            Deny(actor, "company-spend-no-cargo");
            return;
        }

        var cost = _company.Priced(company.Value, supply.Cost, CompanyProjectEffect.SupplyDiscount);
        if (!await TrySpend(actor, company.Value, cost, session.UserId))
            return;

        var crate = Spawn(supply.Crate, coords);
        _transform.Unanchor(crate, Transform(crate));
        await _company.AddLog(company.Value, CompanyLogType.TreasurySpent, $"Ordered {id} for {cost}", session.UserId);
        _popup.PopupEntity(Loc.GetString("company-spend-crate"), actor, actor);
    }

    private async Task BuyShip(EntityUid actor, ICommonSession session, ProtoId<CompanyPrototype>? company, string? id)
    {
        if (company == null || id == null ||
            !_company.HasPermission(session, company.Value, CompanyPermission.OrderShips) ||
            !_proto.TryIndex<CompanyShipOfferPrototype>(id, out var offer) ||
            !_proto.TryIndex<VesselPrototype>(offer.Vessel, out var vessel) ||
            vessel.Price <= 0)
            return;

        if (!TryFindCargoBuyPad(actor, out var coords))
        {
            Deny(actor, "company-spend-no-cargo");
            return;
        }

        var cost = _company.Priced(company.Value, vessel.Price, CompanyProjectEffect.ShipDiscount);
        if (!await TrySpend(actor, company.Value, cost, session.UserId))
            return;

        var voucher = Spawn("CompanyShipVoucher", coords);
        _transform.Unanchor(voucher, Transform(voucher));
        var shipVoucher = EnsureComp<ShipyardVoucherComponent>(voucher);
        shipVoucher.Vessels.Add(offer.Vessel);
        shipVoucher.CompanyName = company.Value.Id;
        var funded = EnsureComp<CompanyTreasuryShipComponent>(voucher);
        funded.CompanyId = company.Value.Id;
        funded.RefundPercent = CompanyOrgLimits.ShipSaleRefundPercent / 100f;

        await _company.AddLog(company.Value, CompanyLogType.TreasurySpent, $"Ship voucher {offer.Vessel} for {cost}", session.UserId);
        _popup.PopupEntity(Loc.GetString("company-spend-ship-bought", ("ship", vessel.Name)), actor, actor);
    }

    private async Task AcceptContract(EntityUid actor, ICommonSession session, ProtoId<CompanyPrototype>? company, string? id, bool funded)
    {
        if (company == null || id == null ||
            _company.GetPermissions(session, company.Value) == CompanyPermission.None ||
            !_proto.TryIndex<CompanyContractPrototype>(id, out var contract))
            return;

        if (funded && !_company.HasPermission(session, company.Value, CompanyPermission.PostContracts))
            return;

        if (_company.TryGetContract(company.Value, out _, out _))
        {
            Deny(actor, "company-spend-contract-busy");
            return;
        }

        if (!await _company.TrySetContract(company.Value, id, funded, funded ? contract.Reward : 0, session.UserId))
            DenySpendFailed(actor, company.Value, funded ? contract.Reward : 0);
        else
            _popup.PopupEntity(Loc.GetString(funded ? "company-spend-contract-reserved" : "company-spend-contract-accepted"), actor, actor);
    }

    private async Task CancelContract(EntityUid actor, ICommonSession session, ProtoId<CompanyPrototype>? company)
    {
        if (company == null || _company.GetPermissions(session, company.Value) == CompanyPermission.None)
            return;

        if (!await _company.TryClearContract(company.Value, refund: true, session.UserId))
            return;

        _popup.PopupEntity(Loc.GetString("company-spend-contract-cancelled"), actor, actor);
    }

    public async Task<bool> TurnInContract(EntityUid actor, ICommonSession session, ProtoId<CompanyPrototype>? company)
    {
        if (company == null ||
            _company.GetPermissions(session, company.Value) == CompanyPermission.None ||
            !_company.TryGetContract(company.Value, out var contractId, out var funded) ||
            !_proto.TryIndex<CompanyContractPrototype>(contractId, out var contract))
            return false;

        if (!TryComp<BankAccountComponent>(actor, out _))
        {
            Deny(actor, "company-spend-no-bank");
            return false;
        }

        var have = CountContractItems(actor, contract);
        if (have < contract.Count)
        {
            Deny(actor, "company-spend-contract-short", ("have", CompanyNumbers.Format(have)), ("need", CompanyNumbers.Format(contract.Count)));
            return false;
        }

        ConsumeContractItems(actor, contract, contract.Count);
        var memberPercent = funded ? CompanyOrgLimits.FundedMemberPercent : CompanyOrgLimits.MarketMemberPercent;
        var memberPay = contract.Reward * memberPercent / 100;
        var treasuryPay = contract.Reward - memberPay;
        _bank.TryBankDeposit(actor, memberPay, tax: false);
        if (treasuryPay > 0)
            await _company.TryDepositBank(company.Value, treasuryPay, session.UserId);

        await _company.TryClearContract(company.Value, refund: false, session.UserId);
        await _company.AddLog(company.Value, CompanyLogType.ContractCompleted, $"Completed {contractId}: member {memberPay}, treasury {treasuryPay}", session.UserId);
        _popup.PopupEntity(Loc.GetString("company-spend-contract-done",
            ("pay", CompanyNumbers.Format(memberPay)),
            ("treasury", CompanyNumbers.Format(treasuryPay))), actor, actor);
        return true;
    }

    public async Task TurnInAtBoard(EntityUid actor, EntityUid board)
    {
        if (!TryComp<ActorComponent>(actor, out var actorComp))
            return;

        var session = actorComp.PlayerSession;
        if (!_company.TryGetCharacterContext(session.UserId, out var slot, out _) ||
            _company.GetMemberForCharacter(session.UserId, slot) is not { } member ||
            member.Company == "None")
        {
            Deny(actor, "company-job-board-none");
            return;
        }

        if (member.Company == "None")
        {
            Deny(actor, "company-job-board-none");
            return;
        }

        var company = member.Company;
        var hadContract = _company.TryGetContract(company, out _, out _);
        var hadJob = _company.VisibleJobs(company.Id).Any(job => job.TakenByUs);
        var turnedContract = await TurnInContract(actor, session, company);
        var turnedJob = await CompletePostedJob(actor, session, company, board);
        if (!hadContract && !hadJob && !turnedContract && !turnedJob)
            Deny(actor, "company-job-board-nothing");
    }

    private async Task PostCreditJob(EntityUid actor, ICommonSession session, ProtoId<CompanyPrototype>? company, string? text, int amount)
    {
        if (!CanPostJob(actor, session, company))
            return;

        var error = await _company.TryPostCreditJob(company!.Value, text, amount, session.UserId);
        if (error != null)
            Deny(actor, error, ("cost", CompanyNumbers.Format(amount)));
        else
            _popup.PopupEntity(Loc.GetString("company-job-posted"), actor, actor);
    }

    private async Task PostItemJob(EntityUid actor, ICommonSession session, ProtoId<CompanyPrototype>? company, string? text)
    {
        if (!CanPostJob(actor, session, company))
            return;

        if (!_hands.TryGetActiveItem(actor, out var held) || held is not { } item ||
            HasComp<MobStateComponent>(item) ||
            HasComp<StorageComponent>(item) ||
            HasComp<CompanyVaultComponent>(item) ||
            Prototype(item) is not { } proto)
        {
            Deny(actor, "company-job-item");
            return;
        }

        var count = TryComp<StackComponent>(item, out var stack) ? stack.Count : 1;
        var take = Math.Min(count, CompanyOrgLimits.MaxJobItemCount);
        var error = await _company.TryPostItemJob(company!.Value, text, proto.ID, take, session.UserId);
        if (error != null)
        {
            Deny(actor, error);
            return;
        }

        if (stack != null)
            _stack.SetCount(item, count - take, stack);
        else
            QueueDel(item);

        _popup.PopupEntity(Loc.GetString("company-job-posted"), actor, actor);
    }

    private async Task AcceptJob(EntityUid actor, ICommonSession session, ProtoId<CompanyPrototype>? company, string? jobId)
    {
        if (company == null || _company.GetPermissions(session, company.Value) == CompanyPermission.None)
            return;

        var error = await _company.TryAcceptJob(company.Value, jobId, session.UserId);
        if (error != null)
            Deny(actor, error);
        else
            _popup.PopupEntity(Loc.GetString("company-job-accepted"), actor, actor);
    }

    private async Task AbandonJob(EntityUid actor, ICommonSession session, ProtoId<CompanyPrototype>? company)
    {
        if (company == null || _company.GetPermissions(session, company.Value) == CompanyPermission.None)
            return;

        var error = await _company.TryAbandonJob(company.Value, session.UserId);
        if (error != null)
            Deny(actor, error);
        else
            _popup.PopupEntity(Loc.GetString("company-job-abandoned"), actor, actor);
    }

    private async Task CancelJob(EntityUid actor, ICommonSession session, ProtoId<CompanyPrototype>? company, string? jobId, EntityUid spawnAt)
    {
        if (!CanPostJob(actor, session, company))
            return;

        var (error, refund) = await _company.TryCancelJob(company!.Value, jobId, session.UserId);
        if (error != null)
        {
            Deny(actor, error);
            return;
        }

        if (refund != null)
            SpawnReward(refund.RewardProto, refund.RewardAmount, spawnAt);

        _popup.PopupEntity(Loc.GetString("company-job-cancelled"), actor, actor);
    }

    private async Task<bool> CompletePostedJob(EntityUid actor, ICommonSession session, ProtoId<CompanyPrototype> company, EntityUid board)
    {
        var job = await _company.TryFinishJob(company, session.UserId);
        if (job == null)
            return false;

        if (job.RewardKind == CompanyJobReward.Items)
        {
            SpawnReward(job.RewardProto, job.RewardAmount, board);
            _popup.PopupEntity(Loc.GetString("company-job-done-items"), actor, actor);
            return true;
        }

        var memberPay = job.RewardAmount * CompanyOrgLimits.MarketMemberPercent / 100;
        var treasuryPay = job.RewardAmount - memberPay;
        if (!TryComp<BankAccountComponent>(actor, out _))
        {
            memberPay = 0;
            treasuryPay = job.RewardAmount;
        }
        else if (memberPay > 0)
        {
            _bank.TryBankDeposit(actor, memberPay, tax: false);
        }

        if (treasuryPay > 0)
            await _company.TryDepositBank(company, treasuryPay, session.UserId);

        _popup.PopupEntity(Loc.GetString("company-job-done-credits",
            ("pay", CompanyNumbers.Format(memberPay)),
            ("treasury", CompanyNumbers.Format(treasuryPay))), actor, actor);
        return true;
    }

    private bool CanPostJob(EntityUid actor, ICommonSession session, ProtoId<CompanyPrototype>? company)
    {
        if (company == null ||
            !_company.HasPermission(session, company.Value, CompanyPermission.PostContracts))
        {
            if (company != null)
                Deny(actor, "company-spend-no-fund-permission");
            return false;
        }

        return true;
    }

    private void SpawnReward(string protoId, int count, EntityUid at)
    {
        if (string.IsNullOrWhiteSpace(protoId) || count < 1 || !Exists(at))
            return;

        var left = count;
        var coordinates = Transform(at).Coordinates;
        while (left > 0)
        {
            var spawned = Spawn(protoId, coordinates);
            if (!TryComp<StackComponent>(spawned, out var stack))
            {
                left--;
                continue;
            }

            var room = _stack.GetMaxCount(spawned);
            var give = Math.Min(left, Math.Max(1, room));
            _stack.SetCount(spawned, give, stack);
            left -= give;
        }
    }

    private void FillDevelopment(CompanyManagementUiState state, ProtoId<CompanyPrototype> company, bool isOwner)
    {
        state.WarCost = _company.ScaleDiplomacy(company, CompanyOrgLimits.WarCost);
        state.AllianceCost = _company.ScaleDiplomacy(company, CompanyOrgLimits.AllianceCost);
        state.BreakAllianceCost = _company.ScaleDiplomacy(company, CompanyOrgLimits.BreakAllianceCost);
        state.ResearchPoints = _company.GetResearchArchive(company);

        foreach (var tier in CompanyResearch.Tiers)
        {
            state.Spends.Add(new CompanySpendEntry
            {
                Id = tier.Name,
                Kind = CompanySpendKind.Research,
                Title = tier.Name,
                Purpose = tier.Purpose,
                Cost = (int)tier.Threshold,
                Arg1 = tier.Amount,
                Owned = state.ResearchPoints >= tier.Threshold,
            });
        }

        var perms = state.Permissions;
        bool Can(CompanyPermission flag) => isOwner || (perms & flag) != 0;

        foreach (var project in _proto.EnumeratePrototypes<CompanyProjectPrototype>().OrderBy(p => p.Cost))
        {
            var owned = _company.HasProject(company, project.ID);
            var next = project.Effect == CompanyProjectEffect.MemberSlots
                ? _company.MaxMembers(company) + (owned ? 0 : project.Amount)
                : project.Amount;
            state.Spends.Add(new CompanySpendEntry
            {
                Id = project.ID,
                Kind = CompanySpendKind.Project,
                Title = project.Name,
                Purpose = project.Purpose,
                Cost = project.Cost,
                Arg1 = project.Amount,
                Arg2 = next,
                Owned = owned,
                CanBuy = Can(CompanyPermission.FundProjects),
            });
        }

        foreach (var supply in _proto.EnumeratePrototypes<CompanySupplyPrototype>().OrderBy(s => s.Cost))
        {
            var cost = _company.Priced(company, supply.Cost, CompanyProjectEffect.SupplyDiscount);
            state.Spends.Add(new CompanySpendEntry
            {
                Id = supply.ID,
                Kind = CompanySpendKind.Supply,
                Title = supply.Name,
                Purpose = supply.Purpose,
                Cost = cost,
                Owned = false,
                Discounted = cost != supply.Cost,
                CanBuy = Can(CompanyPermission.OrderSupplies),
            });
        }

        foreach (var offer in _proto.EnumeratePrototypes<CompanyShipOfferPrototype>())
        {
            if (!_proto.TryIndex<VesselPrototype>(offer.Vessel, out var vessel) || vessel.Price <= 0)
                continue;

            var cost = _company.Priced(company, vessel.Price, CompanyProjectEffect.ShipDiscount);
            state.Spends.Add(new CompanySpendEntry
            {
                Id = offer.ID,
                Kind = CompanySpendKind.Ship,
                Title = vessel.Name,
                Purpose = "company-spend-ship-purpose",
                Cost = cost,
                Arg1 = CompanyOrgLimits.ShipSaleRefundPercent,
                Discounted = cost != vessel.Price,
                CanBuy = Can(CompanyPermission.OrderShips),
                Detail = vessel.Description,
            });
        }

        foreach (var contract in _proto.EnumeratePrototypes<CompanyContractPrototype>().OrderBy(c => c.Reward))
        {
            state.Spends.Add(new CompanySpendEntry
            {
                Id = contract.ID,
                Kind = CompanySpendKind.Contract,
                Title = contract.Name,
                Purpose = contract.Purpose,
                Cost = contract.Reward,
                Arg1 = contract.Reward * CompanyOrgLimits.MarketMemberPercent / 100,
                Arg2 = contract.Reward * CompanyOrgLimits.FundedMemberPercent / 100,
                CanBuy = true,
                CanFund = Can(CompanyPermission.PostContracts),
                Detail = string.IsNullOrEmpty(contract.StackType) ? contract.Item?.ToString() ?? string.Empty : contract.StackType,
            });
        }

        if (_company.TryGetContract(company, out var activeId, out var funded) &&
            _proto.TryIndex<CompanyContractPrototype>(activeId, out var active))
        {
            var percent = funded ? CompanyOrgLimits.FundedMemberPercent : CompanyOrgLimits.MarketMemberPercent;
            var memberPay = active.Reward * percent / 100;
            state.ActiveContractId = activeId;
            state.ActiveContractTitle = active.Name;
            state.ActiveContractItem = string.IsNullOrEmpty(active.StackType) ? active.Item?.ToString() ?? string.Empty : active.StackType;
            state.ActiveContractCount = active.Count;
            state.ActiveContractMemberPay = memberPay;
            state.ActiveContractTreasuryPay = active.Reward - memberPay;
            state.ActiveContractFunded = funded;
        }
    }

    private bool TryFindCargoBuyPad(EntityUid actor, out EntityCoordinates coordinates)
    {
        coordinates = default;
        if (!TryComp<TransformComponent>(actor, out var actorXform) || actorXform.GridUid is not { } grid)
            return false;

        var origin = _transform.GetWorldPosition(actorXform);
        var best = float.MaxValue;
        EntityCoordinates? found = null;

        var query = EntityQueryEnumerator<CargoPalletComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var pallet, out var xform))
        {
            if (xform.ParentUid != grid || !xform.Anchored)
                continue;
            if ((pallet.PalletType & BuySellType.Buy) == 0)
                continue;

            var distance = (_transform.GetWorldPosition(xform) - origin).Length();
            if (distance > CargoBuyPadRange || distance >= best)
                continue;

            var aabb = _lookup.GetAABBNoContainer(uid, xform.LocalPosition, xform.LocalRotation);
            if (_lookup.AnyLocalEntitiesIntersecting(grid, aabb, LookupFlags.Dynamic))
                continue;

            best = distance;
            found = new EntityCoordinates(grid, xform.LocalPosition);
        }

        if (found == null)
            return false;

        coordinates = found.Value;
        return true;
    }

    private int CountContractItems(EntityUid actor, CompanyContractPrototype contract)
    {
        var total = 0;
        foreach (var item in MatchingItems(actor, contract))
            total += ItemCount(item);
        return total;
    }

    private void ConsumeContractItems(EntityUid actor, CompanyContractPrototype contract, int need)
    {
        var left = need;
        foreach (var item in MatchingItems(actor, contract))
        {
            if (left <= 0)
                return;

            if (TryComp<StackComponent>(item, out var stack))
            {
                var take = Math.Min(stack.Count, left);
                _stack.SetCount(item, stack.Count - take);
                left -= take;
            }
            else
            {
                QueueDel(item);
                left--;
            }
        }
    }

    private List<EntityUid> MatchingItems(EntityUid actor, CompanyContractPrototype contract)
    {
        var list = new List<EntityUid>();
        foreach (var item in _inventory.GetHandOrInventoryEntities(actor))
        {
            if (!string.IsNullOrEmpty(contract.StackType))
            {
                if (TryComp<StackComponent>(item, out var stack) && stack.StackTypeId == contract.StackType)
                    list.Add(item);
            }
            else if (contract.Item is { } wanted && Prototype(item)?.ID == wanted.Id)
            {
                list.Add(item);
            }
        }

        return list;
    }

    private int ItemCount(EntityUid item)
    {
        return TryComp<StackComponent>(item, out var stack) ? stack.Count : 1;
    }
}
