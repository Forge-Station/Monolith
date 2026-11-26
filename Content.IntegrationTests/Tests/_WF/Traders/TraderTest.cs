#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._WF.Traders;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Ame.Components;
using Content.Server.Ame.Components;
using Content.Server.Cargo.Systems;
using Content.Shared._NF.Bank.Components;
using Content.Shared._WF.Traders;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Clothing.Components;
using Content.Shared.Cuffs.Components;
using Content.Shared.Damage.Components;
using Content.Shared.FixedPoint;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Interaction.Components;
using Content.Shared.Inventory;
using Content.Shared.Materials;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Paper;
using Content.Shared.Stacks;
using Content.Shared.Strip.Components;
using Content.Shared.VendingMachines;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.IntegrationTests.Tests._WF.Traders;

/// <summary>
/// Covers the trader's change splitter, its prototype wiring, table detection, a cash purchase
/// and the refuelling quote.
/// </summary>
[TestFixture]
[TestOf(typeof(TraderSystem))]
public sealed class TraderTest
{
    private const string TraderProto = "WFTraderFuelTechnician";
    private const string VendorProto = "VendingMachineFuelVend";
    private const string ZoneMarkerProto = "WFTraderBarterZone";
    private const string TableProto = "Table";
    private const string CashProto = "SpaceCash";
    private const string FuelItemProto = "FuelPlasma";
    private const string FuelMaterial = "FuelGradePlasma";
    private const string GeneratorProto = "PortableGeneratorPacmanShuttle";
    private const string ChemGeneratorProto = "PortableGeneratorJrPacmanShuttle";
    private const string AmeControllerProto = "AmeController";
    private const string AmeJarProto = "AmeJar";
    private const string ChemItemProto = "JerryCanWeldingFuel";
    private const string FuelReagent = "WeldingFuel";
    private const string HairMarking = "HumanHairPixie";
    private const string JumpsuitProto = "ClothingUniformJumpsuitEngineering";
    private const string CustomerProto = "MobHuman";

    /// <summary>
    /// Change is always one to three positive stacks that add up exactly.
    /// </summary>
    [Test]
    public void SplitChangeAddsUp()
    {
        var random = new RobustRandom();

        for (var seed = 0; seed < 25; seed++)
        {
            random.SetSeed(seed);

            for (var amount = 1; amount <= 250; amount++)
            {
                var parts = TraderSystem.SplitChange(amount, random);

                Assert.That(parts, Is.Not.Empty, $"No change for {amount} (seed {seed}).");
                Assert.That(parts, Has.Count.LessThanOrEqualTo(3), $"Too many stacks for {amount} (seed {seed}).");
                Assert.That(parts.Sum(), Is.EqualTo(amount), $"Change for {amount} did not add up (seed {seed}).");
                Assert.That(parts.All(p => p > 0), Is.True, $"Empty stack in change for {amount} (seed {seed}).");

                if (amount < 3)
                    Assert.That(parts, Has.Count.EqualTo(1), $"{amount} should come back as one stack.");
            }
        }

        Assert.That(TraderSystem.SplitChange(0, random), Is.Empty);
        Assert.That(TraderSystem.SplitChange(-5, random), Is.Empty);
    }

    /// <summary>
    /// Everything that needs a running server: the trader prototype, its table, a purchase and a refuel quote.
    /// </summary>
    [Test]
    public async Task TraderServerTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        var entMan = server.EntMan;
        var protoMan = server.ResolveDependency<IPrototypeManager>();
        var compFactory = server.ResolveDependency<IComponentFactory>();

        var mapSys = entMan.System<SharedMapSystem>();
        var invSys = entMan.System<InventorySystem>();
        var materialSys = entMan.System<SharedMaterialStorageSystem>();
        var pricing = entMan.System<PricingSystem>();
        var stackSys = entMan.System<SharedStackSystem>();
        var traderSys = entMan.System<TraderSystem>();
        var shopSys = entMan.System<TraderShopSystem>();
        var refuelSys = entMan.System<TraderRefuelSystem>();

        var gridUid = map.Grid.Owner;
        var trader = EntityUid.Invalid;
        var table = EntityUid.Invalid;
        var customer = EntityUid.Invalid;

