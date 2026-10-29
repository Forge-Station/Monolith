#nullable enable
using System.Numerics;
using Content.Server._WF.TradeGoods.Systems;
using Content.Shared._Mono.Company;
using Content.Shared._WF.TradeGoods;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.TradeGoods;

/// <summary>
/// Hitting a crate with the Market app names the station paying most for it, skipping chutes that would refuse it, and
/// names a hidden market only to the underground.
/// </summary>
[TestFixture]
[TestOf(typeof(WFMarketCartridgeSystem))]
public sealed class MarketBestSaleTest
{
    private const string Fabric = TradeTestWorld.Fabric;

    [Test]
    public async Task BestSaleSkipsRefusalsAndHidesTheFence()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var market = entMan.System<WFTradeMarketSystem>();
        var cartridge = entMan.System<WFMarketCartridgeSystem>();

        await server.WaitAssertion(() =>
        {
            var mall = TradeTestWorld.MarketChute(entMan, map);
            var (depotGrid, depot) = TradeTestWorld.Station(entMan, map, new Vector2(0f, -60f), "WF test depot");
            var (fenceGrid, fence) = TradeTestWorld.Station(entMan, map, new Vector2(0f, 60f), "WF test fence", TradeTestWorld.Fence);
            var far = TradeTestWorld.Grid(entMan, map, new Vector2(5000f, 0f), "WF test far yard");
            var near = TradeTestWorld.Grid(entMan, map, new Vector2(30f, 0f), "WF test near yard");
            var depotName = TradeTestWorld.Name(entMan, depotGrid);
            var fenceName = TradeTestWorld.Name(entMan, fenceGrid);

            EntityUid Holder(string company)
            {
                var holder = entMan.SpawnEntity(null, map.GridCoords);
                entMan.EnsureComponent<CompanyComponent>(holder).CompanyName = company;
                return holder;
            }

            var lawful = Holder("TSFCivilian");
            var underground = Holder("WFUnderground");

            // A glut at the mall leaves the depot paying most for an ordinary hauled crate.
            market.AddPressure(map.Grid.Owner, Fabric, 0.5f);
            var crate = TradeTestWorld.Hauled(entMan, far, map.GridCoords);
            var depotQuote = market.Quote(depot, Fabric, crate, lawful)!.Value.Amount;
            Assert.That(depotQuote, Is.GreaterThan(market.Quote(mall, Fabric, crate, lawful)!.Value.Amount));
            Assert.That(cartridge.BestSale(crate, lawful), Is.EqualTo((depotName, depotQuote)));
            Assert.That(cartridge.BestSale(crate, underground)?.Station, Is.EqualTo(depotName),
                "The fence takes only sealed freight, so it shouldn't be suggested for an unsealed crate.");

            // Sealed freight pays full price only at the fence, which only the underground is told about.
            var origin = entMan.GetComponent<WFTradeOriginComponent>(crate);
            origin.Run = "WF test freighter";
            origin.Destination = near;
            market.AddPressure(depotGrid, Fabric, 0.5f);
            var fenceQuote = market.Quote(fence, Fabric, crate, underground)!.Value.Amount;
            Assert.That(fenceQuote, Is.GreaterThan(market.Quote(depot, Fabric, crate, underground)!.Value.Amount));
            Assert.That(cartridge.BestSale(crate, underground), Is.EqualTo((fenceName, fenceQuote)));
            Assert.That(cartridge.BestSale(crate, lawful)?.Station, Is.Not.EqualTo(fenceName).And.Not.Null,
                "A lawful holder should never be pointed at the fence.");

            // Made next to every chute: nobody takes it, and asking doesn't stamp it anew.
            var local = TradeTestWorld.Hauled(entMan, near, map.GridCoords);
            var stamped = entMan.GetComponent<WFTradeOriginComponent>(local).Point;
            Assert.That(cartridge.BestSale(local, underground), Is.Null);
            Assert.That(entMan.GetComponent<WFTradeOriginComponent>(local).Point, Is.EqualTo(stamped));
        });

        await pair.CleanReturnAsync();
    }
}
