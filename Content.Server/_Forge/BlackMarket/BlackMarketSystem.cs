using Content.Server.Administration.Logs;
using Content.Server._NF.Bank;
using Content.Server._NF.CrateMachine;
using Content.Shared._Forge.BlackMarket.BUI;
using Content.Shared._Forge.BlackMarket.Components;
using Content.Shared._Forge.BlackMarket.Prototypes;
using Content.Shared._NF.Bank.Components;
using Content.Shared._NF.CrateMachine.Components;
using Content.Shared.Database;
using Content.Shared.Popups;
using Content.Shared.Power;
using Content.Shared.Random;
using Content.Shared.Random.Helpers;
using Content.Server.Power.EntitySystems;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Forge.BlackMarket;

public sealed partial class BlackMarketSystem : EntitySystem
{
    private const float UiRefreshInterval = 30f;

    [Flags]
    private enum ContractExclusion
    {
        None = 0,
        OtherCategorySlots = 1 << 0,
        CurrentContract = 1 << 1,
        RecentHistory = 1 << 2,
        PurchaseLimit = 1 << 3,

        /// <summary>
        /// Full exclusion: no duplicates, no recent history, no purchase-limit overflow.
        /// </summary>
        Strict = OtherCategorySlots | CurrentContract | RecentHistory | PurchaseLimit,

        /// <summary>
        /// Allow re-rolling the current contract when the pool is tiny, but never duplicate other slots.
        /// </summary>
        AllowSameContract = OtherCategorySlots | PurchaseLimit,
    }

    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly BankSystem _bank = default!;
    [Dependency] private readonly CrateMachineSystem _crateMachine = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly MetaDataSystem _meta = default!;
    [Dependency] private readonly IAdminLogManager _adminLog = default!;

