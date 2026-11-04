#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Client._WF.VehicleShop;
using Content.IntegrationTests.Tests._WF.Drydock;
using Content.Server._NF.Bank;
using Content.Server._WF.VehicleShop;
using Content.Shared._NF.Bank.Components;
using Content.Shared._WF.VehicleShop;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.VehicleShop;

/// <summary>
/// The expeditionary terminal sells a vehicle onto its pad from the buyer's bank, won't deliver onto a busy pad, and buys
/// the vehicle back for its share of the value.
/// </summary>
[TestFixture]
[TestOf(typeof(WFVehicleShopSystem))]
public sealed class VehicleShopTest
{
    private const string Terminal = "WFComputerVehicleShopExpeditionary";
    private const string Catalog = "WFVehicleCatalogExpeditionary";

    [Test]
    public async Task BuyOntoThePadAndSellBack()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var shop = server.System<WFVehicleShopSystem>();
        EntityUid terminal = default, pad = default, payer = default;
        var catalog = server.ProtoMan.Index<WFVehicleCatalogPrototype>(Catalog);
        var index = catalog.Vehicles.FindIndex(listing => listing.Vehicle == "VehicleBoxVan");
        var price = catalog.Vehicles[index].Price;

        await server.WaitPost(() =>
        {
            var maps = server.System<SharedMapSystem>();
            var plating = server.Resolve<ITileDefinitionManager>()["Plating"].TileId;
            for (var x = -5; x < 25; x++)
            for (var y = -5; y < 25; y++)
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), new Tile(plating));

