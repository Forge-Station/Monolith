#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.Server._WF.TradeGoods;
using Content.Server._WF.TradeGoods.Systems;
using Content.Server.GameTicking;
using Content.Shared._WF.TradeGoods;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WF.TradeGoods;

/// <summary>
/// Payouts beyond the chute price: rush orders raise a station's quote until filled or expired.
/// </summary>
[TestFixture]
[TestOf(typeof(WFTradeRushSystem))]
public sealed class TradeRewardsTest
{
    private const string Fabric = TradeTestWorld.Fabric;
    private const string Metals = "TradeGoodMetals";
    private const string Scrap = "TradeGoodScrap";
    private static readonly ProtoId<WFTradeRushPrototype> IndustrialRush = "WFTradeRushIndustrial";

    /// <summary>A real round: rush orders only expire while one is running.</summary>
    [Test]
    public async Task RushOrdersPayCountAndExpire()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, DummyTicker = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var market = entMan.System<WFTradeMarketSystem>();
        var rushes = entMan.System<WFTradeRushSystem>();
        var ticker = entMan.System<GameTicker>();
        var timing = server.ResolveDependency<IGameTiming>();

        EntityUid chute = default;
        WFTradeRush expiring = default!;

        await server.WaitAssertion(() =>
        {
            Assert.That(ticker.RunLevel, Is.EqualTo(GameRunLevel.InRound), "The test needs a round running.");
            chute = TradeTestWorld.MarketChute(entMan, map);
            var grid = map.Grid.Owner;
            var far = TradeTestWorld.Grid(entMan, map, new Vector2(5000f, 0f), "WF test far yard");
            var seller = entMan.SpawnEntity(null, map.GridCoords);
            const string hauler = "WF rush test hauler";
            entMan.System<MetaDataSystem>().SetEntityName(seller, hauler);

            var rush = rushes.Start(IndustrialRush)!;
            Assert.That(rush, Is.Not.Null, "No station could post an industrial rush.");
            Assert.That(rush.Grid, Is.EqualTo(grid), "The rush went to a station other than the test's.");
            Assert.That(rush.Remaining, Is.EqualTo(rush.Total));

            var quote = market.Quote(chute, Metals)!.Value;
            Assert.That(quote.Reason, Is.EqualTo((LocId) "wf-trade-reason-rush"));
            Assert.That(quote.Factor, Is.EqualTo(rush.Factor).Within(0.01f));

            market.Sell(chute, TradeTestWorld.Hauled(entMan, far, map.GridCoords, Metals), seller);
            Assert.That(rush.Remaining, Is.EqualTo(rush.Total - 1));
            Assert.That(rush.Filled.GetValueOrDefault(hauler), Is.EqualTo(1));

            // A crate whose carrier ship is gone credits the seller.
            var orphan = TradeTestWorld.Hauled(entMan, far, map.GridCoords, Metals);
            var sunk = entMan.SpawnEntity(null, map.GridCoords);
            entMan.GetComponent<WFTradeOriginComponent>(orphan).Carrier = sunk;
            entMan.DeleteEntity(sunk);
            Assert.DoesNotThrow(() => market.Sell(chute, orphan, seller), "A sale threw on a deleted carrier.");
            Assert.That(rush.Filled.GetValueOrDefault(hauler), Is.EqualTo(2));

            for (var i = 0; i < 20 && rush.Remaining > 0; i++)
            {
                market.Sell(chute, TradeTestWorld.Hauled(entMan, far, map.GridCoords, Metals), seller);
            }

            Assert.That(rush.Remaining, Is.Zero);
            Assert.That(rushes.Active, Does.Not.Contain(rush), "A filled rush stayed open.");
            Assert.That(market.Quote(chute, Scrap)!.Value.Reason, Is.Not.EqualTo((LocId) "wf-trade-reason-rush"));

            // A second order nobody fills runs out at its end time.
            expiring = rushes.Start(IndustrialRush)!;
            Assert.That(expiring, Is.Not.Null);
            Assert.That(expiring.Grid, Is.EqualTo(grid));
            Assert.That(market.Quote(chute, Scrap)!.Value.Reason, Is.EqualTo((LocId) "wf-trade-reason-rush"));
            expiring.Ends = timing.CurTime;
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(rushes.Active, Does.Not.Contain(expiring), "An expired rush stayed open.");
            var scrap = market.Quote(chute, Scrap)!.Value;
            Assert.That(scrap.Amount, Is.EqualTo(scrap.Base), "An expired rush still raised the price.");
        });

        await pair.CleanReturnAsync();
    }
}
