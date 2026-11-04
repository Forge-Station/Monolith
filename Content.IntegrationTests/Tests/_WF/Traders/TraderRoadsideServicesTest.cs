#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Pair;
using Content.Server._WF.Traders;
using Content.Server.Cargo.Systems;
using Content.Shared._WF.Traders;
using Content.Shared._WF.Vehicles.Hardpoint;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Stacks;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Traders;

/// <summary>
/// The roadside traders' services: the mechanic repairs a damaged vehicle parked nearby and charges for it, and the
/// innkeeper takes an organ as payment, sells organs, and eats a body left at his counter.
/// </summary>
[TestFixture]
public sealed class TraderRoadsideServicesTest
{
    private const string Mechanic = "WFPoiMechanic";
    private const string Innkeeper = "WFPoiInnkeeper";
    private const string Vehicle = "AU14VehicleCivTruck";
    private const string Organ = "OrganHumanKidneys";
    private const string SoldOrgan = "OrganHumanLiver";
    private const string Drink = "DrinkBeerBottleFull";

    /// <summary>Lays a floor, a trader facing a table to the south and a customer beside him.</summary>
    private static async Task<(EntityUid Trader, EntityUid Customer, EntityCoordinates Counter)> SetUp(TestPair pair, TestMapData map, string trader)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        EntityUid traderUid = default, customer = default;
        var counter = new EntityCoordinates(map.Grid.Owner, 0.5f, -0.5f);
        await server.WaitPost(() =>
        {
            var maps = server.System<SharedMapSystem>();
            for (var x = -4; x < 24; x++)
            for (var y = -4; y < 24; y++)
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), map.Tile.Tile);