            terminal = entMan.SpawnEntity(Terminal, new EntityCoordinates(map.Grid.Owner, 3.5f, 3.5f));
            pad = entMan.SpawnEntity("WFVehicleDeliveryPad", new EntityCoordinates(map.Grid.Owner, 12.5f, 12.5f));
            payer = DrydockTest.CreatePayer(pair, map.MapId);
            server.System<BankSystem>().TryBankDeposit(payer, price * 2, tax: false);
        });
        await server.WaitRunTicks(5);

        // A crate on the pad is in the way, and named; a tool dropped there isn't.
        EntityUid crate = default, tool = default;
        await server.WaitPost(() =>
        {
            crate = entMan.SpawnEntity("CrateGenericSteel", new EntityCoordinates(map.Grid.Owner, 13.5f, 12.5f));
            tool = entMan.SpawnEntity("Wrench", new EntityCoordinates(map.Grid.Owner, 12.5f, 12.5f));
        });
        await server.WaitRunTicks(3);
        await server.WaitAssertion(() =>
        {
            var (_, status, thing) = shop.InspectPad((terminal, entMan.GetComponent<WFVehicleShopComponent>(terminal)));
            Assert.That(status, Is.EqualTo(WFVehicleShopPad.Blocked), "A crate on the pad didn't block it.");
            Assert.That(thing, Is.EqualTo(crate), "The pad named the wrong thing in the way.");
            entMan.DeleteEntity(crate);
        });
        await server.WaitRunTicks(2);

        var before = 0;
        await server.WaitAssertion(() =>
        {
            Assert.That(index, Is.GreaterThanOrEqualTo(0), "The box van is not in the expeditionary catalogue.");
            Assert.That(shop.InspectPad((terminal, entMan.GetComponent<WFVehicleShopComponent>(terminal))).Status, Is.EqualTo(WFVehicleShopPad.Clear),
                "A tool lying on the pad blocked it.");
            entMan.DeleteEntity(tool);
            before = entMan.GetComponent<BankAccountComponent>(payer).Balance;
            entMan.EventBus.RaiseLocalEvent(terminal, new WFVehicleShopBuyMessage(index) { Actor = payer });
        });
        await server.WaitRunTicks(5);

        EntityUid vehicle = default;
        await server.WaitAssertion(() =>
        {
            var (_, status, onPad) = shop.InspectPad((terminal, entMan.GetComponent<WFVehicleShopComponent>(terminal)));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(status, Is.EqualTo(WFVehicleShopPad.Vehicle), "Nothing was delivered to the pad.");
                Assert.That(onPad is { } delivered && entMan.GetComponent<MetaDataComponent>(delivered).EntityPrototype?.ID == "VehicleBoxVan", Is.True,
                    "The pad holds the wrong vehicle.");
                Assert.That(entMan.GetComponent<BankAccountComponent>(payer).Balance, Is.EqualTo(before - price), "The buyer was not charged the listed price.");
                var keyId = entMan.GetComponent<Content.Shared._WF.Vehicles.Core.VehicleLockComponent>(onPad!.Value).KeyId;
                var keys = entMan.EntityQueryEnumerator<Content.Shared._WF.Vehicles.Core.VehicleKeyComponent>();
                var handed = false;
                while (keys.MoveNext(out _, out var key))
                {
                    handed |= key.KeyId != null && key.KeyId == keyId;
                }

                Assert.That(handed, Is.True, "The buyer was given no key for the vehicle.");
            }

            vehicle = onPad!.Value;
            before = entMan.GetComponent<BankAccountComponent>(payer).Balance;

            // A second order can't land on top of the first.
            entMan.EventBus.RaiseLocalEvent(terminal, new WFVehicleShopBuyMessage(index) { Actor = payer });
        });
        await server.WaitRunTicks(5);

        var sellPrice = 0;
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<BankAccountComponent>(payer).Balance, Is.EqualTo(before), "A busy pad still took an order.");
            sellPrice = shop.SellPrice((terminal, entMan.GetComponent<WFVehicleShopComponent>(terminal)), vehicle);
            Assert.That(sellPrice, Is.InRange(1, price - 1), "A vehicle should sell back for something, and for less than it cost.");
            entMan.EventBus.RaiseLocalEvent(terminal, new WFVehicleShopSellMessage { Actor = payer });
        });
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.Deleted(vehicle), Is.True, "The sold vehicle is still there.");
                Assert.That(entMan.GetComponent<BankAccountComponent>(payer).Balance, Is.GreaterThan(before), "The seller was not paid.");
                Assert.That(entMan.GetComponent<BankAccountComponent>(payer).Balance, Is.LessThanOrEqualTo(before + sellPrice));
            }
        });

        // The window previews each vehicle by spawning it on the client.
        var previews = new List<EntityUid>();
        await pair.Client.WaitPost(() =>
        {
            foreach (var listing in catalog.Vehicles)
                previews.Add(pair.Client.EntMan.Spawn(listing.Vehicle));
        });
        await pair.RunTicksSync(5);
        await pair.Client.WaitPost(() =>
        {
            foreach (var preview in previews)
                pair.Client.EntMan.DeleteEntity(preview);
        });

        // The window builds a row per vehicle and offers only what the buyer can afford.
        await pair.Client.WaitAssertion(() =>
        {
            var window = new WFVehicleShopWindow();
            var listings = new List<WFVehicleListing>(catalog.Vehicles);
            var cheapest = listings.Min(listing => listing.Price);
            window.UpdateState(new WFVehicleShopState(listings, cheapest, WFVehicleShopPad.Clear, null, 0, false));
            window.OpenCentered();

            var buttons = window.FindControl<BoxContainer>("Listings").Children
                .SelectMany(row => row.Children.SelectMany(contents => contents.Children))
                .OfType<Button>()
                .ToList();
            using (Assert.EnterMultipleScope())
            {
                Assert.That(buttons, Has.Count.EqualTo(listings.Count), "The window should have a buy button per vehicle.");
                Assert.That(buttons.Count(button => !button.Disabled), Is.EqualTo(listings.Count(listing => listing.Price <= cheapest)),
                    "Only what the buyer can afford should be offered.");
            }

            window.Close();
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A damaged vehicle on the pad is repaired for its price, and a part bought at the terminal fits it by hand.</summary>
    [Test]
    public async Task BayRepairsAndUpgrades()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var shop = server.System<WFVehicleShopSystem>();
        var hardpoints = server.System<Content.Shared._WF.Vehicles.Hardpoint.HardpointSystem>();
        EntityUid terminal = default, pad = default, payer = default, vehicle = default;

        await server.WaitPost(() =>
        {
            var maps = server.System<SharedMapSystem>();
            var plating = server.Resolve<ITileDefinitionManager>()["Plating"].TileId;
            for (var x = -5; x < 25; x++)
            for (var y = -5; y < 25; y++)
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), new Tile(plating));

            terminal = entMan.SpawnEntity(Terminal, new EntityCoordinates(map.Grid.Owner, 3.5f, 3.5f));
            pad = entMan.SpawnEntity("WFVehicleDeliveryPad", new EntityCoordinates(map.Grid.Owner, 12.5f, 12.5f));
            vehicle = entMan.SpawnEntity("VehicleHumveeTransport", new EntityCoordinates(map.Grid.Owner, 12.5f, 12.5f));
            payer = DrydockTest.CreatePayer(pair, map.MapId);
            server.System<SharedTransformSystem>().SetCoordinates(payer, new EntityCoordinates(map.Grid.Owner, 10.5f, 12.5f));
            server.System<BankSystem>().TryBankDeposit(payer, 100000, tax: false);
        });
        await server.WaitRunTicks(5);

        var before = 0;
        var price = 0;
        await server.WaitAssertion(() =>
        {
            var ent = (terminal, entMan.GetComponent<WFVehicleShopComponent>(terminal));
            Assert.That(shop.RepairPrice(ent, vehicle), Is.Zero, "A whole vehicle shouldn't be charged for.");

            foreach (var (part, integrity) in hardpoints.GetIntegrityParts(vehicle))
                hardpoints.DamageHardpoint(vehicle, part, integrity.MaxIntegrity * 0.5f, integrity);

            price = shop.RepairPrice(ent, vehicle);
            Assert.That(price, Is.GreaterThan(0), "A damaged vehicle should cost something to repair.");
            before = entMan.GetComponent<BankAccountComponent>(payer).Balance;
            entMan.EventBus.RaiseLocalEvent(terminal, new WFVehicleShopRepairMessage { Actor = payer });
        });
        await server.WaitRunTicks(3);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(hardpoints.GetMissingIntegrity(vehicle, out _), Is.Zero, "The bay didn't repair the vehicle.");
                Assert.That(entMan.GetComponent<BankAccountComponent>(payer).Balance, Is.EqualTo(before - price), "The repair wasn't charged.");
            }

            before = entMan.GetComponent<BankAccountComponent>(payer).Balance;
            var listings = shop.Listings((terminal, entMan.GetComponent<WFVehicleShopComponent>(terminal)));
            var index = listings.FindIndex(listing => listing.Vehicle == "VehicleHumveeOverlight");
            Assert.That(index, Is.GreaterThanOrEqualTo(0), "The bay doesn't sell the overlight.");
            entMan.EventBus.RaiseLocalEvent(terminal, new WFVehicleShopBuyMessage(index) { Actor = payer });
        });
        await server.WaitRunTicks(3);

        EntityUid part = default;
        await server.WaitAssertion(() =>
        {
            var hands = server.System<Content.Shared.Hands.EntitySystems.SharedHandsSystem>();
            Assert.That(hands.GetActiveItem(payer) is { } held && entMan.GetComponent<MetaDataComponent>(held).EntityPrototype?.ID == "VehicleHumveeOverlight",
                "The bought part wasn't handed over.");
            part = hands.GetActiveItem(payer)!.Value;
            server.System<Content.Shared.Interaction.SharedInteractionSystem>().InteractUsing(payer, part, vehicle, entMan.GetComponent<TransformComponent>(vehicle).Coordinates);
        });
        await server.WaitRunTicks(pair.SecondsToTicks(3));

        await server.WaitAssertion(() =>
        {
            var slots = server.System<Content.Shared.Containers.ItemSlots.ItemSlotsSystem>();
            Assert.That(slots.TryGetSlot(vehicle, "roof", out var slot), Is.True, "The humvee has no roof slot.");
            Assert.That(slot!.Item, Is.EqualTo(part), "The part didn't fit.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>John Wolfgate sells flatpacks for both halves of a bay.</summary>
    [Test]
    public async Task WolfgateSellsTheBay()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var stock = server.ProtoMan.Index<Content.Shared.VendingMachines.VendingMachineInventoryPrototype>("WFWolfgateVendInventory").StartingInventory;
            foreach (var (flatpack, built) in new[] { ("WFVehicleBayTerminalFlatpack", "WFComputerVehicleBay"), ("WFVehicleDeliveryPadFlatpack", "WFVehicleDeliveryPad") })
            {
                Assert.That(stock, Does.ContainKey(flatpack));
                var pack = server.EntMan.SpawnEntity(flatpack, map.GridCoords);
                Assert.That(server.EntMan.GetComponent<Content.Shared.Construction.Components.FlatpackComponent>(pack).Entity?.Id, Is.EqualTo(built));
                Assert.That(server.EntMan.HasComponent<WFVehicleShopComponent>(server.EntMan.SpawnEntity("WFComputerVehicleBay", map.GridCoords)), Is.True);
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Every vessel mapped with a vehicle bay: the terminal finds a clear pad, is in reach of an APC, and the biggest
    /// vehicle on sale can get from the pad out of the hull through doors.
    /// </summary>
    [Test]
    public async Task VesselBaysWork()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var resources = server.ResolveDependency<Robust.Shared.ContentPack.IResourceManager>();
        var mapLoader = entMan.System<Robust.Shared.EntitySerialization.Systems.MapLoaderSystem>();
        var maps = entMan.System<SharedMapSystem>();
        var shop = server.System<WFVehicleShopSystem>();
        var vessels = server.ProtoMan.EnumeratePrototypes<Content.Shared._NF.Shipyard.Prototypes.VesselPrototype>()
            .Where(vessel => resources.ContentFileReadAllText(vessel.ShuttlePath).Contains(Terminal))
            .ToList();
        Assert.That(vessels, Is.Not.Empty, "No vessel carries a vehicle bay.");

        foreach (var vessel in vessels)
        {
            EntityUid mapUid = default;
            await server.WaitPost(() =>
            {
                maps.CreateMap(out var mapId);
                mapUid = maps.GetMap(mapId);
                Assert.That(mapLoader.TryLoadGrid(mapId, vessel.ShuttlePath, out _), Is.True, $"{vessel.ID} didn't load.");
            });
            await server.WaitRunTicks(5);

            await server.WaitAssertion(() =>
            {
                var query = entMan.EntityQueryEnumerator<WFVehicleShopComponent, TransformComponent>();
                while (query.MoveNext(out var terminal, out var comp, out var xform))
                {
                    if (xform.MapUid != mapUid)
                        continue;

                    var (found, status, thing) = shop.InspectPad((terminal, comp));
                    Assert.That(status, Is.EqualTo(WFVehicleShopPad.Clear),
                        $"{vessel.ID}'s pad is {status}: {(thing is { } t ? entMan.ToPrettyString(t) : "nothing")}");
                    Assert.That(entMan.GetComponent<Content.Server.Power.Components.ExtensionCableReceiverComponent>(terminal).Provider, Is.Not.Null,
                        $"{vessel.ID}'s terminal is out of reach of an APC.");

                    var footprint = shop.Footprint(shop.Listings((terminal, comp))
                        .Where(listing => !shop.IsCarried(listing.Vehicle))
                        .Select(listing => listing.Vehicle));
                    Assert.That(CanDriveOut(entMan, maps, shop, found!.Value, footprint), Is.True,
                        $"{vessel.ID}'s biggest vehicle ({footprint}) can't drive from its pad out of the hull.");
                }

                entMan.DeleteEntity(mapUid);
            });
            await server.WaitRunTicks(2);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>Walks a vehicle-wide square from the pad over the deck; doors count as open, everything else solid as solid.</summary>
    private static bool CanDriveOut(IEntityManager entMan, SharedMapSystem maps, WFVehicleShopSystem shop, EntityUid pad, Box2 footprint)
    {
        var grid = entMan.GetComponent<TransformComponent>(pad).GridUid!.Value;
        var comp = entMan.GetComponent<MapGridComponent>(grid);
        var width = (int) MathF.Ceiling(MathF.Min(footprint.Width, footprint.Height));
        var start = maps.TileIndicesFor(grid, comp, entMan.GetComponent<TransformComponent>(pad).Coordinates);

        bool Solid(Vector2i tile)
        {
            foreach (var other in maps.GetAnchoredEntities(grid, comp, tile))
            {
                if (!entMan.HasComponent<Content.Shared.Doors.Components.DoorComponent>(other) && shop.Blocks(other, pad))
                    return true;
            }

            return false;
        }

        var seen = new HashSet<Vector2i> { start };
        var open = new Queue<Vector2i>();
        open.Enqueue(start);
        while (open.TryDequeue(out var at))
        {
            var deck = 0;
            var blocked = false;
            for (var x = 0; x < width && !blocked; x++)
            for (var y = 0; y < width && !blocked; y++)
            {
                var tile = at + new Vector2i(x, y);
                if (!maps.TryGetTileRef(grid, comp, tile, out var tileRef) || tileRef.Tile.IsEmpty)
                    continue;

                deck++;
                blocked = Solid(tile);
            }

            if (blocked)
                continue;

            if (deck == 0)
                return true;

            foreach (var step in new[] { new Vector2i(1, 0), new Vector2i(-1, 0), new Vector2i(0, 1), new Vector2i(0, -1) })
            {
                if (seen.Add(at + step))
                    open.Enqueue(at + step);
            }
        }

        return false;
    }

    /// <summary>Every vehicle a catalogue lists exists and costs more than it appraises for, so none can be bought to sell at a profit.</summary>
    [Test]
    public async Task CataloguesNeverSellBelowValue()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var pricing = server.System<Content.Server.Cargo.Systems.PricingSystem>();
            using (Assert.EnterMultipleScope())
            {
                foreach (var catalog in server.ProtoMan.EnumeratePrototypes<WFVehicleCatalogPrototype>())
                foreach (var listing in catalog.Vehicles)
                {
                    Assert.That(server.ProtoMan.TryIndex<EntityPrototype>(listing.Vehicle, out var proto), Is.True, $"{catalog.ID} lists {listing.Vehicle}, which doesn't exist.");
                    if (proto == null)
                        continue;

                    Assert.That(listing.Price, Is.GreaterThan(pricing.GetEstimatedPrice(proto)),
                        $"{catalog.ID} sells {listing.Vehicle} for no more than it appraises for.");
                }
            }
        });

        await pair.CleanReturnAsync();
    }
}
