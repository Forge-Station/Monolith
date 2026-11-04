#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Tests._WF.Drydock;
using Content.Server._NF.Bank;
using Content.Server._WF.MilitaryVehicles;
using Content.Server._WF.VehicleShop;
using Content.Server.Cargo.Systems;
using Content.Shared._NF.Bank.Components;
using Content.Shared._WF.MilitaryVehicles;
using Content.Shared._WF.VehicleShop;
using Content.Shared._WF.Vehicles.Base;
using Content.Shared._WF.Vehicles.Hardpoint;
using Content.Shared.Prototypes;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.MilitaryVehicles;

/// <summary>
/// Nothing a vehicle terminal sells resells at a profit, the licence gates military listings, and weapons fit military
/// chassis only.
/// </summary>
[TestFixture]
[TestOf(typeof(WFMilitaryHardpointSystem))]
public sealed class MilitaryVehiclesTest
{
    private const string Counter = "WFComputerMilitaryCounter";

    /// <summary>Every military listing and every carried listing, spawned whole with what it holds, appraises for less than its price.</summary>
    [Test]
    public async Task NothingResellsAboveItsPrice()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var pricing = server.System<PricingSystem>();
            var shop = server.System<WFVehicleShopSystem>();
            using (Assert.EnterMultipleScope())
            {
                var x = 0f;
                foreach (var catalog in server.ProtoMan.EnumeratePrototypes<WFVehicleCatalogPrototype>())
                foreach (var listing in catalog.Vehicles)
                {
                    if (!server.ProtoMan.HasIndex<EntityPrototype>(listing.Vehicle))
                    {
                        Assert.Fail($"{catalog.ID} lists {listing.Vehicle}, which doesn't exist.");
                        continue;
                    }

                    // Civilian vehicles carry nothing of value and VehicleShopTest covers them by prototype.
                    if (!catalog.ID.StartsWith("WFMilitary") && !shop.IsCarried(listing.Vehicle))
                        continue;

                    var spawned = entMan.SpawnEntity(listing.Vehicle, new MapCoordinates(x, 200f, map.MapId));
                    x += 20f;
                    var value = pricing.GetPrice(spawned);
                    Assert.That(listing.Price, Is.GreaterThan(value),
                        $"{catalog.ID} sells {listing.Vehicle} for {listing.Price}, no more than it appraises for ({value}).");
                    entMan.DeleteEntity(spawned);
                }
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The counter refuses a gun to someone without a licence, sells them one made out to them, then sells the gun.</summary>
    [Test]
    public async Task LicenceGatesMilitaryListings()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var shop = server.System<WFVehicleShopSystem>();
        EntityUid terminal = default, payer = default;
        List<WFVehicleListing> listings = new();

        await server.WaitPost(() =>
        {
            terminal = entMan.SpawnEntity(Counter, new EntityCoordinates(map.Grid.Owner, 0.5f, 0.5f));
            payer = DrydockTest.CreatePayer(pair, map.MapId);
            server.System<BankSystem>().TryBankDeposit(payer, 200000, tax: false);
            listings = shop.Listings((terminal, entMan.GetComponent<WFVehicleShopComponent>(terminal)));
        });
        await server.WaitRunTicks(5);

        var licenceIndex = listings.FindIndex(listing => listing.Vehicle == "WFHeavyVehicleLicence");
        var gunIndex = listings.FindIndex(listing => listing.Vehicle == "VehicleTankLTBCannon");
        var armourIndex = listings.FindIndex(listing => listing.Vehicle == "VehicleTankArmorBallistic");
        var before = 0;

        // Armour needs no licence; the cannon does.
        await server.WaitAssertion(() =>
        {
            Assert.That(licenceIndex, Is.GreaterThanOrEqualTo(0), "The counter doesn't sell the licence.");
            Assert.That(gunIndex, Is.GreaterThanOrEqualTo(0), "The counter doesn't sell the LTB cannon.");
            Assert.That(armourIndex, Is.GreaterThanOrEqualTo(0), "The counter doesn't sell ballistic plating.");
            Assert.That(listings[gunIndex].Licence, Is.True, "The cannon is sold without a licence.");
            Assert.That(shop.HasLicence(payer), Is.False, "The payer counts as licensed with no licence.");
            before = entMan.GetComponent<BankAccountComponent>(payer).Balance;
            entMan.EventBus.RaiseLocalEvent(terminal, new WFVehicleShopBuyMessage(gunIndex) { Actor = payer });
            entMan.EventBus.RaiseLocalEvent(terminal, new WFVehicleShopBuyMessage(armourIndex) { Actor = payer });
        });
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.GetComponent<BankAccountComponent>(payer).Balance, Is.EqualTo(before - listings[armourIndex].Price),
                    "The unlicensed payer should be charged for the armour only.");
                Assert.That(Count("VehicleTankLTBCannon"), Is.Zero, "A cannon was handed to an unlicensed payer.");
                Assert.That(Count("VehicleTankArmorBallistic"), Is.EqualTo(1), "The armour wasn't handed over.");
            }

            // Drop the armour so both hands are free.
            foreach (var armour in Find("VehicleTankArmorBallistic"))
                entMan.DeleteEntity(armour);

            before = entMan.GetComponent<BankAccountComponent>(payer).Balance;
            entMan.EventBus.RaiseLocalEvent(terminal, new WFVehicleShopBuyMessage(licenceIndex) { Actor = payer });
        });
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            var licence = Find("WFHeavyVehicleLicence").Single();
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.GetComponent<BankAccountComponent>(payer).Balance, Is.EqualTo(before - listings[licenceIndex].Price));
                Assert.That(entMan.GetComponent<WFHeavyVehicleLicenceComponent>(licence).Holder, Is.EqualTo(entMan.GetComponent<MetaDataComponent>(payer).EntityName),
                    "The licence isn't made out to its buyer.");
                Assert.That(shop.HasLicence(payer), Is.True, "The licence in hand doesn't count.");
            }

            before = entMan.GetComponent<BankAccountComponent>(payer).Balance;
            entMan.EventBus.RaiseLocalEvent(terminal, new WFVehicleShopBuyMessage(gunIndex) { Actor = payer });
        });
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<BankAccountComponent>(payer).Balance, Is.EqualTo(before - listings[gunIndex].Price),
                "The licensed payer wasn't sold the cannon.");
            Assert.That(Count("VehicleTankLTBCannon"), Is.EqualTo(1), "The cannon wasn't handed over.");

            // Faction crew start with one, made out to them.
            var deputy = entMan.SpawnEntity("MobHuman", new MapCoordinates(5, 5, map.MapId));
            var issued = server.System<WFHeavyVehicleLicenceSystem>().IssueForJob(deputy, "Deputy", "Test Deputy");
            Assert.That(issued, Is.Not.Null, "A deputy wasn't issued a licence.");
            Assert.That(shop.HasLicence(deputy), Is.True, "The deputy doesn't carry the issued licence.");
            Assert.That(server.System<WFHeavyVehicleLicenceSystem>().IssueForJob(deputy, "Contractor", "Test Deputy"), Is.Null,
                "A contractor was issued a licence.");
        });

        await pair.CleanReturnAsync();
        return;

        List<EntityUid> Find(string proto)
        {
            var found = new List<EntityUid>();
            var query = entMan.EntityQueryEnumerator<MetaDataComponent>();
            while (query.MoveNext(out var uid, out var meta))
            {
                if (meta.EntityPrototype?.ID == proto)
                    found.Add(uid);
            }

            return found;
        }

        int Count(string proto) => Find(proto).Count;
    }

    /// <summary>
    /// No vehicle without the military mark has a slot a weapon could fill, and the fitting rule refuses a weapon on a
    /// civilian vehicle while letting other parts and military chassis through.
    /// </summary>
    [Test]
    public async Task WeaponsFitMilitaryChassisOnly()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var rule = server.System<WFMilitaryHardpointSystem>();
        var factory = entMan.ComponentFactory;

        await server.WaitAssertion(() =>
        {
            var weapons = new List<(string Id, HardpointItemComponent Item)>();
            foreach (var proto in server.ProtoMan.EnumeratePrototypes<EntityPrototype>())
            {
                if (proto.Abstract || !proto.TryGetComponent<HardpointItemComponent>(out var item, factory) || !rule.IsWeapon(proto))
                    continue;

                weapons.Add((proto.ID, item));
            }

            Assert.That(weapons, Is.Not.Empty);
            using (Assert.EnterMultipleScope())
            {
                foreach (var proto in server.ProtoMan.EnumeratePrototypes<EntityPrototype>())
                {
                    if (proto.Abstract
                        || !proto.HasComponent<RMCVehicleComponent>(factory)
                        || proto.HasComponent<WFMilitaryVehicleComponent>(factory)
                        || !proto.TryGetComponent<HardpointSlotsComponent>(out var slots, factory))
                        continue;

                    foreach (var slot in slots.Slots)
                    foreach (var (id, item) in weapons)
                    {
                        var fits = (slots.VehicleFamily == null || item.VehicleFamily == slots.VehicleFamily)
                                   && (slot.SlotType == null || item.SlotType == slot.SlotType)
                                   && item.CompatibilityId == slot.CompatibilityId
                                   && (slot.HardpointType == default || item.HardpointType == slot.HardpointType);
                        Assert.That(fits, Is.False, $"Civilian {proto.ID} has a slot ({slot.Id}) that takes the weapon {id}.");
                    }
                }
            }

            var civilian = entMan.SpawnEntity("VehicleHumveeTransport", new MapCoordinates(0, 200, map.MapId));
            var military = entMan.SpawnEntity("WFMilitaryChassisHumvee", new MapCoordinates(30, 200, map.MapId));
            var cannon = entMan.SpawnEntity("VehicleHumveeCannon", new MapCoordinates(0, 220, map.MapId));
            var plating = entMan.SpawnEntity("VehicleTankArmorBallistic", new MapCoordinates(0, 221, map.MapId));
            var fit = new HardpointFitAttemptEvent(cannon, cannon);
            entMan.EventBus.RaiseLocalEvent(civilian, ref fit);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(fit.Cancelled, Is.True, "A cannon was let onto a civilian humvee.");
                Assert.That(rule.CanFit(military, cannon), Is.True, "A cannon was refused on a military chassis.");
                Assert.That(rule.CanFit(civilian, plating), Is.True, "Armour was refused on a civilian vehicle.");
                Assert.That(rule.IsWeapon(plating), Is.False, "Armour plating counts as a weapon.");
                Assert.That(rule.IsWeapon(cannon), Is.True, "The humvee cannon doesn't count as a weapon.");
            }

            entMan.DeleteEntity(civilian);
            entMan.DeleteEntity(military);
            entMan.DeleteEntity(cannon);
            entMan.DeleteEntity(plating);
        });

        await pair.CleanReturnAsync();
    }
}
