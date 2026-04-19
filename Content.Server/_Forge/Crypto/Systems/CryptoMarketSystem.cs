using Content.Shared._NF.CCVar;
using Robust.Shared.Configuration;
using Robust.Shared.Random;

namespace Content.Server._Forge.Crypto.Systems;

public sealed class CryptoMarketSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    /// <summary>
    /// Forge-Change: one shared order book per <see cref="CryptoCoinComponent.MarketId"/> for the whole server,
    /// so multiple sell consoles stay in sync (not keyed by map / grid).
    /// </summary>
    private readonly Dictionary<string, CryptoMarketData> _markets = new(StringComparer.OrdinalIgnoreCase);
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

    public double GetCurrentPrice(string marketId, double? basePrice = null, float growthMultiplier = 1f)
    {
        var market = EnsureMarket(marketId, basePrice, growthMultiplier);
        return market.CurrentPrice;
    }

    public IReadOnlyList<double> GetPriceHistory(string marketId, double? basePrice = null, float growthMultiplier = 1f)
    {
        var market = EnsureMarket(marketId, basePrice, growthMultiplier);
        return market.PriceHistory;
    }

    public void RegisterSale(string marketId, int soldUnits, double? basePrice = null, float growthMultiplier = 1f)
    {
        if (soldUnits <= 0)
            return;

        var market = EnsureMarket(marketId, basePrice, growthMultiplier);
        var (_, finalPrice) = SimulateSaleInternal(market, soldUnits);
        market.CurrentPrice = finalPrice;
        market.RecentSoldVolume += soldUnits;
        market.TimeSinceLastSale = 0f;
        ClampPrice(market);
        PushHistory(market);
    }

    public int GetProjectedPayout(string marketId, int soldUnits, double? basePrice = null, float growthMultiplier = 1f)
    {
        if (soldUnits <= 0)
            return 0;

        var market = EnsureMarket(marketId, basePrice, growthMultiplier);
        var (totalPayout, _) = SimulateSaleInternal(market, soldUnits);
        return totalPayout;
    }

    public int GetProjectedPurchaseCost(string marketId, int boughtUnits, double? basePrice = null, float growthMultiplier = 1f)
    {
        if (boughtUnits <= 0)
            return 0;

        var market = EnsureMarket(marketId, basePrice, growthMultiplier);
        var (totalCost, _) = SimulatePurchaseInternal(market, boughtUnits);
        return totalCost;
    }

    public void RegisterPurchase(string marketId, int boughtUnits, double? basePrice = null, float growthMultiplier = 1f)
    {
        if (boughtUnits <= 0)
            return;

        var market = EnsureMarket(marketId, basePrice, growthMultiplier);
        var (_, finalPrice) = SimulatePurchaseInternal(market, boughtUnits);
        market.CurrentPrice = finalPrice;
        market.RecentBoughtVolume += boughtUnits;
        market.TimeSinceLastSale = 0f;
        ClampPrice(market);
        PushHistory(market);
    }

    private CryptoMarketData EnsureMarket(string marketId, double? basePrice = null, float growthMultiplier = 1f)
    {
        if (string.IsNullOrWhiteSpace(marketId))
            marketId = "default";

        if (_markets.TryGetValue(marketId, out var existing))
            return existing;

        var seedPrice = basePrice ?? _cfg.GetCVar(NFCCVars.CryptoBasePrice);
        var market = new CryptoMarketData
        {
            CurrentPrice = seedPrice,
            BasePrice = seedPrice,
            GrowthMultiplier = Math.Max(0.01f, growthMultiplier),
        };

        _markets[marketId] = market;
        PushHistory(market);
        return market;
    }

    private void UpdateMarket(CryptoMarketData market, float dt)
    {
        market.TimeSinceLastSale += dt;

        var growthRate = _cfg.GetCVar(NFCCVars.CryptoPassiveGrowthRate);
        // Forge-Change: additively grows once per minute, avoids explosive jumps.
        market.CurrentPrice += market.BasePrice * growthRate * market.GrowthMultiplier;

        // Forge-Change: random passive dips so the chart is not only upward.
        var dropChance = _cfg.GetCVar(NFCCVars.CryptoPassiveDropChance);
        if (dropChance > 0f && _random.Prob(Math.Clamp(dropChance, 0f, 1f)))
        {
            var dropStrength = _cfg.GetCVar(NFCCVars.CryptoPassiveDropStrength);
            if (dropStrength > 0f)
                market.CurrentPrice -= market.BasePrice * dropStrength * market.GrowthMultiplier;
        }

        var decay = _cfg.GetCVar(NFCCVars.CryptoVolumeDecayPerSecond);
        var decayMultiplier = MathF.Pow(decay, dt);
        market.RecentSoldVolume *= decayMultiplier;
        market.RecentBoughtVolume *= decayMultiplier;

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

    private (int TotalCost, double FinalPrice) SimulatePurchaseInternal(CryptoMarketData market, int boughtUnits)
    {
        var workPrice = market.CurrentPrice;
        var floorPrice = market.BasePrice * _cfg.GetCVar(NFCCVars.CryptoMinPriceMultiplier);
        var ceilingPrice = GetCeilingPrice(market);
        var baseRise = _cfg.GetCVar(NFCCVars.CryptoBaseRise);
        var volumeRise = _cfg.GetCVar(NFCCVars.CryptoVolumeRiseFactor);
        var momentumRise = _cfg.GetCVar(NFCCVars.CryptoMomentumRiseFactor);
        var maxRiseMultiplier = _cfg.GetCVar(NFCCVars.CryptoMaxRiseMultiplier);
        var rollingVolume = market.RecentBoughtVolume;
        var totalCost = 0;

        for (var i = 0; i < boughtUnits; i++)
        {
            var piecePrice = Math.Max(floorPrice, workPrice);
            totalCost += Math.Max(1, (int) Math.Round(piecePrice));

            var riseFraction = baseRise + (volumeRise * 0.1f) + momentumRise * (rollingVolume / 100f);
            var riseMultiplier = Math.Min(maxRiseMultiplier, 1f + riseFraction);
            workPrice = Math.Min(ceilingPrice, piecePrice * riseMultiplier);
            rollingVolume += 1f;
        }

        return (totalCost, workPrice);
    }

    private double GetCeilingPrice(CryptoMarketData market)
    {
        var multiplierMax = market.BasePrice * _cfg.GetCVar(NFCCVars.CryptoMaxPriceMultiplier);
        var absoluteMax = _cfg.GetCVar(NFCCVars.CryptoAbsoluteMaxPrice);
        return Math.Min(multiplierMax, absoluteMax);
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
        public float RecentBoughtVolume;
        public List<double> PriceHistory = new();
    }
}
