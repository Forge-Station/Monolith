#nullable enable
using System.Linq;
using System.Numerics;
using System.Reflection;
using Content.IntegrationTests.Tests._WF.CargoLoad;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.TradeGoods.Components;
using Content.Server._WF.TradeGoods.Systems;
using Content.Server.GameTicking;
using Content.Shared.GameTicking;
using Content.Shared._WF.TradeGoods;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WF.TradeGoods;

/// <summary>
/// Consignments go between stations a haul apart and never to a wandering merchant or a hidden market, are recalled once
/// their destination is gone or long after their deadline, a crate made aboard a ship docked to a moving market is made
/// there, and a low sale never suggests a hidden market.
/// </summary>
[TestFixture]
[TestOf(typeof(WFTradeSpecialCrateSystem))]
public sealed class TradeConsignmentTest
{
    private const string Hideout = "CargoChuteHideout";
    private const string Ammo = "TradeGoodAmmo";

    /// <summary>Puts the dummy ticker in a round, so the timed consignment checks run, as the sector tests do.</summary>
    private static void StartRound(GameTicker ticker)
    {
        typeof(GameTicker).GetProperty(nameof(GameTicker.RunLevel), BindingFlags.Public | BindingFlags.Instance)!
            .GetSetMethod(true)!.Invoke(ticker, new object[] { GameRunLevel.InRound });
    }

    [Test]
    public async Task ConsignmentsSkipMerchantsAndAreRecalled()
    {
        // Dirty: the forced round must not leak into the next test on this pair.
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var special = entMan.System<WFTradeSpecialCrateSystem>();
        var xformSys = entMan.System<SharedTransformSystem>();
        var timing = server.ResolveDependency<IGameTiming>();

        EntityUid first = default, second = default;
        await server.WaitAssertion(() =>
        {
            StartRound(entMan.System<GameTicker>());
            TradeTestWorld.MarketChute(entMan, map);
            TradeTestWorld.Station(entMan, map, new Vector2(5000f, 0f), "WF consignment far depot");
            var merchant = TradeTestWorld.Station(entMan, map, new Vector2(-5000f, 0f), "WF consignment merchant").Grid;
            entMan.EnsureComponent<WFEncounterGridComponent>(merchant);
            var fence = TradeTestWorld.Station(entMan, map, new Vector2(0f, -5000f), "WF consignment fence", TradeTestWorld.Fence).Grid;

            first = special.SpawnConsignment()!.Value;
            second = special.SpawnConsignment()!.Value;
            Assert.That(special.SpawnConsignment(), Is.Null, "A merchant or hidden market was given a consignment.");

            foreach (var crate in new[] { first, second })
            {
                var consignment = entMan.GetComponent<WFConsignmentComponent>(crate);
                Assert.That(consignment.Destination, Is.Not.EqualTo(merchant));
                Assert.That(consignment.Origin, Is.Not.EqualTo(merchant));
                Assert.That(consignment.Destination, Is.Not.EqualTo(fence), "A consignment was bound for a hidden market.");
                Assert.That(consignment.Origin, Is.Not.EqualTo(fence), "A consignment was posted at a hidden market.");
                var distance = (xformSys.GetWorldPosition(consignment.Destination!.Value)
                                - xformSys.GetWorldPosition(consignment.Origin!.Value)).Length();
                Assert.That(distance, Is.GreaterThanOrEqualTo(3000f), "A consignment was posted between stations closer than a haul.");
            }

            // One loses its destination; the other is carried off and abandoned well past its deadline.
            var lost = entMan.GetComponent<WFConsignmentComponent>(first).Destination!.Value;
            var lapsed = entMan.GetComponent<WFConsignmentComponent>(second);
            lapsed.Deadline = timing.CurTime - lapsed.Window - System.TimeSpan.FromSeconds(1);
            xformSys.SetCoordinates(second, new EntityCoordinates(map.MapUid, new Vector2(2500f, 0f)));
            entMan.DeleteEntity(lost);

            Assert.That(special.Consignments().Select(entry => entry.Destination), Does.Not.Contain(lost),
                "The app still lists a consignment for a station that is gone.");
        });

        await pair.RunTicksSync(400);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(first), Is.True, "A consignment whose destination is gone wasn't recalled.");
            Assert.That(entMan.Deleted(second), Is.True, "A carried-off consignment was never recalled after its deadline.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CrateMadeDockedToAMovingMarketIsMadeThere()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var market = entMan.System<WFTradeMarketSystem>();
        var xformSys = entMan.System<SharedTransformSystem>();

        await server.WaitAssertion(() =>
        {
            entMan.EnsureComponent<WFTradeMarketMapComponent>(map.MapUid);
            var merchant = CargoDeck.Create(entMan, map, new Vector2(30f, 0f));
            var chute = entMan.SpawnEntity(TradeTestWorld.Chute, new EntityCoordinates(merchant, 1.5f, 1.5f));
            var ship = CargoDeck.Create(entMan, map, new Vector2(30f, 10f));
            CargoDeck.Dock(entMan, CargoDeck.AddPort(entMan, merchant, new Vector2(4.5f, 9.5f), 180f),
                CargoDeck.AddPort(entMan, ship, new Vector2(4.5f, 0.5f), 0f));
            var loose = CargoDeck.Create(entMan, map, new Vector2(60f, 10f));

            var docked = entMan.SpawnEntity(TradeTestWorld.Fabric, new EntityCoordinates(ship, 1.5f, 1.5f));
            var undocked = entMan.SpawnEntity(TradeTestWorld.Fabric, new EntityCoordinates(loose, 1.5f, 1.5f));
            Assert.That(entMan.GetComponent<WFTradeOriginComponent>(docked).Docked, Does.Contain(merchant));

            // The merchant wanders off with the ship still docked; the crates ride along to its chute.
            xformSys.SetWorldPosition(merchant, new Vector2(6000f, 0f));
            xformSys.SetCoordinates(docked, xformSys.GetMoverCoordinates(chute));
            xformSys.SetCoordinates(undocked, xformSys.GetMoverCoordinates(chute));

            Assert.That(market.CheckHaul(chute, docked), Is.EqualTo((LocId?) "wf-trade-refuse-made-here"),
                "Riding docked to a moving market counted as a haul.");
            Assert.That(market.WasHauled(chute, docked), Is.False);
            Assert.That(market.CheckHaul(chute, undocked), Is.Null, "A crate hauled from a ship that was never docked was refused.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task LowSaleNeverSuggestsAHiddenMarket()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var market = entMan.System<WFTradeMarketSystem>();

        await server.WaitAssertion(() =>
        {
            var chute = TradeTestWorld.MarketChute(entMan, map);
            var (helios, heliosChute) = TradeTestWorld.Station(entMan, map, new Vector2(0f, 5000f), "WF test helios", Hideout);
            Assert.That(market.Quote(heliosChute, Ammo)!.Value.Amount, Is.GreaterThan(market.Quote(chute, Ammo)!.Value.Amount),
                "The test needs Helios to pay more for ammo than the open chute.");

            var best = market.Best(Ammo);
            Assert.That(best, Is.Not.Null);
            Assert.That(best!.Value.Chute, Is.Not.EqualTo(heliosChute), "A sale suggested a hidden market.");
            Assert.That(market.IsHiddenMarket(helios), Is.True);
        });

        await pair.CleanReturnAsync();
    }
}
