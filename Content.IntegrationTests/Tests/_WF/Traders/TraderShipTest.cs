using System;
#nullable enable
using System.Collections.Generic;
using System.Numerics;
using System.Linq;
using Content.Server._NF.SectorServices;
using Content.Server._NF.Shipyard.Systems;
using Content.Server._WF.Shipyard;
using Content.Server._WF.Traders;
using Content.Server.DeviceNetwork.Systems;
using Content.Server.Gravity;
using Content.Server.Power.Components;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Server.Station.Systems;
using Content.Server.Wires;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._Mono.Ships.Components;
using Content.Shared._NF.Shipyard;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WF.Access;
using Content.Shared._WF.Traders;
using Content.Shared.Access.Components;
using Content.Shared.Mind;
using Content.Shared.Players;
using Content.Shared.DeviceNetwork.Components;
using Content.Shared.Gravity;
using Content.Shared.Pinpointer;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Stacks;
using Content.Shared.Station.Components;
using Robust.Server.GameObjects;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Traders;

/// <summary>
/// Covers the ship-selling traders: the dialogue arguments that pick a console listing, the card a
/// trader holds while a listing is open, and the used ship round trip through YAML and back.
/// </summary>
[TestFixture]
[TestOf(typeof(UsedShipMarketSystem))]
public sealed class TraderShipTest
{
    private const string DealerProto = "WFTraderShipyard";
    private const string SalesmanProto = "WFTraderUsedShips";
    private const string DealerDialogue = "WFTraderShipyardDialogue";
    private const string ConsoleProto = "ComputerShipyard";
    private const string TableProto = "Table";
    private const string IdProto = "PassengerIDCard";
    private const string VoucherProto = "ShipVoucherFrontierService";
    private const string CustomerProto = "MobHuman";
    private const string CashProto = "SpaceCash";

    /// <summary>
    /// Index of the dealer's first BuyShip option, which fronts <see cref="ConsoleProto"/>.
    /// </summary>
    private const int BuyShipOption = 0;
    private const string MarkerProto = "Wrench";
    private const string VesselProto = "Guppy";
    private const string BorgProto = "BorgChassisGeneric";
    private const string ShipName = "Test Barge";
    private const string MarkerName = "Wolfgate resale marker";

    /// <summary>
    /// Every BuyShip option names a console the trader is actually allowed to front.
    /// </summary>
    [Test]
    public async Task DialogueArgumentsMatchConsoles()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var protoMan = server.ResolveDependency<IPrototypeManager>();
            var compFactory = server.ResolveDependency<IComponentFactory>();

            var dealer = protoMan.Index<EntityPrototype>(DealerProto);
            Assert.That(dealer.TryGetComponent<TraderShipyardComponent>(out var shipyard, compFactory), Is.True,
                $"{DealerProto} should front shipyard listings.");

            var allowed = shipyard!.Consoles.Select(listing => listing.Console.Id).ToList();
            Assert.That(allowed, Is.Not.Empty, "The dealer should have at least one console.");

            var dialogue = protoMan.Index<TraderDialoguePrototype>(DealerDialogue);
            var buyOptions = dialogue.Options.Where(o => o.Action == TraderAction.BuyShip).ToList();
            Assert.That(buyOptions, Is.Not.Empty, "The dealer should offer ships.");

            foreach (var option in buyOptions)
            {
                Assert.That(option.Argument, Is.Not.Null,
                    "A BuyShip option has to say which console it opens.");
                Assert.That(allowed, Does.Contain(option.Argument),
                    $"'{option.Argument}' is not one of the dealer's consoles.");
                Assert.That(protoMan.HasIndex<EntityPrototype>(option.Argument!), Is.True,
                    $"'{option.Argument}' is not an entity prototype.");
            }

            // Every listed console has to be a real shipyard console the trader can host.
            foreach (var id in allowed)
            {
                var console = protoMan.Index<EntityPrototype>(id);
                Assert.That(console.TryGetComponent<ShipyardConsoleComponent>(out _, compFactory), Is.True,
                    $"{id} is not a shipyard console.");
            }

