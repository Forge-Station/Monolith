using Content.Server.Hands.Systems;
using Content.Server.Stack;
using Content.Server._Mono.Cargo;
using Content.Server._NF.Bank;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Popups;
using Content.Shared.Stacks;
using Content.Shared._Forge.Crypto.Components;
using Content.Shared._Forge.Crypto.Systems;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;
using System.Linq;

namespace Content.Server._Forge.Crypto.Systems;

public sealed class CryptoSellConsoleSystem : SharedCryptoSellConsoleSystem
{
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;
    [Dependency] private readonly CryptoMarketSystem _market = default!;
    [Dependency] private readonly StackSystem _stack = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly BankSystem _bank = default!;
    [Dependency] private readonly HandsSystem _hands = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    private float _uiRefreshAccumulator;
    private const float UiRefreshInterval = 30f; // Forge-Change: keep opened console UI live.
    private static readonly CoinMarketProfile[] DefaultMarketProfiles =
    {
        new("contribcoin", 9000, 0.9f, "BitcoinContribcoin"),
        new("ideascoin", 14000, 1.1f, "BitcoinIdeascoin"),
        new("corvaxcoin", 22000, 1.35f, "BitcoinCorvaxcoin"),
    };

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CryptoSellConsoleComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<CryptoSellConsoleComponent, EntInsertedIntoContainerMessage>(OnContainerChanged);
        SubscribeLocalEvent<CryptoSellConsoleComponent, EntRemovedFromContainerMessage>(OnContainerChanged);
        SubscribeLocalEvent<CryptoSellConsoleComponent, BoundUIOpenedEvent>(OnUiOpened);
        SubscribeLocalEvent<CryptoSellConsoleComponent, CryptoSellRequestMessage>(OnSellRequested);
        SubscribeLocalEvent<CryptoSellConsoleComponent, CryptoBuyRequestMessage>(OnBuyRequested);
        SubscribeLocalEvent<CryptoSellConsoleComponent, CryptoSellEjectMessage>(OnEjectRequested);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        _uiRefreshAccumulator += frameTime;
        if (_uiRefreshAccumulator < UiRefreshInterval)
            return;

        _uiRefreshAccumulator = 0f;
        var query = EntityQueryEnumerator<CryptoSellConsoleComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (!_ui.IsUiOpen(uid, CryptoSellConsoleUiKey.Key))
                continue;