    private float _uiRefreshAccumulator;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BlackMarketConsoleComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<BlackMarketConsoleComponent, BoundUIOpenedEvent>(OnUiOpened);
        SubscribeLocalEvent<BlackMarketConsoleComponent, BlackMarketPurchaseMessage>(OnPurchase);
        SubscribeLocalEvent<BlackMarketConsoleComponent, PowerChangedEvent>(OnPowerChanged);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var anySlotChanged = false;
        var query = EntityQueryEnumerator<BlackMarketConsoleComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.Slots.Count == 0)
                continue;

            var changed = false;
            for (var i = 0; i < comp.Slots.Count; i++)
            {
                var slot = comp.Slots[i];
                if (_timing.CurTime < slot.NextEventTime)
                    continue;

                switch (slot.Mode)
                {
                    case BlackMarketSlotMode.Available:
                        if (TryRollContract(uid, comp, i, excludeCurrent: true))
                            changed = true;
                        break;
                    case BlackMarketSlotMode.PurchasedCooldown:
                        if (TryRollContract(uid, comp, i))
                            changed = true;
                        break;
                }
            }

            if (changed)
            {
                anySlotChanged = true;
                UpdateUi(uid, comp);
            }
        }

        _uiRefreshAccumulator += frameTime;
        if (_uiRefreshAccumulator < UiRefreshInterval && !anySlotChanged)
            return;

        _uiRefreshAccumulator = 0f;

        var uiQuery = EntityQueryEnumerator<BlackMarketConsoleComponent, UserInterfaceComponent>();
        while (uiQuery.MoveNext(out var uid, out var comp, out _))
        {
            if (!_ui.IsUiOpen(uid, BlackMarketConsoleUiKey.Key))
                continue;

            UpdateUi(uid, comp);
        }
    }

    private void OnMapInit(EntityUid uid, BlackMarketConsoleComponent comp, MapInitEvent args)
    {
        if (comp.Slots.Count > 0)
            return;

        foreach (var (categoryId, count) in comp.CategorySlots)
        {
            if (!_proto.HasIndex<BlackMarketCategoryPrototype>(categoryId))
            {
                Log.Error($"Black market console {ToPrettyString(uid)} references unknown category '{categoryId}'");
                continue;
            }

            for (var i = 0; i < count; i++)
            {
                comp.Slots.Add(new BlackMarketSlotData { CategoryId = categoryId });
                TryRollContract(uid, comp, comp.Slots.Count - 1);
            }
        }
    }

    private void OnUiOpened(EntityUid uid, BlackMarketConsoleComponent comp, BoundUIOpenedEvent args)
    {
        UpdateUi(uid, comp, args.Actor);
    }

    private void OnPowerChanged(EntityUid uid, BlackMarketConsoleComponent comp, ref PowerChangedEvent args)
    {
        if (args.Powered)
            return;

        _ui.CloseUi(uid, BlackMarketConsoleUiKey.Key);
    }

    private void OnPurchase(EntityUid uid, BlackMarketConsoleComponent comp, BlackMarketPurchaseMessage args)
    {
        if (args.Actor is not { Valid: true } player)
            return;

        if (args.SlotIndex < 0 || args.SlotIndex >= comp.Slots.Count)
            return;

        var slot = comp.Slots[args.SlotIndex];
        if (slot.Mode != BlackMarketSlotMode.Available || slot.ContractId == null)
            return;

        if (!this.IsPowered(uid, EntityManager))
        {
            _audio.PlayPvs(comp.DenySound, uid);
            return;
        }

        if (!_proto.TryIndex<BlackMarketContractPrototype>(slot.ContractId, out var contract))
            return;

        if (contract.Stub)
        {
            _audio.PlayPvs(comp.DenySound, uid);
            return;
        }

        if (IsPurchaseLimitReached(comp, contract))
        {
            _audio.PlayPvs(comp.DenySound, uid);
            _popup.PopupEntity(Loc.GetString("black-market-purchase-limit-reached"), player, player);
            UpdateUi(uid, comp, player);
            return;
        }

        if (!TryComp<BankAccountComponent>(player, out _))
        {
            _audio.PlayPvs(comp.DenySound, uid);
            return;
        }

        var price = GetEffectivePrice(contract, comp);
        if (!_bank.TryGetBalance(player, out var balance) || balance < price)
        {
            _audio.PlayPvs(comp.DenySound, uid);
            _popup.PopupEntity(
                Loc.GetString("black-market-insufficient-funds", ("cost", price)),
                player,
                player);
            UpdateUi(uid, comp, player);
            return;
        }

        if (!_crateMachine.FindNearestUnoccupied(
                uid,
                comp.MaxCrateMachineDistance,
                CrateMachineKind.BlackMarket,
                out var machineUid,
                out var machineComp,
                comp.CratePrototype.ToString()))
        {
            _audio.PlayPvs(comp.DenySound, uid);
            _popup.PopupEntity(Loc.GetString("black-market-no-crate-machine"), player, player);
            return;
        }

        if (!_bank.TryBankWithdraw(player, price))
        {
            _audio.PlayPvs(comp.DenySound, uid);
            return;
        }

        RecordPurchase(comp, contract);
        _crateMachine.QueueBlackMarketDelivery(machineUid.Value, comp.CratePrototype, contract);
        _crateMachine.OpenFor(machineUid.Value, machineComp);
        EnterPurchasedCooldown(uid, comp, args.SlotIndex);
        _audio.PlayPvs(comp.PurchaseSound, uid);
        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(player)} purchased black market contract {contract.ID} from {ToPrettyString(uid)} for {price} spesos");

        UpdateUi(uid, comp, player);
    }

    private void RecordPurchase(BlackMarketConsoleComponent comp, BlackMarketContractPrototype contract)
    {
        comp.ContractPurchaseCounts.TryGetValue(contract.ID, out var count);
        comp.ContractPurchaseCounts[contract.ID] = count + 1;

        if (_proto.TryIndex(contract.Category, out BlackMarketCategoryPrototype? category))
            AddToRecentHistory(comp, category, contract.ID);
    }

    private static bool IsPurchaseLimitReached(BlackMarketConsoleComponent comp, BlackMarketContractPrototype contract)
    {
        if (contract.PurchaseLimit <= 0)
            return false;

        comp.ContractPurchaseCounts.TryGetValue(contract.ID, out var count);
        return count >= contract.PurchaseLimit;
    }

    private static int GetPurchasesRemaining(BlackMarketConsoleComponent comp, BlackMarketContractPrototype contract)
    {
        if (contract.PurchaseLimit <= 0)
            return -1;

        comp.ContractPurchaseCounts.TryGetValue(contract.ID, out var count);
        return Math.Max(0, contract.PurchaseLimit - count);
    }

    public static int GetEffectivePrice(BlackMarketContractPrototype contract, BlackMarketConsoleComponent comp)
    {
        if (!contract.DynamicPrice)
            return contract.Price;

        comp.ContractPurchaseCounts.TryGetValue(contract.ID, out var purchases);
        var multiplier = 1f + contract.DynamicPriceIncreasePercent * purchases;
        var price = (int) MathF.Round(contract.Price * multiplier);

        if (contract.DynamicPriceMax > 0)
            price = Math.Min(price, contract.DynamicPriceMax);

        return Math.Max(price, contract.Price);
    }

    private void EnterPurchasedCooldown(EntityUid uid, BlackMarketConsoleComponent comp, int slotIndex)
    {
        var slot = comp.Slots[slotIndex];
        if (!_proto.TryIndex<BlackMarketCategoryPrototype>(slot.CategoryId, out var category))
            return;

        slot.ContractId = null;
        slot.Mode = BlackMarketSlotMode.PurchasedCooldown;
        slot.NextEventTime = _timing.CurTime + category.PurchasedRefreshDelay;
        comp.Slots[slotIndex] = slot;
    }

    private bool TryRollContract(
        EntityUid uid,
        BlackMarketConsoleComponent comp,
        int slotIndex,
        bool excludeCurrent = false)
    {
        var slot = comp.Slots[slotIndex];
        if (!_proto.TryIndex<BlackMarketCategoryPrototype>(slot.CategoryId, out var category))
            return false;

        if (!_proto.TryIndex(category.ContractPool, out var pool))
        {
            Log.Error($"Black market category {slot.CategoryId} references unknown pool {category.ContractPool}");
            return ApplyRollFallback(uid, comp, slotIndex, category);
        }

        PruneUnavailableHistory(comp, category, pool);

        var currentContractId = excludeCurrent ? slot.ContractId : null;
        var contractId = TryPickContractId(comp, category, slotIndex, pool, currentContractId);

        if (contractId == null || !_proto.TryIndex<BlackMarketContractPrototype>(contractId, out var contract))
        {
            if (contractId != null)
                Log.Error($"Black market pool {category.ContractPool} picked unknown contract '{contractId}'");

            return ApplyRollFallback(uid, comp, slotIndex, category);
        }

        slot.ContractId = contractId;
        slot.Mode = BlackMarketSlotMode.Available;
        slot.NextEventTime = _timing.CurTime + category.PassiveRefreshDelay;
        comp.Slots[slotIndex] = slot;

        if (!contract.Stub)
            AddToRecentHistory(comp, category, contractId);

        return true;
    }

    /// <summary>
    /// Picks a contract using progressively relaxed exclusions when the pool is too small.
    /// History is trimmed one entry at a time (oldest first) before allowing duplicate slots or stubs.
    /// </summary>
    private string? TryPickContractId(
        BlackMarketConsoleComponent comp,
        BlackMarketCategoryPrototype category,
        int slotIndex,
        WeightedRandomPrototype pool,
        string? currentContractId)
    {
        comp.RecentContractsByCategory.TryGetValue(category.ID, out var history);
        var historyLength = history?.Count ?? 0;

        // Keep full history first, then allow older entries back one at a time.
        for (var historyExclude = historyLength; historyExclude >= 0; historyExclude--)
        {
            var picks = BuildEligiblePicks(
                comp, category, slotIndex, pool, currentContractId,
                ContractExclusion.Strict, historyExclude);

            if (picks.Count > 0)
                return _random.Pick(picks);
        }

        var sameContractPicks = BuildEligiblePicks(
            comp, category, slotIndex, pool, currentContractId,
            ContractExclusion.AllowSameContract);

        if (sameContractPicks.Count > 0)
            return _random.Pick(sameContractPicks);

        return null;
    }

    private Dictionary<string, float> BuildEligiblePicks(
        BlackMarketConsoleComponent comp,
        BlackMarketCategoryPrototype category,
        int slotIndex,
        WeightedRandomPrototype pool,
        string? currentContractId,
        ContractExclusion exclusions,
        int recentHistoryEntriesToExclude = int.MaxValue)
    {
        var excluded = GetExcludedContractIds(
            comp, category, slotIndex, currentContractId, exclusions, recentHistoryEntriesToExclude);
        var picks = new Dictionary<string, float>();

        foreach (var (contractId, weight) in pool.Weights)
        {
            if (weight <= 0 || excluded.Contains(contractId))
                continue;

            if (!_proto.TryIndex<BlackMarketContractPrototype>(contractId, out var contract) || contract.Stub)
                continue;

            picks[contractId] = weight;
        }

        return picks;
    }

    private bool ApplyRollFallback(
        EntityUid uid,
        BlackMarketConsoleComponent comp,
        int slotIndex,
        BlackMarketCategoryPrototype category)
    {
        var slot = comp.Slots[slotIndex];
        string? stubId = null;

        if (category.StubContract is { } stub && _proto.HasIndex(stub))
            stubId = stub;

        slot.ContractId = stubId;
        slot.Mode = BlackMarketSlotMode.Available;
        slot.NextEventTime = _timing.CurTime + category.PassiveRefreshDelay;
        comp.Slots[slotIndex] = slot;

        Log.Warning(
            $"Black market console {ToPrettyString(uid)} slot {slotIndex}: pool exhausted for category {category.ID}, fallback={(stubId ?? "none")}");

        return true;
    }

    private HashSet<string> GetExcludedContractIds(
        BlackMarketConsoleComponent comp,
        BlackMarketCategoryPrototype category,
        int slotIndex,
        string? currentContractId,
        ContractExclusion exclusions,
        int recentHistoryEntriesToExclude = int.MaxValue)
    {
        var excluded = new HashSet<string>();

        if (exclusions.HasFlag(ContractExclusion.OtherCategorySlots))
        {
            for (var i = 0; i < comp.Slots.Count; i++)
            {
                if (i == slotIndex)
                    continue;

                var other = comp.Slots[i];
                if (other.CategoryId != category.ID)
                    continue;

                if (other.Mode != BlackMarketSlotMode.Available || other.ContractId == null)
                    continue;

                if (!_proto.TryIndex<BlackMarketContractPrototype>(other.ContractId, out var otherContract) ||
                    otherContract.Stub)
                    continue;

                excluded.Add(other.ContractId);
            }
        }

        if (currentContractId != null && exclusions.HasFlag(ContractExclusion.CurrentContract))
            excluded.Add(currentContractId);

        if (exclusions.HasFlag(ContractExclusion.RecentHistory) &&
            comp.RecentContractsByCategory.TryGetValue(category.ID, out var recent))
        {
            var excludeCount = recentHistoryEntriesToExclude == int.MaxValue
                ? recent.Count
                : Math.Clamp(recentHistoryEntriesToExclude, 0, recent.Count);

            for (var i = 0; i < excludeCount; i++)
                excluded.Add(recent[i]);
        }

        if (exclusions.HasFlag(ContractExclusion.PurchaseLimit))
        {
            foreach (var (contractId, weight) in _proto.Index(category.ContractPool).Weights)
            {
                if (weight <= 0)
                    continue;

                if (!_proto.TryIndex<BlackMarketContractPrototype>(contractId, out var contract) || contract.Stub)
                    continue;

                if (IsPurchaseLimitReached(comp, contract))
                    excluded.Add(contractId);
            }
        }

        return excluded;
    }

    private static void AddToRecentHistory(
        BlackMarketConsoleComponent comp,
        BlackMarketCategoryPrototype category,
        string contractId)
    {
        if (category.RecentHistorySize <= 0)
            return;

        if (!comp.RecentContractsByCategory.TryGetValue(category.ID, out var history))
        {
            history = new List<string>();
            comp.RecentContractsByCategory[category.ID] = history;
        }

        history.Remove(contractId);
        history.Insert(0, contractId);

        while (history.Count > category.RecentHistorySize)
            history.RemoveAt(history.Count - 1);
    }

    /// <summary>
    /// Drops history entries that can no longer be rolled (limit reached or removed from pool).
    /// </summary>
    private void PruneUnavailableHistory(
        BlackMarketConsoleComponent comp,
        BlackMarketCategoryPrototype category,
        WeightedRandomPrototype pool)
    {
        if (!comp.RecentContractsByCategory.TryGetValue(category.ID, out var history))
            return;

        for (var i = history.Count - 1; i >= 0; i--)
        {
            var contractId = history[i];

            if (!pool.Weights.TryGetValue(contractId, out var weight) || weight <= 0)
            {
                history.RemoveAt(i);
                continue;
            }

            if (!_proto.TryIndex<BlackMarketContractPrototype>(contractId, out var contract) || contract.Stub)
            {
                history.RemoveAt(i);
                continue;
            }

            if (IsPurchaseLimitReached(comp, contract))
                history.RemoveAt(i);
        }
    }

    private void UpdateUi(EntityUid uid, BlackMarketConsoleComponent comp, EntityUid? viewer = null)
    {
        if (!TryComp<UserInterfaceComponent>(uid, out var ui))
            return;

        var balance = 0;
        if (viewer != null)
            _bank.TryGetBalance(viewer.Value, out balance);
        else
        {
            foreach (var actor in _ui.GetActors(uid, BlackMarketConsoleUiKey.Key))
            {
                if (_bank.TryGetBalance(actor, out balance))
                    break;
            }
        }

        var slots = BuildSlotStates(comp);
        _ui.SetUiState((uid, ui), BlackMarketConsoleUiKey.Key, new BlackMarketConsoleState(slots, balance));
    }

    private List<BlackMarketSlotState> BuildSlotStates(BlackMarketConsoleComponent comp)
    {
        var result = new List<BlackMarketSlotState>(comp.Slots.Count);
        for (var i = 0; i < comp.Slots.Count; i++)
        {
            var slot = comp.Slots[i];
            var until = slot.NextEventTime - _timing.CurTime;
            if (until < TimeSpan.Zero)
                until = TimeSpan.Zero;

            var price = 0;
            string? icon = null;
            var purchasesRemaining = -1;
            var available = slot.Mode == BlackMarketSlotMode.Available && slot.ContractId != null;

            if (slot.ContractId != null && _proto.TryIndex<BlackMarketContractPrototype>(slot.ContractId, out var contract))
            {
                price = GetEffectivePrice(contract, comp);
                icon = contract.Icon;
                purchasesRemaining = GetPurchasesRemaining(comp, contract);
                available = !contract.Stub && slot.Mode == BlackMarketSlotMode.Available;
                if (!contract.Stub && purchasesRemaining == 0)
                    available = false;
            }

            result.Add(new BlackMarketSlotState(
                i,
                slot.CategoryId,
                slot.ContractId,
                icon,
                price,
                purchasesRemaining,
                until,
                available));
        }

        return result;
    }
}