            var salesman = protoMan.Index<EntityPrototype>(SalesmanProto);
            Assert.That(salesman.TryGetComponent<TraderUsedShipsComponent>(out var used, compFactory), Is.True,
                $"{SalesmanProto} should deal in used ships.");
            Assert.That(used!.UsedShipMarkup, Is.EqualTo(0.05f).Within(0.0001f));
            Assert.That(used.RelistDelay, Is.EqualTo(TimeSpan.FromMinutes(1)));
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A card taken for a listing goes into the console's slot, is locked in, and comes back to the
    /// table when the conversation ends - even after the listing has been torn down.
    /// </summary>
    [Test]
    public async Task HeldIdComesBack()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        var entMan = server.EntMan;
        var mapSys = entMan.System<SharedMapSystem>();
        var traderSys = entMan.System<TraderSystem>();
        var shipyardSys = entMan.System<ShipyardSystem>();

        var gridUid = map.Grid.Owner;

        await server.WaitAssertion(() =>
        {
            mapSys.SetTile(gridUid, map.Grid.Comp, new Vector2i(0, -1), map.Tile.Tile);

            var trader = entMan.SpawnEntity(DealerProto, new EntityCoordinates(gridUid, 0.5f, 0.5f));
            entMan.SpawnEntity(TableProto, new EntityCoordinates(gridUid, 0.5f, -0.5f));

            var comp = entMan.GetComponent<TraderComponent>(trader);
            traderSys.RefreshTable((trader, comp));
            Assert.That(comp.Table, Is.Not.Null, "The trader should have found its table.");

            var idCard = entMan.SpawnEntity(IdProto, new EntityCoordinates(gridUid, 0.5f, -0.5f));
            Assert.That(traderSys.GetZoneItems((trader, comp)), Does.Contain(idCard),
                "The card should start on the table.");

            // No owner stamp, so the trader is allowed to pick it up for the customer.
            Assert.That(traderSys.TryHoldItem((trader, comp), idCard), Is.True);
            Assert.That(traderSys.GetZoneItems((trader, comp)), Does.Not.Contain(idCard),
                "The trader should be holding the card.");

            Assert.That(shipyardSys.TryHostConsole(trader, ConsoleProto, out var uiKey), Is.True,
                "The trader should be able to front a shipyard console.");
            Assert.That(uiKey, Is.EqualTo(ShipyardConsoleUiKey.Shipyard));
            Assert.That(entMan.HasComponent<ShipyardConsoleComponent>(trader), Is.True,
                "The console's own component should be on the trader.");

            Assert.That(shipyardSys.TryInsertHostedId(trader, idCard), Is.True,
                "The card should go into the console's slot.");

            var console = entMan.GetComponent<ShipyardConsoleComponent>(trader);
            Assert.That(console.TargetIdSlot.Item, Is.EqualTo(idCard));
            Assert.That(console.TargetIdSlot.Locked, Is.True,
                "The customer must not be able to pull the card back out of the NPC.");

            comp.Customer = trader;
            traderSys.EndConversation((trader, comp), farewell: false);

            Assert.That(entMan.Deleted(idCard), Is.False, "The card must never be lost.");
            Assert.That(entMan.GetComponent<TransformComponent>(idCard).ParentUid, Is.EqualTo(gridUid),
                "The card should be back out on the table.");
            Assert.That(traderSys.GetZoneItems((trader, comp)), Does.Contain(idCard),
                "The card should be back in the barter zone.");
            Assert.That(entMan.HasComponent<ShipyardConsoleComponent>(trader), Is.False,
                "The hosted listing should have been torn down with the conversation.");

            // The salesman hosts a console too, for the sale rules it buys under.
            var salesman = entMan.SpawnEntity(SalesmanProto, new EntityCoordinates(gridUid, 1.5f, 0.5f));
            var used = entMan.GetComponent<TraderUsedShipsComponent>(salesman);
            Assert.That(shipyardSys.TryHostConsole(salesman, used.Console, out _), Is.True,
                "The salesman should be able to host the console it buys under.");
            shipyardSys.ClearHostedConsole(salesman);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The dealer takes a ship voucher in place of a card, prefers it over one, gives back an unspent
    /// one, survives a spent one being deleted out of the slot, and picks a refused option back up by
    /// itself once what it asked for lands on the table.
    /// </summary>
    [Test]
    public async Task VoucherAndPendingBuyShip()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        var entMan = server.EntMan;
        var mapSys = entMan.System<SharedMapSystem>();
        var mindSys = entMan.System<SharedMindSystem>();
        var traderSys = entMan.System<TraderSystem>();

        var gridUid = map.Grid.Owner;

        Assert.That(pair.Client.Session, Is.Not.Null, "This test needs a connected pair.");
        var session = server.PlayerMan.GetSessionById(pair.Client.Session!.UserId);

        var trader = EntityUid.Invalid;
        var customer = EntityUid.Invalid;
        var voucher = EntityUid.Invalid;

        // A customer with a real session: an ID only counts as theirs against a player.
        await server.WaitPost(() =>
        {
            mapSys.SetTile(gridUid, map.Grid.Comp, new Vector2i(0, -1), map.Tile.Tile);
            mapSys.SetTile(gridUid, map.Grid.Comp, new Vector2i(1, 0), map.Tile.Tile);

            trader = entMan.SpawnEntity(DealerProto, new EntityCoordinates(gridUid, 0.5f, 0.5f));
            entMan.SpawnEntity(TableProto, new EntityCoordinates(gridUid, 0.5f, -0.5f));

            mindSys.WipeMind(session.ContentData()?.Mind);
            customer = entMan.SpawnEntity(CustomerProto, new EntityCoordinates(gridUid, 1.5f, 0.5f));
            var mind = mindSys.CreateMind(session.UserId).Owner;
            mindSys.TransferTo(mind, customer);
        });

        await pair.RunTicksSync(5);

        Assert.That(session.AttachedEntity, Is.EqualTo(customer), "The customer should be the player's body.");

        // A voucher and a card on the table: the voucher is what the customer came to redeem.
        await server.WaitPost(() =>
        {
            var comp = entMan.GetComponent<TraderComponent>(trader);
            traderSys.RefreshTable((trader, comp));

            entMan.SpawnEntity(IdProto, new EntityCoordinates(gridUid, 0.5f, -0.5f));
            voucher = entMan.SpawnEntity(VoucherProto, new EntityCoordinates(gridUid, 0.5f, -0.5f));

            Assert.That(traderSys.TryStartConversation((trader, comp), customer), Is.True,
                "The dealer should have started talking.");

            SelectOption(entMan, trader, customer, BuyShipOption);
        });

        await pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.TryGetComponent<ShipyardConsoleComponent>(trader, out var console), Is.True,
                "A voucher should have got the listing open.");
            Assert.That(console!.TargetIdSlot.Item, Is.EqualTo(voucher),
                "The voucher, not the card, should be in the console's slot.");
            Assert.That(console.TargetIdSlot.Locked, Is.True);
            Assert.That(entMan.GetComponent<TraderShipyardComponent>(trader).ActiveKey,
                Is.EqualTo(ShipyardConsoleUiKey.Shipyard));
        });

        // Ending the conversation hands an unspent voucher straight back.
        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<TraderComponent>(trader);
            traderSys.EndConversation((trader, comp), farewell: false);

            Assert.That(entMan.Deleted(voucher), Is.False, "An unspent voucher must never be destroyed.");
            Assert.That(traderSys.GetZoneItems((trader, comp)), Does.Contain(voucher),
                "The voucher should be back on the table.");
            Assert.That(entMan.HasComponent<ShipyardConsoleComponent>(trader), Is.False);
        });

        // A voucher the console spends is deleted under the trader; handing items back must cope.
        await server.WaitPost(() =>
        {
            var comp = entMan.GetComponent<TraderComponent>(trader);
            Assert.That(traderSys.TryStartConversation((trader, comp), customer), Is.True);
            SelectOption(entMan, trader, customer, BuyShipOption);
        });

        await pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<TraderComponent>(trader);
            Assert.That(comp.Held, Does.Contain(voucher), "The trader should be holding the voucher.");

            entMan.DeleteEntity(voucher);

            Assert.DoesNotThrow(() => traderSys.EndConversation((trader, comp), farewell: false),
                "A voucher spent inside the console must not break the handback.");
            Assert.That(comp.Held, Is.Empty);
            Assert.That(entMan.HasComponent<ShipyardConsoleComponent>(trader), Is.False);
        });

        // Nothing on the table: the option is asked for rather than refused, and waits.
        await server.WaitPost(() =>
        {
            var comp = entMan.GetComponent<TraderComponent>(trader);
            foreach (var uid in traderSys.GetZoneItems((trader, comp)))
                entMan.DeleteEntity(uid);
        });

        await pair.RunTicksSync(2);

        await server.WaitPost(() =>
        {
            var comp = entMan.GetComponent<TraderComponent>(trader);
            Assert.That(traderSys.GetZoneItems((trader, comp)), Is.Empty, "The table should be clear.");

            Assert.That(traderSys.TryStartConversation((trader, comp), customer), Is.True);
            SelectOption(entMan, trader, customer, BuyShipOption);
        });

        await pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<TraderComponent>(trader);
            Assert.That(comp.PendingOption, Is.EqualTo(BuyShipOption), "The dealer should be waiting on a card.");
            Assert.That(entMan.HasComponent<ShipyardConsoleComponent>(trader), Is.False,
                "Nothing should have opened without a card.");
        });

        // The customer puts their own card down; the dealer carries on by itself.
        await server.WaitPost(() =>
        {
            var idCard = entMan.SpawnEntity(IdProto, new EntityCoordinates(gridUid, 0.5f, -0.5f));
            entMan.EnsureComponent<IdCardOwnerComponent>(idCard).UserId = session.UserId;
        });

        await pair.RunTicksSync(150);

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<TraderComponent>(trader);
            Assert.That(comp.PendingOption, Is.Null, "The waiting option should have run.");
            Assert.That(entMan.TryGetComponent<ShipyardConsoleComponent>(trader, out var console), Is.True,
                "The listing should have opened as soon as the card arrived.");
            Assert.That(console!.TargetIdSlot.Item, Is.Not.Null, "The card should be in the console's slot.");
            Assert.That(entMan.GetComponent<TraderShipyardComponent>(trader).ActiveKey,
                Is.EqualTo(ShipyardConsoleUiKey.Shipyard));

            traderSys.EndConversation((trader, comp), farewell: false);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Sends the dialogue message a customer's window would send, without a client behind it.
    /// </summary>
    private static void SelectOption(IEntityManager entMan, EntityUid trader, EntityUid customer, int index)
    {
        var msg = new TraderDialogueSelectMessage(index)
        {
            Actor = customer,
            UiKey = TraderUiKey.Dialogue,
        };

        entMan.EventBus.RaiseLocalEvent(trader, msg);
    }

    /// <summary>
    /// A ship is bought back, kept as YAML, put on the lot after the delay, and comes back out with
    /// its cargo and its name.
    /// </summary>
    [Test]
    public async Task UsedShipRoundTrip()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        var entMan = server.EntMan;
        var protoMan = server.ResolveDependency<IPrototypeManager>();

        var mapLoader = entMan.System<MapLoaderSystem>();
        var metaSys = entMan.System<MetaDataSystem>();
        var stackSys = entMan.System<SharedStackSystem>();
        var shipyardSys = entMan.System<ShipyardSystem>();
        var marketSys = entMan.System<UsedShipMarketSystem>();

        var gridUid = map.Grid.Owner;
        var shuttle = EntityUid.Invalid;
        var console = EntityUid.Invalid;
        var station = EntityUid.Invalid;
        UsedShipListing? listing = null;
        var appraisal = 0;

        // Load a real vessel, brand it as a bought ship and drop a marker aboard.
        await server.WaitAssertion(() =>
        {
            var vessel = protoMan.Index<VesselPrototype>(VesselProto);

            Assert.That(mapLoader.TryLoadGrid(map.MapId, vessel.ShuttlePath, out var grid), Is.True,
                $"Could not load {vessel.ShuttlePath}.");

            shuttle = grid!.Value.Owner;
            metaSys.SetEntityName(shuttle, ShipName);
            entMan.EnsureComponent<VesselComponent>(shuttle).VesselId = VesselProto;

            // Somewhere over the hull, or grid traversal drops the marker onto the map instead.
            var deck = entMan.GetComponent<MapGridComponent>(shuttle).LocalAABB.Center;
            var marker = entMan.SpawnEntity(CashProto, new EntityCoordinates(shuttle, deck));
            metaSys.SetEntityName(marker, MarkerName);
            Assert.That(MarkersAboard(entMan, shuttle), Is.EqualTo(1),
                "The marker should be aboard before the ship is bought back.");

            console = entMan.SpawnEntity(ConsoleProto, new EntityCoordinates(gridUid, 0.5f, 0.5f));
            appraisal = shipyardSys.GetShipAppraisal(shuttle);
            Assert.That(appraisal, Is.GreaterThan(0), "The vessel should appraise for something.");
        });

        // Capture it the way a sale at a marked station would, without the docking rules.
        await server.WaitAssertion(() =>
        {
            Assert.That(marketSys.TryCapture(shuttle, console, appraisal,
                    UsedShipMarketSystem.DefaultMarkup, UsedShipMarketSystem.DefaultRelistDelay, out listing),
                Is.True, "The sold ship should have been copied.");

            Assert.That(listing!.ShipName, Is.EqualTo(ShipName), "The listing should keep the ship's name.");
            Assert.That(listing.DesignId?.Id, Is.EqualTo(VesselProto), "The listing should remember the design.");
            Assert.That(listing.DesignName, Is.EqualTo(protoMan.Index<VesselPrototype>(VesselProto).Name));
            Assert.That(listing.Data, Is.Not.Empty, "The grid should have been serialised.");

            Assert.That(listing.SaleValue, Is.EqualTo(shipyardSys.GetPostSaleRateBill(console, appraisal)),
                "The listing should record the post-rate, pre-tax bill.");
            Assert.That(listing.Price,
                Is.EqualTo((int) Math.Ceiling(listing.SaleValue * (1f + UsedShipMarketSystem.DefaultMarkup))),
                "The lot price should be the sale value plus the markup.");

            Assert.That(listing.AvailableAt - listing.SoldAt, Is.EqualTo(UsedShipMarketSystem.DefaultRelistDelay),
                "A bought ship should sit out back for the relist delay.");
            Assert.That(marketSys.GetAvailable(), Does.Not.Contain(listing),
                "It should not be on the lot yet.");

            // The old owner's bindings must not survive the copy.
            Assert.That(entMan.HasComponent<ShuttleDeedComponent>(shuttle), Is.False,
                "The deed should have been stripped before the copy.");

            // Stand in for five minutes passing.
            listing.AvailableAt = listing.SoldAt;
            Assert.That(marketSys.GetAvailable(), Does.Contain(listing), "It should be on the lot now.");

            entMan.DeleteEntity(shuttle);
        });

        await pair.RunTicksSync(2);

        // Buy it: load the copy and dock it to a station, then take it off the lot.
        var bought = EntityUid.Invalid;

        await server.WaitAssertion(() =>
        {
            station = entMan.Spawn();
            entMan.EnsureComponent<StationDataComponent>(station);
            entMan.System<StationSystem>().AddGridToStation(station, gridUid);

            Assert.That(marketSys.TryLoadListing(listing!, station, out var loaded), Is.True,
                "The saved ship should load back in.");

            bought = loaded!.Value;
            Assert.That(entMan.Deleted(bought), Is.False);
            Assert.That(entMan.HasComponent<MapGridComponent>(bought), Is.True,
                "What came back should be a grid.");
            Assert.That(entMan.GetComponent<MetaDataComponent>(bought).EntityName, Is.EqualTo(ShipName),
                "The resold ship should keep its name.");

            Assert.That(MarkersAboard(entMan, bought), Is.EqualTo(1),
                "The marker should still be aboard the resold ship.");

            marketSys.RemoveListing(listing!);
            Assert.That(marketSys.Listings, Does.Not.Contain(listing), "A sold listing should be off the lot.");
            Assert.That(marketSys.GetAvailable(), Is.Empty);

            entMan.DeleteEntity(bought);
            entMan.DeleteEntity(station);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// How many marker items are somewhere aboard a grid.
    /// </summary>
    /// <summary>
    /// The real thing: a ship docked to a marked station is sold through the console path, and the
    /// copy the market took must load and dock again.
    /// </summary>
    [Test]
    public async Task DockedSaleReloads()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        var entMan = server.EntMan;
        var protoMan = server.ResolveDependency<IPrototypeManager>();
        var mapLoader = entMan.System<MapLoaderSystem>();
        var metaSys = entMan.System<MetaDataSystem>();
        var shipyardSys = entMan.System<ShipyardSystem>();
        var marketSys = entMan.System<UsedShipMarketSystem>();
        var shuttleSys = entMan.System<ShuttleSystem>();
        var stationSys = entMan.System<StationSystem>();

        var station = EntityUid.Invalid;
        var dockGrid = EntityUid.Invalid;
        var shuttle = EntityUid.Invalid;
        var console = EntityUid.Invalid;
        var listingsBefore = 0;

        await server.WaitAssertion(() =>
        {
            var vessel = protoMan.Index<VesselPrototype>(VesselProto);

            // One hull stands in for the station (it has docks), a second is the ship being sold.
            Assert.That(mapLoader.TryLoadGrid(map.MapId, vessel.ShuttlePath, out var stationHull), Is.True);
            Assert.That(mapLoader.TryLoadGrid(map.MapId, vessel.ShuttlePath, out var sold, offset: new Vector2(60, 0)), Is.True);
            dockGrid = stationHull!.Value.Owner;
            shuttle = sold!.Value.Owner;

            station = entMan.Spawn();
            entMan.EnsureComponent<StationDataComponent>(station);
            entMan.EnsureComponent<UsedShipMarketComponent>(station);
            // The sale's last step writes a sector shuttle record; the host component gives it a sector entity.
            entMan.EnsureComponent<StationSectorServiceHostComponent>(station);
            stationSys.AddGridToStation(station, dockGrid);

            metaSys.SetEntityName(shuttle, ShipName);
            entMan.EnsureComponent<VesselComponent>(shuttle).VesselId = VesselProto;
            var deck = entMan.GetComponent<MapGridComponent>(shuttle).LocalAABB.Center;
            // Not cash: the sale teleports preserve-on-sale items (cash, IDs) off the hull first.
            var marker = entMan.SpawnEntity(MarkerProto, new EntityCoordinates(shuttle, deck));
            metaSys.SetEntityName(marker, MarkerName);

            // Dent the marker so the copy has damage to keep.
            var damageSys = entMan.System<DamageableSystem>();
            entMan.EnsureComponent<DamageableComponent>(marker);
            damageSys.TryChangeDamage(marker, new DamageSpecifier(protoMan.Index<DamageTypePrototype>("Blunt"), 7), true);
            Assert.That(entMan.GetComponent<DamageableComponent>(marker).TotalDamage, Is.EqualTo(FixedPoint2.New(7)));

            // A ship that was running when it was sold: force the gravity generator's receiver powered
            // so its charge tops out and the hull really has gravity before it is copied.
            foreach (var uid in Descendants(entMan, shuttle))
            {
                if (entMan.HasComponent<GravityGeneratorComponent>(uid)
                    && entMan.TryGetComponent<ApcPowerReceiverComponent>(uid, out var receiver))
                {
                    receiver.NeedsPower = false;
                }
            }

            var shuttleComp = entMan.GetComponent<ShuttleComponent>(shuttle);
            Assert.That(shuttleSys.TryFTLDock(shuttle, shuttleComp, dockGrid), Is.True, "The ship should dock to the station hull.");

            console = entMan.SpawnEntity(ConsoleProto, new EntityCoordinates(dockGrid, 0.5f, 0.5f));
            listingsBefore = marketSys.Listings.Count;
        });

        // The power net and the generator's charge loop take a few ticks to settle; how many depends on the runner.
        for (var i = 0; i < 40 && !entMan.GetComponent<GravityComponent>(shuttle).Enabled; i++)
            await pair.RunTicksSync(10);

        // Anything the map loader will not write out has to stop the sale rather than be destroyed by it.
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<GravityComponent>(shuttle).Enabled, Is.True,
                "The ship should have gravity before it is sold.");

            var deck = entMan.GetComponent<MapGridComponent>(shuttle).LocalAABB.Center;
            Assert.That(marketSys.GetUnsavableAboard(shuttle), Is.Empty,
                "A stock hull, humming computers and all, should be sellable.");

            var borg = entMan.SpawnEntity(BorgProto, new EntityCoordinates(shuttle, deck));

            Assert.That(marketSys.GetUnsavableAboard(shuttle), Does.Contain(borg),
                "A borg aboard should block the sale.");

            entMan.DeleteEntity(borg);

            var leftover = marketSys.GetUnsavableAboard(shuttle);
            Assert.That(leftover, Is.Empty,
                $"Nothing else on a stock hull should be unsavable: {string.Join(", ", leftover.Select(uid => entMan.ToPrettyString(uid).ToString()))}");
        });

        // Docking settles over a few ticks; wait for it rather than assuming a count.
        for (var i = 0; i < 40 && !IsDockedTo(entMan, shuttle, dockGrid); i++)
            await pair.RunTicksSync(5);

        UsedShipListing? listing = null;

        await server.WaitAssertion(() =>
        {
            Assert.That(IsDockedTo(entMan, shuttle, dockGrid), Is.True, "The ship should be docked before it is sold.");
            shipyardSys.SetupShipyardIfNeeded();

            var result = shipyardSys.TrySellShuttle(station, shuttle, console, out _);
            Assert.That(result.Error, Is.EqualTo(ShipyardSystem.ShipyardSaleError.Success), $"Sale failed: {result.Error}");

            Assert.That(marketSys.Listings.Count, Is.EqualTo(listingsBefore + 1), "The sale should have been captured.");
            listing = marketSys.Listings[^1];
            Assert.That(listing.ShipName, Is.EqualTo(ShipName));
        });

        for (var i = 0; i < 40 && !entMan.Deleted(shuttle); i++)
            await pair.RunTicksSync(5);

        var bought = EntityUid.Invalid;

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(shuttle), Is.True, "The sold hull should be gone.");

            listing!.AvailableAt = listing.SoldAt;
            Assert.That(marketSys.TryLoadListing(listing, station, out var loaded), Is.True,
                "The copy of a really sold ship should load back in.");

            bought = loaded!.Value;
            Assert.That(entMan.GetComponent<MetaDataComponent>(bought).EntityName, Is.EqualTo(ShipName));
            Assert.That(MarkersAboard(entMan, bought), Is.EqualTo(1), "The cargo should still be aboard.");
            var boughtMarker = Descendants(entMan, bought)
                .First(uid => entMan.GetComponent<MetaDataComponent>(uid).EntityName == MarkerName);
            Assert.That(entMan.GetComponent<DamageableComponent>(boughtMarker).TotalDamage, Is.EqualTo(FixedPoint2.New(7)),
                "Damage should survive the resale copy.");
            Assert.That(entMan.HasComponent<ShuttleDeedComponent>(bought), Is.False, "The old deed must not come back.");

            // The grid never map-inits again, so the nav map has to be rebuilt by hand.
            Assert.That(entMan.TryGetComponent<NavMapComponent>(bought, out var navMap), Is.True,
                "The resold hull should have nav map data for its consoles.");
            Assert.That(navMap!.Chunks, Is.Not.Empty, "The nav map should have been filled from the tiles.");

            Assert.That(Descendants(entMan, bought).Any(entMan.HasComponent<ShipShieldComponent>), Is.False,
                "The seller's shield bubble must not come back with the hull.");

            var deviceSys = entMan.System<DeviceNetworkSystem>();
            var connected = Descendants(entMan, bought).Count(uid =>
                entMan.TryGetComponent<DeviceNetworkComponent>(uid, out var device)
                && deviceSys.IsDeviceConnected(uid, device));
            Assert.That(connected, Is.GreaterThan(0), "The hull's devices should be back on a device net.");

            var wired = Descendants(entMan, bought).Count(uid =>
                entMan.TryGetComponent<WiresComponent>(uid, out var wires) && wires.WiresList.Count > 0);
            Assert.That(wired, Is.GreaterThan(0), "The hull's boards should have wires again.");
        });

        await pair.RunTicksSync(15);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<GravityComponent>(bought).Enabled, Is.True,
                "A ship sold with a running generator should come back with its gravity on.");

            marketSys.RemoveListing(listing!);
            entMan.DeleteEntity(bought);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Whether any dock on the shuttle is joined to a dock on the other grid.
    /// </summary>
    private static bool IsDockedTo(IEntityManager entMan, EntityUid shuttle, EntityUid other)
    {
        var query = entMan.EntityQueryEnumerator<DockingComponent, TransformComponent>();
        while (query.MoveNext(out _, out var dock, out var xform))
        {
            if (xform.GridUid != shuttle || dock.DockedWith is not { } target)
                continue;

            if (entMan.GetComponent<TransformComponent>(target).GridUid == other)
                return true;
        }

        return false;
    }

    private static int MarkersAboard(IEntityManager entMan, EntityUid grid)
    {
        return Descendants(entMan, grid)
            .Count(uid => entMan.GetComponent<MetaDataComponent>(uid).EntityName == MarkerName);
    }

    /// <summary>
    /// Everything parented to an entity, however deeply.
    /// </summary>
    private static List<EntityUid> Descendants(IEntityManager entMan, EntityUid root)
    {
        var found = new List<EntityUid>();
        var pending = new Queue<EntityUid>();
        pending.Enqueue(root);

        while (pending.TryDequeue(out var uid))
        {
            var enumerator = entMan.GetComponent<TransformComponent>(uid).ChildEnumerator;
            while (enumerator.MoveNext(out var child))
            {
                found.Add(child);
                pending.Enqueue(child);
            }
        }

        return found;
    }
}
