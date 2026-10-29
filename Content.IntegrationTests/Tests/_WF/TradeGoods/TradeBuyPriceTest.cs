#nullable enable
using System.Collections.Generic;
using Content.Server._WF.TradeGoods.Components;
using Content.Server._WF.TradeGoods.Systems;
using Content.Shared._Mono.VendingMachine;
using Content.Shared._WF.TradeGoods;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WF.TradeGoods;

/// <summary>
/// Station trade vendors charge a live price: each crate bought makes the good scarcer there, the scarcity decays, a
/// shortage raises it further, freight supply eases it, and it never falls below the vendor's base price.
/// </summary>
[TestFixture]
[TestOf(typeof(WFTradeMarketSystem))]
public sealed class TradeBuyPriceTest
{
    private const string Vendor = "VendingMachineTraderDepot";
    private const string Fabric = TradeTestWorld.Fabric;
    private static readonly EntProtoId Paper = "PaperWrittenDispenserInstructions";

    [Test]
    public async Task VendorChargesTheLivePrice()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var market = entMan.System<WFTradeMarketSystem>();
        var shortages = entMan.System<WFTradeShortageSystem>();
        var timing = server.ResolveDependency<IGameTiming>();

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

            // Each crate bought from a vendor makes the good scarcer at the station.
            for (var i = 0; i < 4; i++)
            {
                var crate = entMan.SpawnEntity(Fabric, map.GridCoords);
                entMan.AddComponent<VendingMachinePurchaseComponent>(crate);
            }

            var scarce = market.BuyQuote(vendor, Fabric)!.Value;
            var expected = (int) System.MathF.Round(fresh.Base * (1 + 4 * profile.BuyPerPurchase));
            Assert.That(scarce.Amount, Is.EqualTo(expected));
            Assert.That(scarce.Reason, Is.EqualTo((LocId) "wf-trade-reason-scarce"));
            Assert.That(entMan.GetComponent<WFTradeVendorComponent>(vendor).Prices[Fabric], Is.EqualTo(expected),
                "The vendor's menu price wasn't updated after a purchase.");

            // Scarcity halves over the half-life.
            var comp = entMan.GetComponent<WFTradeMarketComponent>(grid);
            comp.Scarcity[Fabric] = (comp.Scarcity[Fabric].Scarcity, timing.CurTime - profile.HalfLife);
            Assert.That(market.Scarcity(grid, Fabric), Is.EqualTo(2 * profile.BuyPerPurchase).Within(0.001f));

            // A shortage there raises it on top.
            shortages.StartLocal(grid, new HashSet<string> { Fabric }, 2f, System.TimeSpan.FromMinutes(5), "wf-trade-goods-textiles");
            var short_ = market.BuyQuote(vendor, Fabric)!.Value;
            Assert.That(short_.Reason, Is.EqualTo((LocId) "wf-trade-reason-short"));
            Assert.That(short_.Factor, Is.EqualTo(2f * (1 + 2 * profile.BuyPerPurchase)).Within(0.01f));
            shortages.Clear();

            // Freight supply eases scarcity, but the price never drops below base.
            market.AddPressure(grid, Fabric, 1f, supplied: true);
            Assert.That(market.BuyQuote(vendor, Fabric)!.Value.Amount, Is.EqualTo(fresh.Base));

            // Anything that isn't a trade good keeps the machine's own price.
            Assert.That(market.BuyPrice(vendor, server.ProtoMan.Index(Paper), 123), Is.EqualTo(123));
        });

        await pair.CleanReturnAsync();
    }
}