            UpdateUi((uid, comp));
        }
    }

    private void OnStartup(EntityUid uid, CryptoSellConsoleComponent component, ComponentStartup args)
    {
        UpdateUi((uid, component));
    }

    private void OnContainerChanged(EntityUid uid, CryptoSellConsoleComponent component, ContainerModifiedMessage args)
    {
        UpdateUi((uid, component));
    }

    private void OnUiOpened(EntityUid uid, CryptoSellConsoleComponent component, BoundUIOpenedEvent args)
    {
        UpdateUi((uid, component));
    }

    private void OnSellRequested(EntityUid uid, CryptoSellConsoleComponent component, CryptoSellRequestMessage args)
    {
        if (component.BitcoinSlot.Item is not { Valid: true } bitcoin || !TryComp<CryptoCoinComponent>(bitcoin, out var coin))
            return;

        var units = GetBitcoinUnits(bitcoin);
        if (units <= 0)
            return;

        var profile = GetMarketProfile(bitcoin, coin);
        var payout = _market.GetProjectedPayout(profile.MarketId, units, profile.BasePrice, profile.GrowthMultiplier);

        _market.RegisterSale(profile.MarketId, units, profile.BasePrice, profile.GrowthMultiplier);
        Del(bitcoin);

        var stackPrototype = _prototype.Index(component.CashType);
        _stack.Spawn(payout, stackPrototype, Transform(uid).Coordinates);
        UpdateUi((uid, component));
    }

    private void OnBuyRequested(EntityUid uid, CryptoSellConsoleComponent component, CryptoBuyRequestMessage args)
    {
        if (string.IsNullOrWhiteSpace(args.MarketId))
            return;

        var bitcoin = component.BitcoinSlot.Item;
        var hasBitcoin = bitcoin is { Valid: true };

        if (!TryMergeProfiles(bitcoin, hasBitcoin, out var profiles)
            || !profiles.TryGetValue(args.MarketId, out var profile))
        {
            _popup.PopupEntity(Loc.GetString("crypto-console-buy-invalid-market"), uid, args.Actor);
            return;
        }

        if (string.IsNullOrEmpty(profile.CoinPrototype)
            || !_prototype.HasIndex<EntityPrototype>(profile.CoinPrototype))
        {
            _popup.PopupEntity(Loc.GetString("crypto-console-buy-spawn-failed"), uid, args.Actor);
            return;
        }

        var cost = _market.GetProjectedPurchaseCost(profile.MarketId, 1, profile.BasePrice, profile.GrowthMultiplier);
        if (cost <= 0)
            return;

        if (!_bank.TryBankWithdraw(args.Actor, cost))
        {
            _popup.PopupEntity(Loc.GetString("cargo-console-insufficient-funds", ("cost", cost)), uid, args.Actor);
            return;
        }

        var spawned = _stack.SpawnMultiple(profile.CoinPrototype, 1, Transform(uid).Coordinates);
        if (spawned.Count == 0)
        {
            _bank.TryBankDeposit(args.Actor, cost);
            _popup.PopupEntity(Loc.GetString("crypto-console-buy-spawn-failed"), uid, args.Actor);
            UpdateUi((uid, component));
            return;
        }

        _market.RegisterPurchase(profile.MarketId, 1, profile.BasePrice, profile.GrowthMultiplier);
        _hands.TryPickupAnyHand(args.Actor, spawned[0]);

        UpdateUi((uid, component));
    }

    private void OnEjectRequested(EntityUid uid, CryptoSellConsoleComponent component, CryptoSellEjectMessage args)
    {
        _itemSlots.TryEjectToHands(uid, component.BitcoinSlot, args.Actor);
        UpdateUi((uid, component));
    }

    private void UpdateUi(Entity<CryptoSellConsoleComponent> entity)
    {
        var bitcoin = entity.Comp.BitcoinSlot.Item;
        var hasBitcoin = bitcoin is { Valid: true };
        var insertedName = string.Empty;
        var units = 0;
        var unitPrice = 0d;
        var payout = 0;
        List<double> history = new();

        if (hasBitcoin && bitcoin is { } inserted && TryComp<CryptoCoinComponent>(inserted, out var coin))
        {
            insertedName = MetaData(inserted).EntityName;
            units = GetBitcoinUnits(inserted);
            var profile = GetMarketProfile(inserted, coin);
            unitPrice = _market.GetCurrentPrice(profile.MarketId, profile.BasePrice, profile.GrowthMultiplier);
            history = _market.GetPriceHistory(profile.MarketId, profile.BasePrice, profile.GrowthMultiplier).ToList();
            payout = _market.GetProjectedPayout(profile.MarketId, units, profile.BasePrice, profile.GrowthMultiplier);
        }

        var markets = BuildMarketSnapshots(bitcoin, hasBitcoin);

        var state = new CryptoSellConsoleBoundUserInterfaceState(
            hasBitcoin,
            insertedName,
            units,
            unitPrice,
            payout,
            history,
            markets);

        _ui.SetUiState(entity.Owner, CryptoSellConsoleUiKey.Key, state);
    }

    private int GetBitcoinUnits(EntityUid uid)
    {
        if (TryComp<StackComponent>(uid, out var stack))
            return Math.Max(1, stack.Count);

        return 1;
    }

    private CoinMarketProfile GetMarketProfile(EntityUid uid, CryptoCoinComponent coin)
    {
        var basePrice = coin.BasePrice;
        if (basePrice <= 0 && TryComp<DriftingPriceComponent>(uid, out var drifting))
            basePrice = drifting.BasePrice;

        if (basePrice <= 0)
            basePrice = 10000;

        var growthMultiplier = Math.Max(0.01f, coin.GrowthMultiplier);
        var proto = MetaData(uid).EntityPrototype?.ID ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(coin.MarketId))
            return new CoinMarketProfile(coin.MarketId, basePrice, growthMultiplier, proto);

        return new CoinMarketProfile(MetaData(uid).EntityPrototype?.ID ?? "default", basePrice, growthMultiplier, proto);
    }

    private readonly record struct CoinMarketProfile(string MarketId, double BasePrice, float GrowthMultiplier, string CoinPrototype);

    /// <summary>
    /// Fills <paramref name="profiles"/> the same way as <see cref="BuildMarketSnapshots"/> for consistent market keys.
    /// </summary>
    private bool TryMergeProfiles(EntityUid? insertedBitcoin, bool hasBitcoin, out Dictionary<string, CoinMarketProfile> profiles)
    {
        profiles = new Dictionary<string, CoinMarketProfile>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in DefaultMarketProfiles)
            profiles[profile.MarketId] = profile;

        if (hasBitcoin && insertedBitcoin is { } inserted && TryComp<CryptoCoinComponent>(inserted, out var coin))
        {
            var insertedProfile = GetMarketProfile(inserted, coin);
            profiles[insertedProfile.MarketId] = insertedProfile;
        }

        return profiles.Count > 0;
    }

    private List<CryptoMarketUiData> BuildMarketSnapshots(EntityUid? insertedBitcoin, bool hasBitcoin)
    {
        TryMergeProfiles(insertedBitcoin, hasBitcoin, out var profiles);

        var result = new List<CryptoMarketUiData>(profiles.Count);
        foreach (var profile in profiles.Values)
        {
            var price = _market.GetCurrentPrice(profile.MarketId, profile.BasePrice, profile.GrowthMultiplier);
            var priceHistory = _market.GetPriceHistory(profile.MarketId, profile.BasePrice, profile.GrowthMultiplier).ToList();
            var buyOne = _market.GetProjectedPurchaseCost(profile.MarketId, 1, profile.BasePrice, profile.GrowthMultiplier);
            result.Add(new CryptoMarketUiData(profile.MarketId, price, priceHistory, buyOne));
        }

        return result;
    }
}