        // The vending machine the trader mirrors, priced exactly as VendingMachineSystem.AuthorizedVend does.
        int ExpectedPrice(EntityPrototype item)
        {
            var vendorProto = protoMan.Index<EntityPrototype>(VendorProto);
            Assert.That(vendorProto.TryGetComponent<VendingMachineComponent>(out var vend, compFactory), Is.True,
                "The mirrored vendor prototype has no VendingMachine component.");
            vendorProto.TryGetComponent<MarketModifierComponent>(out var modifier, compFactory);

            var price = pricing.GetEstimatedPrice(item);
            if (price == 0)
                price = 20;

            if (modifier != null)
                price *= modifier.Mod;

            var total = vend!.RequiresCash ? (int) price : 0;

            var vendPrice = pricing.GetEstimatedVendPrice(item);
            if (vendPrice > 0.0 && vend.RequiresCash)
                total = (int) vendPrice;

            return total;
        }

        // Part 1: the trader prototype spawns inert, dressed and with its fixed look.
        await server.WaitAssertion(() =>
        {
            foreach (var x in new[] { -1, 1, 2, 3, 4, 5 })
                mapSys.SetTile(gridUid, map.Grid.Comp, new Vector2i(x, 0), map.Tile.Tile);

            mapSys.SetTile(gridUid, map.Grid.Comp, new Vector2i(0, -1), map.Tile.Tile);

            trader = entMan.SpawnEntity(TraderProto, new EntityCoordinates(gridUid, 0.5f, 0.5f));
            customer = entMan.SpawnEntity(CustomerProto, new EntityCoordinates(gridUid, 1.5f, 0.5f));

            Assert.That(entMan.HasComponent<GodmodeComponent>(trader), Is.True, "The trader should be invulnerable.");
            Assert.That(entMan.HasComponent<PullableComponent>(trader), Is.False, "The trader should not be draggable.");
            Assert.That(entMan.HasComponent<StrippableComponent>(trader), Is.False, "The trader should not be strippable.");
            Assert.That(entMan.HasComponent<CuffableComponent>(trader), Is.False, "The trader should not be cuffable.");
            Assert.That(entMan.HasComponent<InputMoverComponent>(trader), Is.False, "The trader should not be movable.");
            // A humanoid answers InteractHand with a hug, which would eat the click that starts a conversation.
            Assert.That(entMan.HasComponent<InteractionPopupComponent>(trader), Is.False,
                "The trader should not be huggable.");
            Assert.That(entMan.GetComponent<PhysicsComponent>(trader).BodyType, Is.EqualTo(BodyType.Static),
                "The trader should be rooted in place.");

            var humanoid = entMan.GetComponent<HumanoidAppearanceComponent>(trader);
            Assert.That(humanoid.Age, Is.EqualTo(45), "The humanoidProfile's age did not load.");
            Assert.That(humanoid.Sex, Is.EqualTo(Sex.Female), "The humanoidProfile's sex did not load.");
            Assert.That(humanoid.MarkingSet.TryGetCategory(MarkingCategories.Hair, out var hair), Is.True,
                "The humanoidProfile's hair did not load.");
            Assert.That(hair!.Any(m => m.MarkingId == HairMarking), Is.True,
                $"The trader should be wearing {HairMarking}.");

            Assert.That(entMan.HasComponent<LoadoutComponent>(trader), Is.True);
            Assert.That(invSys.TryGetSlotEntity(trader, "jumpsuit", out var jumpsuit), Is.True,
                "The trader's loadout did not equip.");
            Assert.That(entMan.GetComponent<MetaDataComponent>(jumpsuit!.Value).EntityPrototype?.ID,
                Is.EqualTo(JumpsuitProto));
        });

        // Part 2: every shop entry is priced like the machine would price it.
        await server.WaitAssertion(() =>
        {
            var shop = entMan.GetComponent<TraderShopComponent>(trader);
            var stock = shopSys.GetStock((trader, shop));

            Assert.That(stock, Is.Not.Empty, "The mirrored vendor's pack produced no stock.");

            foreach (var entry in stock)
            {
                var itemProto = protoMan.Index<EntityPrototype>(entry.Item);
                Assert.That(entry.Price, Is.EqualTo(ExpectedPrice(itemProto)),
                    $"{entry.Item} is not priced like the vending machine prices it.");
            }

            Assert.That(stock.Any(e => e.Item == FuelItemProto), Is.True,
                $"{FuelItemProto} should be in the mirrored vendor's stock.");
        });

