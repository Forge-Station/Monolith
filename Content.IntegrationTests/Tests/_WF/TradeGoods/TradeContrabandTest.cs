#nullable enable
using System.Numerics;
using Content.Server._NF.SectorServices;
using Content.Server._WF.TradeGoods.Systems;
using Content.Shared._WF.TradeGoods;
using Content.Shared.Contraband;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.TradeGoods;

/// <summary>
/// Smuggling crates: bought at the Underground's vendor, bought well by open chutes and never by the fence, seizable by
/// security, and a flagged seller gets one warrant per cooldown. The fence takes sealed freight only.
/// </summary>
[TestFixture]
[TestOf(typeof(WFTradeContrabandSystem))]
public sealed class TradeContrabandTest
{
    private const string Contraband = "WFTradeGoodContraband";
    private const string FenceVendor = "WFVendingMachineFence";

    [Test]
    public async Task ContrabandSellsOpenlyAtARisk()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var market = entMan.System<WFTradeMarketSystem>();
        var contraband = entMan.System<WFTradeContrabandSystem>();

        await server.WaitAssertion(() =>
        {
            var chute = TradeTestWorld.MarketChute(entMan, map);
            var (fenceGrid, fence) = TradeTestWorld.Station(entMan, map, new Vector2(0f, 60f), "WF test fence", TradeTestWorld.Fence);
            var vendor = entMan.SpawnEntity(FenceVendor, new EntityCoordinates(fenceGrid, 0.5f, 0.5f));
            var profile = market.Profile(map.Grid.Owner);

            // The Underground's vendor sells it live; open chutes list it well above that, the fence not at all.
            var buy = market.BuyQuote(vendor, Contraband)!.Value;
            var sell = market.Quote(chute, Contraband)!.Value;
            Assert.That(sell.Base, Is.GreaterThan(buy.Base * 2), "An open chute should pay well over the vendor price.");
            Assert.That(market.Quote(fence, Contraband), Is.Null, "The fence shouldn't buy contraband.");

            var crate = entMan.SpawnEntity(Contraband, map.GridCoords);
            Assert.That(entMan.HasComponent<ContrabandComponent>(crate), Is.True, "Security should be able to seize it.");

            // A flagged seller gets a warrant in their name, and no second one while the first is fresh. The board lives
            // on the sector service; a test map hosts one if the server has none.
            if (!entMan.EntityExists(entMan.System<SectorServiceSystem>().GetServiceEntity()))
                entMan.AddComponent<StationSectorServiceHostComponent>(map.MapUid);
            var seller = entMan.SpawnEntity(null, map.GridCoords);
            entMan.System<MetaDataSystem>().SetEntityName(seller, "WF contraband test seller");
            var warrant = contraband.Flag(seller, map.Grid.Owner, null);
            Assert.That(warrant, Is.Not.Null, "Flagging a seller posted no warrant.");
            Assert.That(warrant!.Name, Is.EqualTo("WF contraband test seller"));
            Assert.That(warrant.Reward, Is.EqualTo(profile.ContrabandWarrant));
            Assert.That(contraband.Flag(seller, map.Grid.Owner, null), Is.Null, "A seller was flagged twice within the cooldown.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ContrabandPriceNeverGluts()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var market = entMan.System<WFTradeMarketSystem>();

        await server.WaitAssertion(() =>
        {
            var chute = TradeTestWorld.MarketChute(entMan, map);
            if (!entMan.EntityExists(entMan.System<SectorServiceSystem>().GetServiceEntity()))
                entMan.AddComponent<StationSectorServiceHostComponent>(map.MapUid);
            var far = TradeTestWorld.Grid(entMan, map, new Vector2(5000f, 0f), "WF test far yard");
            var seller = entMan.SpawnEntity(null, map.GridCoords);

            var before = market.Quote(chute, Contraband)!.Value;
            Assert.That(before.Amount, Is.EqualTo(40000));
            for (var i = 0; i < 5; i++)
            {
                market.Sell(chute, TradeTestWorld.Hauled(entMan, far, map.GridCoords, Contraband), seller);
            }

            Assert.That(market.Quote(chute, Contraband)!.Value.Amount, Is.EqualTo(before.Amount), "Selling contraband lowered its price.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task FenceTakesSealedFreightOnly()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var market = entMan.System<WFTradeMarketSystem>();

        await server.WaitAssertion(() =>
        {
            TradeTestWorld.MarketChute(entMan, map);
            var (_, fence) = TradeTestWorld.Station(entMan, map, new Vector2(0f, 60f), "WF test fence", TradeTestWorld.Fence);
            var far = TradeTestWorld.Grid(entMan, map, new Vector2(5000f, 0f), "WF test far yard");

            var plain = TradeTestWorld.Hauled(entMan, far, map.GridCoords);
            Assert.That(market.CheckHaul(fence, plain), Is.EqualTo((LocId?) "wf-trade-refuse-unsealed"));

            var sealedCrate = TradeTestWorld.Hauled(entMan, far, map.GridCoords);
            entMan.GetComponent<WFTradeOriginComponent>(sealedCrate).Run = "WF test freighter";
            Assert.That(market.CheckHaul(fence, sealedCrate), Is.Null, "The fence refused sealed freight.");
            Assert.That(market.Quote(fence, TradeTestWorld.Fabric)!.Value.Reason, Is.EqualTo((LocId) "wf-trade-reason-sealed-only"));
        });

        await pair.CleanReturnAsync();
    }
}
