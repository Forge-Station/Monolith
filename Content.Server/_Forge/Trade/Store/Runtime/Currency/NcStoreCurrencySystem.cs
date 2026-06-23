using Content.Server._NF.Bank;
using Content.Shared._Forge.Trade;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Stacks;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;

namespace Content.Server._Forge.Trade;

public sealed class NcStoreCurrencySystem : EntitySystem, IStoreCurrencyService
{
    [Dependency] private readonly BankSystem _bank = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IEntityManager _ents = default!;
    private readonly Dictionary<string, ICurrencyHandler> _handlerCache = new(StringComparer.Ordinal);

    private readonly List<ICurrencyHandler> _handlers = new();
    private bool _handlersInitialized;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly NcStoreInventorySystem _inventory = default!;
    [Dependency] private readonly IPrototypeManager _protos = default!;
    [Dependency] private readonly SharedStackSystem _stacks = default!;
    [Dependency] private readonly SharedTransformSystem _xform = default!;


    public bool TryGetBalance(EntityUid user, in NcInventorySnapshot snapshot, string currencyId, out int balance)
    {
        balance = 0;
        if (!TryResolveHandler(currencyId, out var h))
            return false;
        return h.TryGetBalance(user, snapshot, currencyId, out balance);
    }

    public bool TryPickCurrencyForBuy(
        EntityUid user,
        NcStoreComponent store,
        NcStoreListingDef listing,
        in NcInventorySnapshot snapshot,
        out string currency,
        out int unitPrice,
        out int balance
    )
    {
        currency = string.Empty;
        unitPrice = 0;
        balance = 0;

        if (listing.Cost.Count == 0)
            return false;

        if (HasWhitelistedCurrency(store))
            return TryPickWhitelistedBuyCurrency(user, store, listing, snapshot, out currency, out unitPrice, out balance);

        return TryPickFallbackBuyCurrency(user, listing, snapshot, out currency, out unitPrice, out balance);
    }

    public bool TryPickCurrencyForSell(
        NcStoreComponent store,
        NcStoreListingDef listing,
        out string currency,
        out int unitPrice
    )
    {
        currency = string.Empty;
        unitPrice = 0;
        if (listing.Cost.Count == 0)
            return false;

        foreach (var cur in store.CurrencyWhitelist)
        {
            if (string.IsNullOrWhiteSpace(cur))
                continue;
            if (listing.Cost.TryGetValue(cur, out var price) && price > 0 && TryResolveHandler(cur, out _))
            {
                currency = cur;
                unitPrice = price;
                return true;
            }
        }

        KeyValuePair<string, int>? best = null;
        foreach (var kv in listing.Cost)
        {
            if (string.IsNullOrWhiteSpace(kv.Key) || kv.Value <= 0)
                continue;

            if (!TryResolveHandler(kv.Key, out _))
                continue;

            if (best == null || string.CompareOrdinal(kv.Key, best.Value.Key) < 0)
                best = kv;
        }

        if (best == null)
            return false;

        currency = best.Value.Key;
        unitPrice = best.Value.Value;
        return true;
    }

    public bool CanHandleCurrency(string currencyId)
    {
        return !string.IsNullOrWhiteSpace(currencyId) && TryResolveHandler(currencyId, out _);
    }

    public bool IsVirtualCurrency(string currencyId)
    {
        return TryResolveHandler(currencyId, out var h) && h.IsVirtualCurrency(currencyId);
    }

    public bool CanGiveCurrency(EntityUid user, string currencyId, int amount)
    {
        if (amount <= 0)
            return true;

        if (!TryResolveHandler(currencyId, out var h))
            return false;

        return h.CanGiveCurrency(user, currencyId, amount);
    }

    public bool TryTakeCurrency(EntityUid user, string currencyId, int amount)
    {
        if (amount <= 0)
            return true;
        if (!TryResolveHandler(currencyId, out var h))
            return false;
        return h.TryTake(user, currencyId, amount);
    }

    public bool TryGiveCurrency(EntityUid user, string currencyId, int amount)
    {
        if (amount <= 0)
            return true;
        if (!TryResolveHandler(currencyId, out var h))
            return false;
        if (!h.CanGiveCurrency(user, currencyId, amount))
            return false;
        return h.TryGiveCurrency(user, currencyId, amount);
    }

    public bool BeginCurrencyIssueTransaction()
    {
        EnsureHandlersInitialized();

        foreach (var h in _handlers)
        {
            if (h.BeginCurrencyIssueTransaction())
                continue;

            foreach (var rollback in _handlers)
            {
                rollback.RollbackCurrencyIssueTransaction(EntityUid.Invalid);
            }

            return false;
        }

        return true;
    }

    public void CommitCurrencyIssueTransaction(EntityUid user)
    {
        EnsureHandlersInitialized();

        foreach (var h in _handlers)
        {
            h.CommitCurrencyIssueTransaction(user);
        }
    }