        // Part 3: a table one tile ahead becomes the barter zone.
        await server.WaitAssertion(() =>
        {
            table = entMan.SpawnEntity(TableProto, new EntityCoordinates(gridUid, 0.5f, -0.5f));

            var comp = entMan.GetComponent<TraderComponent>(trader);
            traderSys.RefreshTable((trader, comp));

            Assert.That(comp.Table, Is.EqualTo(table), "The trader should trade over the table it faces.");
            Assert.That(comp.ZoneMarkerEntity, Is.Not.Null, "A barter zone marker should have been spawned.");
            Assert.That(entMan.GetComponent<MetaDataComponent>(comp.ZoneMarkerEntity!.Value).EntityPrototype?.ID,
                Is.EqualTo(ZoneMarkerProto));
        });

        // Part 4: a one-item basket, paid out of the barter zone, comes out unpackaged.
        var price = 0;
        var overpay = 137;

        await server.WaitAssertion(() =>
        {
            var traderComp = entMan.GetComponent<TraderComponent>(trader);
            var shop = entMan.GetComponent<TraderShopComponent>(trader);

            price = shopSys.GetStock((trader, shop)).First(e => e.Item == FuelItemProto).Price;
            Assert.That(price, Is.GreaterThan(0), $"{FuelItemProto} should cost something.");

            var cash = entMan.SpawnEntity(CashProto, new EntityCoordinates(gridUid, 0.5f, -0.5f));
            stackSys.SetCount(cash, price + overpay);

            // No session is involved: with enough cash in the zone no ID or bank account is looked at.
            Assert.That(shopSys.TryCheckout((trader, shop), (trader, traderComp), customer,
                    new Dictionary<string, int> { [FuelItemProto] = 1 }),
                Is.True, "The purchase should have gone through.");
        });

        // Spent cash is queued for deletion, so let the tick finish before counting what is left.
        await pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            var traderComp = entMan.GetComponent<TraderComponent>(trader);
            var zone = traderSys.GetZoneItems((trader, traderComp));

            var bought = zone.Count(uid =>
                entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID == FuelItemProto);
            Assert.That(bought, Is.EqualTo(1), "The bought item should be sitting on the table.");
            Assert.That(zone.Any(uid => entMan.HasComponent<TraderParcelComponent>(uid)), Is.False,
                "A single item should not be boxed.");
            Assert.That(PaperCount(entMan, zone), Is.EqualTo(1), "The checkout should have printed a receipt.");

