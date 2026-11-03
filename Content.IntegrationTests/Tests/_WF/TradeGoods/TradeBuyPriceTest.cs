#nullable enable
using System.Collections.Generic;
using Content.Server._WF.TradeGoods.Systems;
using Content.Shared._Mono.VendingMachine;
using Content.Shared._WF.TradeGoods;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.TradeGoods;

/// <summary>
/// Station trade vendors charge a live price: the curve at how full the shelf is, a few minutes behind it, a shortage
/// raises it further, a freighter's delivery lifts it at once, and it never falls below the vendor's base price.
/// </summary>
[TestFixture]
[TestOf(typeof(WFTradeMarketSystem))]
public sealed class TradeBuyPriceTest
{
    private const string Vendor = "VendingMachineTraderDepot";
    private const string Fabric = TradeTestWorld.Fabric;
    private static readonly EntProtoId Paper = "PaperWrittenDispenserInstructions";

    /// <summary>A crate of fabric bought from a vendor.</summary>
    private static void Buy(IEntityManager entMan, EntityCoordinates coords)
    {
        entMan.AddComponent<VendingMachinePurchaseComponent>(entMan.SpawnEntity(Fabric, coords));
    }

    [Test]
    public async Task VendorChargesTheLivePrice()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var market = entMan.System<WFTradeMarketSystem>();
        var shortages = entMan.System<WFTradeShortageSystem>();

        await server.WaitAssertion(() =>
        {
            var grid = map.Grid.Owner;
            var profile = market.Profile(grid);
            var vendor = entMan.SpawnEntity(Vendor, map.GridCoords);
            var fabric = server.ProtoMan.Index<EntityPrototype>(Fabric);

            var fresh = market.BuyQuote(vendor, Fabric)!.Value;
            Assert.That(fresh.Base, Is.GreaterThan(0));
            Assert.That(fresh.Amount, Is.EqualTo(fresh.Base), "A quiet station should charge the base price.");
            Assert.That(market.BuyPrice(vendor, fabric, fresh.Base), Is.EqualTo(fresh.Amount), "The vend hook should charge the quote.");

            // Small dips barely show, and the curve steepens toward empty.
            var target = (int) profile.StockTarget;
            market.SetVendorStock(grid, Fabric, target / 2);
            var half = market.BuyQuote(vendor, Fabric)!.Value;
            Assert.That(half.Factor, Is.EqualTo(1f + profile.ScarcityPremium * 0.25f).Within(0.01f));
            Assert.That(half.Reason, Is.EqualTo((LocId) "wf-trade-reason-scarce"));
            market.SetVendorStock(grid, Fabric, 0);
            Assert.That(market.BuyQuote(vendor, Fabric)!.Value.Factor, Is.EqualTo(1f + profile.ScarcityPremium).Within(0.01f));

            // A glut never takes a vendor below base.
            market.SetVendorStock(grid, Fabric, profile.VendorStock);
            Assert.That(market.BuyQuote(vendor, Fabric)!.Value.Amount, Is.EqualTo(fresh.Base));

            // A purchase leaves the stock at once and only reaches the price over a few minutes.
            market.SetVendorStock(grid, Fabric, target / 2);
            for (var i = 0; i < 20; i++)
            {
                Buy(entMan, map.GridCoords);
            }

            Assert.That(market.VendorStock(grid, Fabric), Is.EqualTo(target / 2 - 20), "A purchase should come off the station's stock.");
            Assert.That(market.BuyQuote(vendor, Fabric)!.Value.Amount, Is.EqualTo(half.Amount), "A purchase shouldn't move the price yet.");

            var smoothed = 0f;
            var row = market.Row(grid, Fabric);
            var level = row.Stock / profile.StockTarget;
            row.Priced = 0.5f + (level - 0.5f) * 0.75f;
            smoothed = market.BuyQuote(vendor, Fabric)!.Value.Factor;
            Assert.That(smoothed, Is.GreaterThan(half.Factor), "Stock should reach the price as it catches up.");

            // A shortage there raises it on top.
            market.SetVendorStock(grid, Fabric, target / 2);
            shortages.StartLocal(grid, new HashSet<string> { Fabric }, 2f, System.TimeSpan.FromMinutes(5), "fabric");
            var short_ = market.BuyQuote(vendor, Fabric)!.Value;
            Assert.That(short_.Reason, Is.EqualTo((LocId) "wf-trade-reason-short"));
            Assert.That(short_.Factor, Is.EqualTo(2f * half.Factor).Within(0.01f));
            shortages.Clear();

            // A freighter's delivery lifts the price at once.
            market.Row(grid, Fabric).Inbound = profile.StockTarget;
            market.Deliver(grid, Fabric, profile.StockTarget);
            Assert.That(market.BuyQuote(vendor, Fabric)!.Value.Amount, Is.EqualTo(fresh.Base));
            Assert.That(entMan.GetComponent<WFTradeVendorComponent>(vendor).Prices[Fabric], Is.EqualTo(fresh.Base),
                "The vendor's menu price wasn't updated after a delivery.");

            // Anything that isn't a trade good keeps the machine's own price.
            Assert.That(market.BuyPrice(vendor, server.ProtoMan.Index(Paper), 123), Is.EqualTo(123));
        });

        await pair.CleanReturnAsync();
    }
}
