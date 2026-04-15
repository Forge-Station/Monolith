using Content.Shared._NF.CCVar;
using Robust.Shared.Configuration;
using Robust.Shared.Map;

namespace Content.Server._Forge.Crypto.Systems;

public sealed class CryptoMarketSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    private readonly Dictionary<MarketKey, CryptoMarketData> _markets = new();
    private float _accumulator;
    private const float TickRate = 60f; // Forge-Change: market updates once per minute.

    public override void Update(float frameTime)
    {
        _accumulator += frameTime;
        if (_accumulator < TickRate)
            return;

        var dt = _accumulator;
        _accumulator = 0f;

        foreach (var market in _markets.Values)
        {
            UpdateMarket(market, dt);
        }
    }

    public double GetCurrentPrice(MapId mapId, string marketId, double? basePrice = null, float growthMultiplier = 1f)
    {
        var market = EnsureMarket(mapId, marketId, basePrice, growthMultiplier);
        return market.CurrentPrice;
    }

    public IReadOnlyList<double> GetPriceHistory(MapId mapId, string marketId, double? basePrice = null, float growthMultiplier = 1f)
    {
        var market = EnsureMarket(mapId, marketId, basePrice, growthMultiplier);
        return market.PriceHistory;
    }

    public void RegisterSale(MapId mapId, string marketId, int soldUnits, double? basePrice = null, float growthMultiplier = 1f)
    {
        if (soldUnits <= 0)
            return;

        var market = EnsureMarket(mapId, marketId, basePrice, growthMultiplier);
        var (_, finalPrice) = SimulateSaleInternal(market, soldUnits);
        market.CurrentPrice = finalPrice;
        market.RecentSoldVolume += soldUnits;
        market.TimeSinceLastSale = 0f;
        ClampPrice(market);
        PushHistory(market);
    }

    public int GetProjectedPayout(MapId mapId, string marketId, int soldUnits, double? basePrice = null, float growthMultiplier = 1f)
    {
        if (soldUnits <= 0)
            return 0;

        var market = EnsureMarket(mapId, marketId, basePrice, growthMultiplier);
        var (totalPayout, _) = SimulateSaleInternal(market, soldUnits);
        return totalPayout;
    }

    private CryptoMarketData EnsureMarket(MapId mapId, string marketId, double? basePrice = null, float growthMultiplier = 1f)
    {
        var key = new MarketKey(mapId, marketId);
        if (_markets.TryGetValue(key, out var existing))
            return existing;

        var seedPrice = basePrice ?? _cfg.GetCVar(NFCCVars.CryptoBasePrice);
        var market = new CryptoMarketData
        {
            CurrentPrice = seedPrice,
            BasePrice = seedPrice,
            GrowthMultiplier = Math.Max(0.01f, growthMultiplier),
        };

        _markets[key] = market;
        PushHistory(market);
        return market;
    }

    private void UpdateMarket(CryptoMarketData market, float dt)
    {
        market.TimeSinceLastSale += dt;

        var growthRate = _cfg.GetCVar(NFCCVars.CryptoPassiveGrowthRate);
        // Forge-Change: additively grows once per minute, avoids explosive jumps.
        market.CurrentPrice += market.BasePrice * growthRate * market.GrowthMultiplier;

        var decay = _cfg.GetCVar(NFCCVars.CryptoVolumeDecayPerSecond);
        var decayMultiplier = MathF.Pow(decay, dt);
        market.RecentSoldVolume *= decayMultiplier;

        ClampPrice(market);
        PushHistory(market);
    }

    private void ClampPrice(CryptoMarketData market)
    {
        var minPrice = market.BasePrice * _cfg.GetCVar(NFCCVars.CryptoMinPriceMultiplier);
        var multiplierMax = market.BasePrice * _cfg.GetCVar(NFCCVars.CryptoMaxPriceMultiplier);
        var absoluteMax = _cfg.GetCVar(NFCCVars.CryptoAbsoluteMaxPrice);
        var maxPrice = Math.Min(multiplierMax, absoluteMax);
        market.CurrentPrice = Math.Clamp(market.CurrentPrice, minPrice, maxPrice);
    }

    private (int TotalPayout, double FinalPrice) SimulateSaleInternal(CryptoMarketData market, int soldUnits)
    {
        var workPrice = market.CurrentPrice;
        var floorPrice = market.BasePrice * _cfg.GetCVar(NFCCVars.CryptoMinPriceMultiplier);
        var baseDrop = _cfg.GetCVar(NFCCVars.CryptoBaseDrop);
        var volumeDrop = _cfg.GetCVar(NFCCVars.CryptoVolumeDropFactor);
        var momentumDrop = _cfg.GetCVar(NFCCVars.CryptoMomentumDropFactor);
        var minDropMultiplier = _cfg.GetCVar(NFCCVars.CryptoMinDropMultiplier);
        var rollingVolume = market.RecentSoldVolume;
        var payout = 0;

        for (var i = 0; i < soldUnits; i++)
        {
            var piecePrice = Math.Max(floorPrice, workPrice);
            payout += Math.Max(1, (int) Math.Round(piecePrice));

            var dropFraction = baseDrop + (volumeDrop * 0.1f) + momentumDrop * (rollingVolume / 100f);
            var dropMultiplier = Math.Max(minDropMultiplier, 1f - dropFraction);
            workPrice = Math.Max(floorPrice, piecePrice * dropMultiplier);
            rollingVolume += 1f;
        }

        return (payout, workPrice);
    }

    private void PushHistory(CryptoMarketData market)
    {
        market.PriceHistory.Add(market.CurrentPrice);
        var historyLength = _cfg.GetCVar(NFCCVars.CryptoHistoryLength);
        while (market.PriceHistory.Count > historyLength)
        {
            market.PriceHistory.RemoveAt(0);
        }
    }

    private sealed class CryptoMarketData
    {
        public double CurrentPrice;
        public double BasePrice;
        public float GrowthMultiplier;
        public float TimeSinceLastSale;
        public float RecentSoldVolume;
        public List<double> PriceHistory = new();
    }

    private readonly record struct MarketKey(MapId MapId, string MarketId);
}
