using System.Linq;
using System.Text;
using Content.Server.GameTicking;
using Content.Server.Ame.Components;
using Content.Server.Ame.EntitySystems;
using Content.Server.Power.Generator;
using Content.Shared.Ame.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Server.Shuttles.Components;
using Content.Shared._NF.Bank;
using Content.Shared._NF.Bank.Components;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._WF.Traders;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Content.Shared.Materials;
using Content.Shared.Power.Generator;
using Content.Shared.Stacks;
using Content.Shared.VendingMachines;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Traders;

/// <summary>
/// One generator a trader can top off, and what that costs at the mirrored vendor's prices.
/// </summary>
public record struct TraderRefuelLine(
    EntityUid Generator,
    string FuelId,
    bool IsReagent,
    int Units,
    int Cost,
    bool IsAntimatter = false);

/// <summary>
/// Fills the generators of a docked ship, priced off a vending machine's fuel stock.
/// </summary>
public sealed partial class TraderRefuelSystem : EntitySystem
{
    [Dependency] private AmeControllerSystem _ame = default!;
    [Dependency] private GameTicker _gameTicker = default!;
    [Dependency] private ItemSlotsSystem _itemSlots = default!;
    [Dependency] private IComponentFactory _componentFactory = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private SharedMaterialStorageSystem _materialStorage = default!;
    [Dependency] private SharedSolutionContainerSystem _solutionContainer = default!;
    [Dependency] private TraderShopSystem _shop = default!;
    [Dependency] private TraderSystem _trader = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<TraderRefuelComponent, TraderActionEvent>(OnAction);
        SubscribeLocalEvent<TraderRefuelComponent, TraderConfirmedEvent>(OnConfirmed);
    }

    private void OnAction(Entity<TraderRefuelComponent> ent, ref TraderActionEvent args)
    {
        // Any other service taking over means an older quote is dead.
        ent.Comp.AwaitingConfirmation = false;

        if (args.Action != TraderAction.Refuel || args.Handled)
            return;

        if (!TryComp<TraderComponent>(ent, out var trader))
            return;

        args.Handled = true;

        var traderEnt = (ent.Owner, trader);

        if (!TryGetShip(traderEnt, out var ship))
        {
            _trader.SayAndShow(traderEnt, Loc.GetString("trader-refuel-no-ship"));
            return;
        }

        if (!IsDockedToTrader(traderEnt, ship))
        {
            _trader.SayAndShow(traderEnt, Loc.GetString("trader-refuel-not-docked"));
            return;
        }

        var lines = GetQuote(ent, ship, out var total);
        if (lines.Count == 0)
        {
            _trader.SayAndShow(traderEnt, Loc.GetString("trader-refuel-full"));
            return;
        }

        ent.Comp.AwaitingConfirmation = true;
        _trader.AskConfirmation(traderEnt, Loc.GetString("trader-refuel-quote",
            ("count", lines.Count),
            ("cost", BankSystemExtensions.ToSpesoString(total))));
    }

    private void OnConfirmed(Entity<TraderRefuelComponent> ent, ref TraderConfirmedEvent args)
    {
        if (!ent.Comp.AwaitingConfirmation)
            return;

        ent.Comp.AwaitingConfirmation = false;

        if (!args.Accepted)
            return;

        if (!TryComp<TraderComponent>(ent, out var trader))
            return;

        var traderEnt = (ent.Owner, trader);
        var customer = args.Customer;

        // The zone, the ship and the tanks can all have moved while the customer was thinking.
        if (!TryGetShip(traderEnt, out var ship))
        {
            _trader.SayAndShow(traderEnt, Loc.GetString("trader-refuel-no-ship"));
            return;
        }

        if (!IsDockedToTrader(traderEnt, ship))
        {
            _trader.SayAndShow(traderEnt, Loc.GetString("trader-refuel-not-docked"));
            return;
        }

        var lines = GetQuote(ent, ship, out var total);
        if (lines.Count == 0)
        {
            _trader.SayAndShow(traderEnt, Loc.GetString("trader-refuel-full"));
            return;
        }

        if (!_trader.TryTakePayment(traderEnt, customer, total, out var tendered, out var change))
            return;

        Fill(lines);

        if (_shop.TryGetVendor(ent.Comp.Vendor, out var vend, out _))
            _shop.PayTaxes(vend, total);

        PrintReceipt(traderEnt, customer, ship, lines, total, tendered, change);
        _trader.SayAndShow(traderEnt, Loc.GetString("trader-refuel-done"));
    }

    #region Ship

    /// <summary>
    /// Reads the ship off a deed ID sitting in the barter zone.
    /// </summary>
    public bool TryGetShip(Entity<TraderComponent> trader, out EntityUid ship)
    {
        ship = default;

        foreach (var item in _trader.GetZoneItems(trader))
        {
            if (!TryComp<ShuttleDeedComponent>(item, out var deed))
                continue;

            if (deed.ShuttleUid is not { } uid || TerminatingOrDeleted(uid))
                continue;

            ship = uid;
            return true;
        }

        return false;
    }

    /// <summary>
    /// True when one of the ship's docks is joined to the grid the trader is standing on.
    /// </summary>
    public bool IsDockedToTrader(Entity<TraderComponent> trader, EntityUid ship)
    {
        if (Transform(trader).GridUid is not { } here)
            return false;

        if (ship == here)
            return true;

        var query = EntityQueryEnumerator<DockingComponent, TransformComponent>();
        while (query.MoveNext(out _, out var dock, out var xform))
        {
            if (xform.GridUid != ship || dock.DockedWith is not { } other)
                continue;

            if (!TerminatingOrDeleted(other) && Transform(other).GridUid == here)
                return true;
        }

        return false;
    }

    #endregion

    #region Quote

    /// <summary>
    /// Every generator on the ship that can take fuel, with the units missing and what they cost.
    /// <paramref name="total"/> is the sum plus the service fee, rounded up.
    /// </summary>
    public List<TraderRefuelLine> GetQuote(Entity<TraderRefuelComponent> ent, EntityUid ship, out int total)
    {
        var lines = new List<TraderRefuelLine>();
        total = 0;

        if (!_shop.TryGetVendor(ent.Comp.Vendor, out var vend, out var modifier))
            return lines;

        var subtotal = 0;

        var query = EntityQueryEnumerator<FuelGeneratorComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid != ship)
                continue;

            if (TryQuoteSolid(ent, uid, vend, modifier, out var line)
                || TryQuoteChemical(ent, uid, vend, modifier, out line))
            {
                lines.Add(line);
                subtotal += line.Cost;
            }
        }

        var ameQuery = EntityQueryEnumerator<AmeControllerComponent, TransformComponent>();
        while (ameQuery.MoveNext(out var uid, out var controller, out var xform))
        {
            if (xform.GridUid != ship)
                continue;

            if (TryQuoteAntimatter(ent, uid, controller, vend, modifier, out var line))
            {
                lines.Add(line);
                subtotal += line.Cost;
            }
        }

        total = (int) Math.Ceiling(subtotal * (1f + Math.Max(0f, ent.Comp.ServiceFee)));
        return lines;
    }

    private bool TryQuoteSolid(Entity<TraderRefuelComponent> ent,
        EntityUid generator,
        VendingMachineComponent vend,
        MarketModifierComponent? modifier,
        out TraderRefuelLine line)
    {
        line = default;

        if (!TryComp<SolidFuelGeneratorAdapterComponent>(generator, out var adapter))
            return false;

        if (!TryComp<MaterialStorageComponent>(generator, out var storage))
            return false;

        var material = adapter.FuelMaterial;
        if (GetUnitPrice(ent, material, false, vend, modifier) is not { } unitPrice)
            return false;

        // Uncapped generators count as full at the fuel they are built with.
        int missing;
        if (storage.StorageLimit is { } limit)
            missing = limit - _materialStorage.GetTotalMaterialAmount(generator, storage, true);
        else
            missing = GetFactoryFuel(generator, material) - _materialStorage.GetMaterialAmount(generator, material, storage, true);

        if (missing <= 0)
            return false;

        line = new TraderRefuelLine(generator, material, false, missing, (int) Math.Ceiling(missing * unitPrice));
        return true;
    }

    private bool TryQuoteChemical(Entity<TraderRefuelComponent> ent,
        EntityUid generator,
        VendingMachineComponent vend,
        MarketModifierComponent? modifier,
        out TraderRefuelLine line)
    {
        line = default;

        if (!TryComp<ChemicalFuelGeneratorAdapterComponent>(generator, out var adapter))
            return false;

        if (!TryGetTank(generator, adapter, out _, out var solution))
            return false;

        var burns = adapter.Reagents;

        // The first listed reagent this generator will actually burn.
        foreach (var fuel in ent.Comp.Fuels)
        {
            if (fuel.Reagent is not { } reagent || !burns.ContainsKey(reagent))
                continue;

            if (GetUnitPrice(ent, reagent, true, vend, modifier) is not { } unitPrice)
                continue;

            var missing = solution.AvailableVolume.Int();
            if (missing <= 0)
                return false;

            line = new TraderRefuelLine(generator, reagent, true, missing, (int) Math.Ceiling(missing * unitPrice));
            return true;
        }

        return false;
    }

    /// <summary>
    /// An AME controller: top up the jar it holds, or supply a full one if the slot is empty.
    /// </summary>
    private bool TryQuoteAntimatter(Entity<TraderRefuelComponent> ent,
        EntityUid controller,
        AmeControllerComponent ame,
        VendingMachineComponent vend,
        MarketModifierComponent? modifier,
        out TraderRefuelLine line)
    {
        line = default;

        var entry = ent.Comp.Fuels.FirstOrDefault(f => f.Antimatter);
        if (entry == null || !_proto.TryIndex(entry.Item, out var jarProto))
            return false;

        if (!jarProto.TryGetComponent<AmeFuelContainerComponent>(out var fullJar, _componentFactory) || fullJar.FuelCapacity <= 0)
            return false;

        var price = _shop.GetPrice(jarProto, vend, modifier);
        if (price <= 0)
            return false;

        var unitPrice = price / (double) fullJar.FuelCapacity;

        int missing;
        if (TryComp<AmeFuelContainerComponent>(ame.FuelSlot.Item, out var jar))
            missing = jar.FuelCapacity - jar.FuelAmount;
        else
            missing = fullJar.FuelCapacity;

        if (missing <= 0)
            return false;

        line = new TraderRefuelLine(controller, entry.Item, false, missing, (int) Math.Ceiling(missing * unitPrice), true);
        return true;
    }

    /// <summary>
    /// What one unit of a fuel costs: the vendor's price for the item that carries it, over how much it carries.
    /// </summary>
    public double? GetUnitPrice(Entity<TraderRefuelComponent> ent,
        string fuelId,
        bool reagent,
        VendingMachineComponent vend,
        MarketModifierComponent? modifier)
    {
        foreach (var fuel in ent.Comp.Fuels)
        {
            if (reagent ? fuel.Reagent != fuelId : fuel.Material != fuelId)
                continue;

            if (!_proto.TryIndex(fuel.Item, out var itemProto))
                continue;

            var units = reagent ? GetReagentUnits(itemProto, fuelId) : GetMaterialUnits(itemProto, fuelId);
            if (units <= 0)
                continue;

            var price = _shop.GetPrice(itemProto, vend, modifier);
            if (price <= 0)
                continue;

            return price / (double) units;
        }

        return null;
    }

    /// <summary>
    /// How much of a material the generator's prototype starts with.
    /// </summary>
    private int GetFactoryFuel(EntityUid generator, string material)
    {
        if (MetaData(generator).EntityPrototype is not { } proto)
            return 0;

        if (!proto.TryGetComponent<MaterialStorageComponent>(out var storage, _componentFactory))
            return 0;

        return storage.Storage.GetValueOrDefault(material);
    }

    /// <summary>
    /// How much of a material a whole stack of the item is worth to a material storage.
    /// </summary>
    private int GetMaterialUnits(EntityPrototype proto, string material)
    {
        if (!proto.TryGetComponent<PhysicalCompositionComponent>(out var composition, _componentFactory))
            return 0;

        if (!composition.MaterialComposition.TryGetValue(material, out var per))
            return 0;

        var count = 1;
        if (proto.TryGetComponent<StackComponent>(out var stack, _componentFactory))
            count = stack.Count;

        return per * count;
    }

    /// <summary>
    /// How much of a reagent the item's solutions hold.
    /// </summary>
    private int GetReagentUnits(EntityPrototype proto, string reagent)
    {
        if (!proto.TryGetComponent<SolutionContainerManagerComponent>(out var manager, _componentFactory))
            return 0;

        if (manager.Solutions is not { } solutions)
            return 0;

        var total = FixedPoint2.Zero;
        foreach (var solution in solutions.Values)
            total += solution.GetTotalPrototypeQuantity(reagent);

        return total.Int();
    }

    private bool TryGetTank(EntityUid generator,
        ChemicalFuelGeneratorAdapterComponent adapter,
        out Entity<SolutionComponent> tank,
        out Solution solution)
    {
        tank = default;
        solution = default!;

        Entity<SolutionComponent>? found = null;
        if (!_solutionContainer.ResolveSolution(generator, adapter.SolutionName, ref found, out var resolved))
            return false;

        tank = found.Value;
        solution = resolved;
        return true;
    }

    #endregion

    #region Filling

    /// <summary>
    /// Tops off every quoted generator.
    /// </summary>
    public void Fill(List<TraderRefuelLine> lines)
    {
        foreach (var line in lines)
        {
            if (TerminatingOrDeleted(line.Generator))
                continue;

            if (line.IsAntimatter)
            {
                FillAntimatter(line);
                continue;
            }

            if (!line.IsReagent)
            {
                _materialStorage.TryChangeMaterialAmount(line.Generator, line.FuelId, line.Units);
                continue;
            }

            if (!TryComp<ChemicalFuelGeneratorAdapterComponent>(line.Generator, out var adapter))
                continue;

            if (!TryGetTank(line.Generator, adapter, out var tank, out _))
                continue;

            _solutionContainer.TryAddReagent(tank, line.FuelId, line.Units, out _);
        }
    }

    private void FillAntimatter(TraderRefuelLine line)
    {
        if (!TryComp<AmeControllerComponent>(line.Generator, out var ame))
            return;

        if (TryComp<AmeFuelContainerComponent>(ame.FuelSlot.Item, out var jar))
        {
            jar.FuelAmount = Math.Min(jar.FuelCapacity, jar.FuelAmount + line.Units);
            Dirty(ame.FuelSlot.Item.Value, jar);
        }
        else
        {
            var fresh = Spawn(line.FuelId, Transform(line.Generator).Coordinates);
            if (!_itemSlots.TryInsert(line.Generator, ame.FuelSlot, fresh, null))
                QueueDel(fresh);
        }

        _ame.UpdateUi(line.Generator, ame);
    }

    #endregion

    private void PrintReceipt(Entity<TraderComponent> trader,
        EntityUid customer,
        EntityUid ship,
        List<TraderRefuelLine> lines,
        int total,
        int tendered,
        int change)
    {
        var body = new StringBuilder();

        body.AppendLine(Loc.GetString("trader-refuel-receipt-header",
            ("trader", Name(trader)),
            ("customer", Name(customer)),
            ("ship", Name(ship)),
            ("time", _gameTicker.RoundDuration().ToString("hh\\:mm\\:ss"))));

        foreach (var line in lines)
        {
            body.AppendLine(Loc.GetString("trader-refuel-receipt-line",
                ("generator", Name(line.Generator)),
                ("amount", line.Units),
                ("fuel", GetFuelName(line)),
                ("price", BankSystemExtensions.ToSpesoString(line.Cost))));
        }

        body.AppendLine(Loc.GetString("trader-refuel-receipt-total",
            ("total", BankSystemExtensions.ToSpesoString(total)),
            ("paid", BankSystemExtensions.ToSpesoString(tendered)),
            ("change", BankSystemExtensions.ToSpesoString(change))));

        _trader.PrintReceipt(trader, Loc.GetString("trader-refuel-receipt-name"), body.ToString());
    }

    private string GetFuelName(TraderRefuelLine line)
    {
        if (line.IsAntimatter)
            return _proto.TryIndex(line.FuelId, out EntityPrototype? jar) ? jar.Name : line.FuelId;

        if (line.IsReagent)
            return _proto.TryIndex<ReagentPrototype>(line.FuelId, out var reagent) ? reagent.LocalizedName : line.FuelId;

        return _proto.TryIndex<MaterialPrototype>(line.FuelId, out var material) && material.Name != string.Empty
            ? Loc.GetString(material.Name)
            : line.FuelId;
    }
}