    public void RollbackCurrencyIssueTransaction(EntityUid user)
    {
        EnsureHandlersInitialized();

        foreach (var h in _handlers)
        {
            h.RollbackCurrencyIssueTransaction(user);
        }
    }

    public bool BeginCurrencyDebitTransaction()
    {
        EnsureHandlersInitialized();

        foreach (var h in _handlers)
        {
            if (h.BeginCurrencyDebitTransaction())
                continue;

            foreach (var rollback in _handlers)
            {
                rollback.RollbackCurrencyDebitTransaction(EntityUid.Invalid);
            }

            return false;
        }

        return true;
    }

    public bool CommitCurrencyDebitTransaction(EntityUid user)
    {
        EnsureHandlersInitialized();

        foreach (var h in _handlers)
        {
            if (h.PrepareCurrencyDebitTransaction(user))
                continue;

            return false;
        }

        foreach (var h in _handlers)
        {
            if (h.CommitCurrencyDebitTransaction(user))
                continue;

            return false;
        }

        return true;
    }

    public void RollbackCurrencyDebitTransaction(EntityUid user)
    {
        EnsureHandlersInitialized();

        foreach (var h in _handlers)
        {
            h.RollbackCurrencyDebitTransaction(user);
        }
    }

    public override void Initialize()
    {
        base.Initialize();
        RebuildHandlers();
    }

    private void EnsureHandlersInitialized()
    {
        if (_handlersInitialized)
            return;

        RebuildHandlers();
    }

    private void RebuildHandlers()
    {
        _handlers.Clear();
        _handlerCache.Clear();
        _handlers.Add(new BankCurrencyHandler(_bank, _ents, _cfg));
        _handlers.Add(new StackCurrencyHandler(_ents, _hands, _inventory, _protos, _stacks, _xform));
        _handlersInitialized = true;
    }

    private bool TryResolveHandler(string currencyId, out ICurrencyHandler handler)
    {
        EnsureHandlersInitialized();

        if (_handlerCache.TryGetValue(currencyId, out var cached))
        {
            handler = cached;
            return true;
        }

        foreach (var h in _handlers)
        {
            if (!h.CanHandle(currencyId))
                continue;

            _handlerCache[currencyId] = h;
            handler = h;
            return true;
        }

        handler = default!;
        return false;
    }

    private static bool HasWhitelistedCurrency(NcStoreComponent store)
    {
        foreach (var currencyId in store.CurrencyWhitelist)
        {
            if (!string.IsNullOrWhiteSpace(currencyId))
                return true;
        }

        return false;
    }

    private bool TryPickWhitelistedBuyCurrency(
        EntityUid user,
        NcStoreComponent store,
        NcStoreListingDef listing,
        in NcInventorySnapshot snapshot,
        out string currency,
        out int unitPrice,
        out int balance
    )
    {
        currency = string.Empty;
        unitPrice = 0;
        balance = 0;

        foreach (var currencyId in store.CurrencyWhitelist)
        {
            if (!TryGetAffordableBuyCurrency(user, snapshot, listing, currencyId, out var price, out var currentBalance))
                continue;

            currency = currencyId;
            unitPrice = price;
            balance = currentBalance;
            return true;
        }

        return false;
    }

    private bool TryPickFallbackBuyCurrency(
        EntityUid user,
        NcStoreListingDef listing,
        in NcInventorySnapshot snapshot,
        out string currency,
        out int unitPrice,
        out int balance
    )
    {
        currency = string.Empty;
        unitPrice = 0;
        balance = 0;

        if (!TryGetBestBuyCurrency(listing, out var best))
            return false;

        if (!TryGetBalance(user, snapshot, best.Key, out balance))
            balance = 0;

        if (balance < best.Value)
            return false;

        currency = best.Key;
        unitPrice = best.Value;
        return true;
    }

    private bool TryGetAffordableBuyCurrency(
        EntityUid user,
        in NcInventorySnapshot snapshot,
        NcStoreListingDef listing,
        string currencyId,
        out int price,
        out int balance
    )
    {
        price = 0;
        balance = 0;

        if (string.IsNullOrWhiteSpace(currencyId))
            return false;

        if (!listing.Cost.TryGetValue(currencyId, out price) || price <= 0)
            return false;

        if (!TryGetBalance(user, snapshot, currencyId, out balance))
            balance = 0;

        return balance >= price;
    }

    private bool TryGetBestBuyCurrency(
        NcStoreListingDef listing,
        out KeyValuePair<string, int> best
    )
    {
        best = default;
        var found = false;

        foreach (var candidate in listing.Cost)
        {
            if (string.IsNullOrWhiteSpace(candidate.Key) || candidate.Value <= 0)
                continue;

            if (!TryResolveHandler(candidate.Key, out _))
                continue;

            if (!found || string.CompareOrdinal(candidate.Key, best.Key) < 0)
            {
                best = candidate;
                found = true;
            }
        }

        return found;
    }
}
