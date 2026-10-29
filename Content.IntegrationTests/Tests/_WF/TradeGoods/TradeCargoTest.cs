#nullable enable
using System.Linq;
using System.Numerics;
using System.Reflection;
using Content.Server._NF.Trade;
using Content.Server._WF.Encounters;
using Content.Server._WF.Encounters.Systems;
using Content.Server._WF.ReaverCampaign.Systems;
using Content.Server._WF.TradeGoods.Systems;
using Content.Server.Station.Systems;
using Content.Shared._NF.Trade;
using Content.Shared._WF.TradeGoods;
using Content.Shared.Station.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WF.TradeGoods;

/// <summary>
/// Cargo value: a ship's trade crates and console trading crates add up to a value
/// that raises extortion tolls and draws raids, and the Market app names where a console crate is bound and what it
/// fetches there now.
/// </summary>
[TestFixture]
[TestOf(typeof(WFTradeCrateMarketSystem))]
public sealed class TradeCargoTest
{
    private const string Fabric = TradeTestWorld.Fabric;
    private const string ConsoleCrate = "CrateTradeSecureHigh";

    /// <summary>What the market says the trade cargo aboard a grid is worth.</summary>
    private static int CargoValue(IEntityManager entMan, EntityUid grid)
    {
        var ev = new WFCargoValueQueryEvent(grid);
        entMan.EventBus.RaiseEvent(EventSource.Local, ref ev);
        return ev.Value;
    }

    /// <remarks>
    /// The extorter's toll and the raid weight are private; they are called by reflection, since reaching them for real
    /// takes crewed player ships.
    /// </remarks>
    [Test]
    public async Task CargoValueDrawsPredators()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var market = entMan.System<WFTradeMarketSystem>();
        var encounters = entMan.System<WFEncounterSystem>();
        var reavers = entMan.System<WFReaverCampaignSystem>();

        await server.WaitAssertion(() =>
        {
            TradeTestWorld.MarketChute(entMan, map);
            var laden = TradeTestWorld.Grid(entMan, map, new Vector2(0f, -60f), "WF test laden ship", 3);
            var empty = TradeTestWorld.Grid(entMan, map, new Vector2(0f, -120f), "WF test empty ship");
            entMan.SpawnEntity(Fabric, new EntityCoordinates(laden, 0.5f, 0.5f));
            entMan.SpawnEntity(Fabric, new EntityCoordinates(laden, 1.5f, 0.5f));
            var console = entMan.SpawnEntity(ConsoleCrate, new EntityCoordinates(laden, 2.5f, 0.5f));

            // Trade goods at the best base price any chute pays; console crates at their destination value.
            var best = market.Chutes().Select(chute => market.BasePrice(chute.Dispenser, Fabric) ?? 0).DefaultIfEmpty(0).Max();
            Assert.That(best, Is.GreaterThan(0), $"No chute on the test map buys {Fabric}.");
            var value = 2 * best + entMan.GetComponent<TradeCrateComponent>(console).ValueAtDestination;
            Assert.That(CargoValue(entMan, laden), Is.EqualTo(value));
            Assert.That(CargoValue(entMan, empty), Is.Zero);

            var toll = typeof(WFEncounterSystem).GetMethod("CargoToll", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(toll, Is.Not.Null, "WFEncounterSystem.CargoToll was renamed; update this test.");
            int Toll(int usual, EntityUid? mark) => (int) toll!.Invoke(encounters, new object?[] { usual, mark })!;
            Assert.That(Toll(100, empty), Is.EqualTo(100), "An empty hold should pay the usual toll.");
            Assert.That(Toll(100, null), Is.EqualTo(100));
            Assert.That(Toll(100, laden), Is.GreaterThan(100), "A laden hold should pay a share of its cargo.");
            Assert.That(Toll(1_000_000, laden), Is.EqualTo(1_000_000), "A share of the cargo should never lower the usual toll.");

            var weight = typeof(WFReaverCampaignSystem).GetMethod("CargoWeight", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(weight, Is.Not.Null, "WFReaverCampaignSystem.CargoWeight was renamed; update this test.");
            Assert.That((float) weight!.Invoke(reavers, new object[] { empty })!, Is.EqualTo(1f));
            Assert.That((float) weight!.Invoke(reavers, new object[] { laden })!, Is.GreaterThan(1f), "A laden ship should draw raiders.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ConsoleCrateNamesItsDestination()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var market = entMan.System<WFTradeMarketSystem>();
        var crates = entMan.System<WFTradeCrateMarketSystem>();
        var timing = server.ResolveDependency<IGameTiming>();

        await server.WaitAssertion(() =>
        {
            TradeTestWorld.MarketChute(entMan, map);
            var depot = TradeTestWorld.Grid(entMan, map, new Vector2(0f, 60f), "WF test depot grid");
            var station = entMan.Spawn();
            entMan.EnsureComponent<StationDataComponent>(station);
            entMan.System<MetaDataSystem>().SetEntityName(station, "WF Test Depot");
            entMan.System<StationSystem>().AddGridToStation(station, depot);
            entMan.AddComponent(station, new TradeCrateDestinationComponent { DestinationProto = "CargoA" });

            Assert.That(crates.Destination(entMan.SpawnEntity(Fabric, map.GridCoords)), Is.Null, "A trade good has no console destination.");

            // A new trading crate picks its destination from every depot on the server; keep one bound for the test's.
            EntityUid crate = default;
            for (var i = 0; i < 30 && !crate.IsValid(); i++)
            {
                var candidate = entMan.SpawnEntity(ConsoleCrate, map.GridCoords);
                if (entMan.GetComponent<TradeCrateComponent>(candidate).DestinationStation == station)
                    crate = candidate;
                else
                    entMan.DeleteEntity(candidate);
            }

            Assert.That(crate.IsValid(), Is.True, "No trading crate was bound for the test depot.");
            var trade = entMan.GetComponent<TradeCrateComponent>(crate);
            var demand = entMan.GetComponent<WFTradeCrateMarketComponent>(crate);

            int Expected()
            {
                var amount = (float) trade.ValueAtDestination;
                if (trade.ExpressDeliveryTime is { } due)
                    amount += timing.CurTime <= due ? trade.ExpressOnTimeBonus : -trade.ExpressLatePenalty;

                return (int) System.MathF.Max(0f, amount * crates.Factor(depot, demand));
            }

            var found = crates.Destination(crate);
            Assert.That(found, Is.Not.Null);
            var fresh = found!.Value;
            Assert.That(fresh.Station, Is.EqualTo("WF Test Depot"));
            Assert.That(fresh.Amount, Is.EqualTo(Expected()));

            // Demand at the depot's grid lowers what the crate fetches there.
            market.AddPressure(depot, demand.Good, 0.5f);
            var glutted = crates.Destination(crate)!.Value;
            Assert.That(glutted.Amount, Is.LessThan(fresh.Amount));
            Assert.That(glutted.Amount, Is.EqualTo(Expected()));

            // A depot that is gone is no destination.
            entMan.DeleteEntity(station);
            Assert.That(crates.Destination(crate), Is.Null);
        });

        await pair.CleanReturnAsync();
    }
}
