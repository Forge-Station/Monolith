using Content.Server.Stack;
using Content.Server._Mono.Cargo;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Stacks;
using Content.Shared._Forge.Crypto.Components;
using Content.Shared._Forge.Crypto.Systems;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Map;
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
    private float _uiRefreshAccumulator;
    private const float UiRefreshInterval = 30f; // Forge-Change: keep opened console UI live.
    private static readonly CoinMarketProfile[] DefaultMarketProfiles =
    {
        new("contribcoin", 9000, 0.9f),
        new("ideascoin", 14000, 1.1f),
        new("corvaxcoin", 22000, 1.35f),
    };

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CryptoSellConsoleComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<CryptoSellConsoleComponent, EntInsertedIntoContainerMessage>(OnContainerChanged);
        SubscribeLocalEvent<CryptoSellConsoleComponent, EntRemovedFromContainerMessage>(OnContainerChanged);
        SubscribeLocalEvent<CryptoSellConsoleComponent, BoundUIOpenedEvent>(OnUiOpened);
        SubscribeLocalEvent<CryptoSellConsoleComponent, CryptoSellRequestMessage>(OnSellRequested);
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

        var mapId = Transform(uid).MapID;
        var profile = GetMarketProfile(bitcoin, coin);
        var payout = _market.GetProjectedPayout(mapId, profile.MarketId, units, profile.BasePrice, profile.GrowthMultiplier);

        _market.RegisterSale(mapId, profile.MarketId, units, profile.BasePrice, profile.GrowthMultiplier);
        Del(bitcoin);

        var stackPrototype = _prototype.Index(component.CashType);
        _stack.Spawn(payout, stackPrototype, Transform(uid).Coordinates);
        UpdateUi((uid, component));
    }

    private void OnEjectRequested(EntityUid uid, CryptoSellConsoleComponent component, CryptoSellEjectMessage args)
    {
        _itemSlots.TryEjectToHands(uid, component.BitcoinSlot, args.Actor);
        UpdateUi((uid, component));
    }

    private void UpdateUi(Entity<CryptoSellConsoleComponent> entity)
    {
        var mapId = Transform(entity).MapID;

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
            unitPrice = _market.GetCurrentPrice(mapId, profile.MarketId, profile.BasePrice, profile.GrowthMultiplier);
            history = _market.GetPriceHistory(mapId, profile.MarketId, profile.BasePrice, profile.GrowthMultiplier).ToList();
            payout = _market.GetProjectedPayout(mapId, profile.MarketId, units, profile.BasePrice, profile.GrowthMultiplier);
        }

        var markets = BuildMarketSnapshots(mapId, bitcoin, hasBitcoin);

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

        if (!string.IsNullOrWhiteSpace(coin.MarketId))
            return new CoinMarketProfile(coin.MarketId, basePrice, growthMultiplier);

        return new CoinMarketProfile(MetaData(uid).EntityPrototype?.ID ?? "default", basePrice, growthMultiplier);
    }

    private readonly record struct CoinMarketProfile(string MarketId, double BasePrice, float GrowthMultiplier);

    private List<CryptoMarketUiData> BuildMarketSnapshots(MapId mapId, EntityUid? insertedBitcoin, bool hasBitcoin)
    {
        var profiles = new Dictionary<string, CoinMarketProfile>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in DefaultMarketProfiles)
        {
            profiles[profile.MarketId] = profile;
        }

        if (hasBitcoin && insertedBitcoin is { } inserted && TryComp<CryptoCoinComponent>(inserted, out var coin))
        {
            var insertedProfile = GetMarketProfile(inserted, coin);
            profiles[insertedProfile.MarketId] = insertedProfile;
        }

        var result = new List<CryptoMarketUiData>(profiles.Count);
        foreach (var profile in profiles.Values)
        {
            var price = _market.GetCurrentPrice(mapId, profile.MarketId, profile.BasePrice, profile.GrowthMultiplier);
            var priceHistory = _market.GetPriceHistory(mapId, profile.MarketId, profile.BasePrice, profile.GrowthMultiplier).ToList();
            result.Add(new CryptoMarketUiData(profile.MarketId, price, priceHistory));
        }

        return result;
    }
}