            entMan.SpawnEntity("Table", counter);
            traderUid = entMan.SpawnEntity(trader, new EntityCoordinates(map.Grid.Owner, 0.5f, 0.5f));
            customer = entMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid.Owner, 1.5f, -1.5f));
            var comp = entMan.GetComponent<TraderComponent>(traderUid);
            server.System<TraderSystem>().RefreshTable((traderUid, comp));
            Assert.That(comp.Table, Is.Not.Null, $"{trader} found no table.");
        });
        await server.WaitRunTicks(2);
        return (traderUid, customer, counter);
    }

    private static int ZoneCash(IEntityManager entMan, TraderSystem traders, EntityUid trader)
    {
        return traders.GetZoneItems((trader, entMan.GetComponent<TraderComponent>(trader)))
            .Where(item => entMan.TryGetComponent<StackComponent>(item, out var stack) && stack.StackTypeId == "Credit" && !entMan.IsQueuedForDeletion(item))
            .Sum(item => entMan.GetComponent<StackComponent>(item).Count);
    }

    [Test]
    public async Task MechanicRepairsAVehicleForMoney()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var (trader, customer, counter) = await SetUp(pair, map, Mechanic);
        var hardpoints = server.System<HardpointSystem>();
        var repair = server.System<TraderVehicleRepairSystem>();
        var traders = server.System<TraderSystem>();
        EntityUid vehicle = default;
        var price = 0;
        const int Tendered = 100000;

        await server.WaitPost(() =>
        {
            vehicle = entMan.SpawnEntity(Vehicle, new EntityCoordinates(map.Grid.Owner, 8.5f, 8.5f));
        });
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            var service = (trader, entMan.GetComponent<TraderVehicleRepairComponent>(trader));
            Assert.That(repair.GetQuote(service, vehicle), Is.Null, "A new vehicle shouldn't need fixing.");

            var frame = entMan.GetComponent<HardpointIntegrityComponent>(vehicle);
            Assert.That(hardpoints.DamageHardpoint(vehicle, vehicle, frame.MaxIntegrity * 0.5f), Is.True, "The frame took no damage.");
            Assert.That(repair.FindVehicle(service), Is.EqualTo(vehicle), "The mechanic didn't find the vehicle out front.");
            price = repair.GetQuote(service, vehicle) ?? 0;
            Assert.That(price, Is.GreaterThan(0), "A damaged vehicle should cost something to fix.");

            var cash = entMan.SpawnEntity("SpaceCash", counter);
            server.System<SharedStackSystem>().SetCount(cash, Tendered);

            var action = new TraderActionEvent(trader, customer, TraderAction.RepairVehicle, null);
            entMan.EventBus.RaiseLocalEvent(trader, ref action);
            Assert.That(action.Handled, Is.True, "Nobody ran the repair service.");
            Assert.That(entMan.GetComponent<TraderVehicleRepairComponent>(trader).QuotedVehicle, Is.EqualTo(vehicle), "The mechanic gave no quote.");

            var confirmed = new TraderConfirmedEvent(trader, customer, true);
            entMan.EventBus.RaiseLocalEvent(trader, ref confirmed);
        });
        await server.WaitRunTicks(3);

        await server.WaitAssertion(() =>
        {
            Assert.That(hardpoints.GetMissingIntegrity(vehicle, out _), Is.Zero, "The vehicle wasn't repaired.");
            Assert.That(ZoneCash(entMan, traders, trader), Is.EqualTo(Tendered - price), "The customer wasn't charged the quote.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task InnkeeperTakesAnOrganAndSellsOne()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var (trader, customer, counter) = await SetUp(pair, map, Innkeeper);
        var traders = server.System<TraderSystem>();
        var shop = server.System<TraderShopSystem>();
        EntityUid organ = default;
        int drinkPrice = 0, organValue = 0;

        await server.WaitAssertion(() =>
        {
            var traderEnt = (trader, entMan.GetComponent<TraderComponent>(trader));
            var shopEnt = (trader, entMan.GetComponent<TraderShopComponent>(trader));
            var stock = shop.GetStock(shopEnt);
            drinkPrice = stock.First(entry => entry.Item == Drink).Price;
            Assert.That(stock.Any(entry => entry.Item == SoldOrgan), Is.True, "The innkeeper doesn't sell organs.");

            organ = entMan.SpawnEntity(Organ, counter);
            var goods = traders.GetZoneGoods(traderEnt);
            Assert.That(goods, Has.Count.EqualTo(1), "The innkeeper doesn't count the organ as payment.");
            organValue = goods[0].Value;
            Assert.That(organValue, Is.EqualTo((int) Math.Floor(server.System<PricingSystem>().GetPrice(organ) * 0.5)),
                "An organ should count for half what it appraises for.");
            Assert.That(organValue, Is.GreaterThan(drinkPrice), "An organ should cover a drink.");

            // No cash and no ID: only the organ can pay.
            Assert.That(shop.TryCheckout(shopEnt, traderEnt, customer, new Dictionary<string, int> { [Drink] = 1 }), Is.True,
                "The innkeeper refused the organ.");
        });
        await server.WaitRunTicks(3);

        var organPrice = 0;
        await server.WaitAssertion(() =>
        {
            var traderEnt = (trader, entMan.GetComponent<TraderComponent>(trader));
            var zone = traders.GetZoneItems(traderEnt);
            Assert.That(entMan.Deleted(organ), Is.True, "The innkeeper didn't take the organ.");
            Assert.That(zone.Any(item => entMan.GetComponent<MetaDataComponent>(item).EntityPrototype?.ID == Drink), Is.True, "The drink wasn't served.");
            Assert.That(ZoneCash(entMan, traders, trader), Is.EqualTo(organValue - drinkPrice), "The change for the organ is wrong.");

            // Now buy an organ with the change and some cash of one's own.
            var shopEnt = (trader, entMan.GetComponent<TraderShopComponent>(trader));
            organPrice = shop.GetStock(shopEnt).First(entry => entry.Item == SoldOrgan).Price;
            Assert.That(organPrice, Is.GreaterThan(server.System<PricingSystem>().GetEstimatedPrice(server.ProtoMan.Index<EntityPrototype>(SoldOrgan))),
                "An organ should cost more than it resells for.");
            var cash = entMan.SpawnEntity("SpaceCash", counter);
            server.System<SharedStackSystem>().SetCount(cash, organPrice);
            Assert.That(shop.TryCheckout(shopEnt, traderEnt, customer, new Dictionary<string, int> { [SoldOrgan] = 1 }), Is.True,
                "The innkeeper wouldn't sell an organ.");
        });
        await server.WaitRunTicks(3);

        await server.WaitAssertion(() =>
        {
            var zone = traders.GetZoneItems((trader, entMan.GetComponent<TraderComponent>(trader)));
            Assert.That(zone.Any(item => entMan.GetComponent<MetaDataComponent>(item).EntityPrototype?.ID == SoldOrgan), Is.True,
                "The organ bought wasn't handed over.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task InnkeeperEatsTheDeadAndGivesThemUp()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var (trader, customer, _) = await SetUp(pair, map, Innkeeper);
        var mobStates = server.System<MobStateSystem>();
        var containers = server.System<SharedContainerSystem>();
        EntityUid body = default;

        await server.WaitPost(() =>
        {
            body = entMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid.Owner, 1.5f, 0.5f));
            mobStates.ChangeMobState(body, MobState.Dead);
        });

        // He looks round once a second and eats for a few seconds more.
        var timing = server.Timing;
        await server.WaitRunTicks((int) Math.Ceiling(8 / timing.TickPeriod.TotalSeconds));

        await server.WaitAssertion(() =>
        {
            Assert.That(containers.TryGetContainingContainer((body, null, null), out var stomach) && stomach.Owner == trader, Is.True,
                "The innkeeper didn't eat the body at his counter.");
            Assert.That(containers.IsEntityInContainer(customer), Is.False, "The innkeeper ate a living customer.");
            mobStates.ChangeMobState(trader, MobState.Dead);
        });
        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
        {
            Assert.That(containers.IsEntityInContainer(body), Is.False, "The innkeeper kept the body when he died.");
        });

        await pair.CleanReturnAsync();
    }
}