            var stacks = CashStacks(entMan, zone);
            Assert.That(stacks, Is.Not.Empty, "The change should be on the table.");
            Assert.That(stacks, Has.Count.LessThanOrEqualTo(3), "Change should come back as at most three stacks.");
            Assert.That(stacks.Sum(), Is.EqualTo(overpay), "The change should be exact.");
        });

        // Part 5: too little cash and no ID buys nothing and takes nothing.
        await server.WaitAssertion(() =>
        {
            var traderComp = entMan.GetComponent<TraderComponent>(trader);
            var shop = entMan.GetComponent<TraderShopComponent>(trader);

            Assert.That(shopSys.TryCheckout((trader, shop), (trader, traderComp), customer,
                    new Dictionary<string, int> { [FuelItemProto] = 1 }),
                Is.False, "A customer who is short should not be served.");

            var zone = traderSys.GetZoneItems((trader, traderComp));
            var bought = zone.Count(uid =>
                entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID == FuelItemProto);

            Assert.That(bought, Is.EqualTo(1), "Nothing new should have been vended.");
            Assert.That(CashStacks(entMan, zone).Sum(), Is.EqualTo(overpay), "The customer's cash should be untouched.");
        });

        // Part 5b: a multi-item basket is one payment, one parcel and one receipt inside it.
        var basketPrice = 0;
        var basketOverpay = 211;

        await server.WaitAssertion(() =>
        {
            var traderComp = entMan.GetComponent<TraderComponent>(trader);
            var shop = entMan.GetComponent<TraderShopComponent>(trader);

            // Clear the table so the counts below only see this order.
            foreach (var uid in traderSys.GetZoneItems((trader, traderComp)))
                entMan.DeleteEntity(uid);

            var stock = shopSys.GetStock((trader, shop));
            var fuel = stock.First(e => e.Item == FuelItemProto);
            var other = stock.First(e => e.Item == ChemItemProto);

            basketPrice = fuel.Price * 2 + other.Price;

            var cash = entMan.SpawnEntity(CashProto, new EntityCoordinates(gridUid, 0.5f, -0.5f));
            stackSys.SetCount(cash, basketPrice + basketOverpay);

            Assert.That(shopSys.TryCheckout((trader, shop), (trader, traderComp), customer,
                    new Dictionary<string, int> { [FuelItemProto] = 2, [ChemItemProto] = 1 }),
                Is.True, "The basket should have gone through.");
        });

        await pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            var traderComp = entMan.GetComponent<TraderComponent>(trader);
            var zone = traderSys.GetZoneItems((trader, traderComp));

            var parcels = zone.Where(uid => entMan.HasComponent<TraderParcelComponent>(uid)).ToList();
            Assert.That(parcels, Has.Count.EqualTo(1), "Three items should fit in one parcel.");
            Assert.That(PaperCount(entMan, zone), Is.EqualTo(0), "The receipt belongs inside the parcel.");

            var parcel = entMan.GetComponent<TraderParcelComponent>(parcels[0]);
            var containerSys = entMan.System<SharedContainerSystem>();
            Assert.That(containerSys.TryGetContainer(parcels[0], parcel.ContainerId, out var container), Is.True,
                "The parcel should have its contents container.");

            var contents = container!.ContainedEntities.ToList();
            Assert.That(contents.Count(uid =>
                    entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID == FuelItemProto),
                Is.EqualTo(2), "Both units should be in the parcel.");
            Assert.That(contents.Count(uid =>
                    entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID == ChemItemProto),
                Is.EqualTo(1), "The second line should be in the parcel.");
            Assert.That(contents.Count(uid => entMan.HasComponent<PaperComponent>(uid)), Is.EqualTo(1),
                "The receipt should be packed with the goods.");

            var stacks = CashStacks(entMan, zone);
            Assert.That(stacks.Sum(), Is.EqualTo(basketOverpay), "One payment should have covered the whole basket.");
        });

        // Part 5c: browsing costs nothing. The catalogue opens with no money on the table, and it is
        // the checkout that refuses.
        await server.WaitPost(() =>
        {
            var traderComp = entMan.GetComponent<TraderComponent>(trader);
            foreach (var uid in traderSys.GetZoneItems((trader, traderComp)))
            {
                if (entMan.TryGetComponent(uid, out StackComponent? stack)
                    && stack.StackTypeId == TraderSystem.CashStackType.Id)
                {
                    entMan.DeleteEntity(uid);
                }
            }
        });

        await pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            var traderComp = entMan.GetComponent<TraderComponent>(trader);
            var shop = entMan.GetComponent<TraderShopComponent>(trader);
            var dialogue = protoMan.Index<TraderDialoguePrototype>(traderComp.Dialogue);

            var zone = traderSys.GetZoneItems((trader, traderComp));
            Assert.That(CashStacks(entMan, zone), Is.Empty, "There should be no money left on the table.");
            Assert.That(traderSys.TryGetZoneId((trader, traderComp), customer, out _, out _), Is.False,
                "There should be no usable ID on the table either.");

            var browse = dialogue.Options.First(o => o.Action == TraderAction.OpenShop);
            Assert.That(browse.Requires, Is.EqualTo(TraderRequirement.None),
                "Opening a catalogue must not ask for anything.");
            Assert.That(traderSys.MeetsRequirement((trader, traderComp), customer, browse.Requires, out _), Is.True,
                "The shop should open over an empty table.");

            var before = zone.Count(uid =>
                entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID == FuelItemProto);

            Assert.That(shopSys.TryCheckout((trader, shop), (trader, traderComp), customer,
                    new Dictionary<string, int> { [FuelItemProto] = 1 }),
                Is.False, "A checkout with nothing on the table should be refused.");

            var after = traderSys.GetZoneItems((trader, traderComp)).Count(uid =>
                entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID == FuelItemProto);
            Assert.That(after, Is.EqualTo(before), "A refused checkout should not have vended anything.");
        });

        // Part 5d: every trader in the game follows the same rule.
        await server.WaitAssertion(() =>
        {
            foreach (var dialogue in protoMan.EnumeratePrototypes<TraderDialoguePrototype>())
            {
                foreach (var option in dialogue.Options)
                {
                    if (option.Action is not (TraderAction.OpenShop or TraderAction.UsedShips))
                        continue;

                    Assert.That(option.Requires, Is.EqualTo(TraderRequirement.None),
                        $"{dialogue.ID} makes the customer pay before they may look at {option.Action}.");
                }
            }
        });

        // Part 6: losing the table clears the zone within an upkeep tick.
        await server.WaitPost(() => entMan.DeleteEntity(table));
        await pair.RunTicksSync(70);

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<TraderComponent>(trader);
            Assert.That(comp.Table, Is.Null, "The trader should have noticed the table go.");
            Assert.That(comp.ZoneMarkerEntity, Is.Null, "The barter zone marker should have been cleaned up.");
        });

        // Part 7: the refuelling quote, and the fill it promises.
        await server.WaitAssertion(() =>
        {
            var refuel = entMan.GetComponent<TraderRefuelComponent>(trader);
            var solutionSys = entMan.System<SharedSolutionContainerSystem>();

            var solid = entMan.SpawnEntity(GeneratorProto, new EntityCoordinates(gridUid, 3.5f, 0.5f));
            var chemical = entMan.SpawnEntity(ChemGeneratorProto, new EntityCoordinates(gridUid, 2.5f, 0.5f));

            Assert.That(entMan.TryGetComponent(solid, out MaterialStorageComponent? storage), Is.True);
            Assert.That(storage!.StorageLimit, Is.Not.Null, $"{GeneratorProto} has no storage limit to fill to.");

            var limit = storage.StorageLimit!.Value;
            var before = materialSys.GetTotalMaterialAmount(solid, storage, true);
            Assert.That(before, Is.GreaterThan(0), $"{GeneratorProto} should start with some fuel.");

            // Drain a bit so there is something to quote for.
            Assert.That(materialSys.TryChangeMaterialAmount(solid, FuelMaterial, -(before / 2), storage), Is.True);

            var missing = limit - materialSys.GetTotalMaterialAmount(solid, storage, true);
            Assert.That(missing, Is.GreaterThan(0));

            Entity<SolutionComponent>? tank = null;
            Assert.That(solutionSys.ResolveSolution(chemical, "tank", ref tank, out var solution), Is.True,
                $"{ChemGeneratorProto} has no fuel tank.");
            var missingFuel = solution!.AvailableVolume.Int();
            Assert.That(missingFuel, Is.GreaterThan(0), $"{ChemGeneratorProto} should start part-empty.");

            var solidItem = protoMan.Index<EntityPrototype>(FuelItemProto);
            var solidUnitPrice = ExpectedPrice(solidItem) / (double) UnitsPerItem(solidItem, compFactory, FuelMaterial);

            var chemItem = protoMan.Index<EntityPrototype>(ChemItemProto);
            var chemUnitPrice = ExpectedPrice(chemItem) / (double) ReagentsPerItem(chemItem, compFactory, FuelReagent);

            // Two AME controllers: one with an empty slot, one with a half-used jar.
            var emptyAme = entMan.SpawnEntity(AmeControllerProto, new EntityCoordinates(gridUid, 4.5f, 0.5f));
            var jarAme = entMan.SpawnEntity(AmeControllerProto, new EntityCoordinates(gridUid, 5.5f, 0.5f));
            var jar = entMan.SpawnEntity(AmeJarProto, new EntityCoordinates(gridUid, 5.5f, 0.5f));
            var jarComp = entMan.GetComponent<AmeFuelContainerComponent>(jar);
            jarComp.FuelAmount = jarComp.FuelCapacity / 2;
            Assert.That(entMan.System<ItemSlotsSystem>().TryInsert(jarAme,
                entMan.GetComponent<AmeControllerComponent>(jarAme).FuelSlot, jar, null), Is.True, "The jar should go in.");

            var jarProto = protoMan.Index<EntityPrototype>(AmeJarProto);
            Assert.That(jarProto.TryGetComponent<AmeFuelContainerComponent>(out var fullJar, compFactory), Is.True);
            var jarUnitPrice = ExpectedPrice(jarProto) / (double) fullJar!.FuelCapacity;

            var lines = refuelSys.GetQuote((trader, refuel), gridUid, out var total);
            Assert.That(lines, Has.Count.EqualTo(4), "Both generators and both AMEs should have been quoted.");

            var emptyLine = lines.First(l => l.Generator == emptyAme);
            Assert.That(emptyLine.IsAntimatter, Is.True);
            Assert.That(emptyLine.Units, Is.EqualTo(fullJar.FuelCapacity), "An empty slot gets a whole jar.");
            Assert.That(emptyLine.Cost, Is.EqualTo((int) Math.Ceiling(fullJar.FuelCapacity * jarUnitPrice)));

            var jarLine = lines.First(l => l.Generator == jarAme);
            Assert.That(jarLine.Units, Is.EqualTo(jarComp.FuelCapacity - jarComp.FuelAmount), "A part-used jar is topped up.");

            var solidLine = lines.First(l => l.Generator == solid);
            Assert.That(solidLine.Units, Is.EqualTo(missing), "The quote should cover exactly what is missing.");
            Assert.That(solidLine.Cost, Is.EqualTo((int) Math.Ceiling(missing * solidUnitPrice)),
                "The quote should be the missing units at the vendor's unit price.");

            var chemLine = lines.First(l => l.Generator == chemical);
            Assert.That(chemLine.IsReagent, Is.True);
            Assert.That(chemLine.Units, Is.EqualTo(missingFuel), "The quote should fill the tank to the brim.");
            Assert.That(chemLine.Cost, Is.EqualTo((int) Math.Ceiling(missingFuel * chemUnitPrice)));

            Assert.That(total, Is.EqualTo(solidLine.Cost + chemLine.Cost + emptyLine.Cost + jarLine.Cost),
                "This trader charges no service fee.");

            refuelSys.Fill(lines);
            Assert.That(materialSys.GetTotalMaterialAmount(solid, storage, true), Is.EqualTo(limit),
                "Filling should top the generator up to its limit.");
            Assert.That(solution.AvailableVolume.Int(), Is.EqualTo(0),
                "Filling should top the chemical generator's tank up.");
            Assert.That(jarComp.FuelAmount, Is.EqualTo(jarComp.FuelCapacity), "The part-used jar should be full.");
            var newJar = entMan.GetComponent<AmeControllerComponent>(emptyAme).FuelSlot.Item;
            Assert.That(newJar, Is.Not.Null, "The empty AME should have been given a jar.");
            Assert.That(entMan.GetComponent<AmeFuelContainerComponent>(newJar!.Value).FuelAmount, Is.EqualTo(fullJar.FuelCapacity));
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// How much of a material one whole item of this prototype is worth.
    /// </summary>
    private static int UnitsPerItem(EntityPrototype proto, IComponentFactory factory, string material)
    {
        Assert.That(proto.TryGetComponent<PhysicalCompositionComponent>(out var composition, factory), Is.True,
            $"{proto.ID} has no material composition.");
        Assert.That(composition!.MaterialComposition.TryGetValue(material, out var per), Is.True,
            $"{proto.ID} is not made of {material}.");

        var count = 1;
        if (proto.TryGetComponent<StackComponent>(out var stack, factory))
            count = stack!.Count;

        return per * count;
    }

    /// <summary>
    /// How much of a reagent one whole item of this prototype holds.
    /// </summary>
    private static int ReagentsPerItem(EntityPrototype proto, IComponentFactory factory, string reagent)
    {
        Assert.That(proto.TryGetComponent<SolutionContainerManagerComponent>(out var manager, factory), Is.True,
            $"{proto.ID} has no solutions.");
        Assert.That(manager!.Solutions, Is.Not.Null, $"{proto.ID} has no solutions to fill from.");

        var total = FixedPoint2.Zero;
        foreach (var solution in manager.Solutions!.Values)
            total += solution.GetTotalPrototypeQuantity(reagent);

        return total.Int();
    }

    /// <summary>
    /// Receipts lying loose in the barter zone.
    /// </summary>
    private static int PaperCount(IEntityManager entMan, List<EntityUid> zone)
    {
        return zone.Count(uid => entMan.HasComponent<PaperComponent>(uid));
    }

    private static List<int> CashStacks(IEntityManager entMan, List<EntityUid> zone)
    {
        var stacks = new List<int>();
        foreach (var uid in zone)
        {
            if (!entMan.TryGetComponent(uid, out StackComponent? stack))
                continue;

            if (stack.StackTypeId != TraderSystem.CashStackType.Id)
                continue;

            stacks.Add(stack.Count);
        }

        return stacks;
    }
}
