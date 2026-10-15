using System.Globalization;
using System.Linq;
using Content.Shared._WF.Drydock;
using Content.Shared.Materials;
using Content.Shared.Materials.OreSilo;

namespace Content.Server._WF.Drydock;

public sealed partial class WFDrydockSystem
{
    [Dependency] private SharedOreSiloSystem _oreSilo = default!;

    /// <summary>
    /// Sets each item's material bill for a console that pays in materials, within the ship's limits, then pays what
    /// refit units it can with parts from the cranes' parts silos.
    /// </summary>
    private void AssignMaterials(EntityUid ship, WFDrydockConsoleComponent comp, WFDrydockMaterialPaymentComponent pay,
        List<WFDrydockWorkItem> items, List<EntityUid> cranes)
    {
        foreach (var item in items)
        {
            if (item.Kind == WFDrydockWorkKind.Upgrade)
            {
                item.Materials = RefitBill(pay, item);
                continue;
            }

            item.Materials = item.Prototype is { } protoId && _prototype.TryIndex(protoId, out var entityProto)
                ? InKindBill(pay, item, ConstructionMaterials(entityProto))
                : ItemBill(pay, item.Cost);
        }

        LimitBill(pay, items, BillScale(ship, comp, pay));

        // Rebuilding what a break stripped costs its full materials, outside the limits, so breaking it again never pays.
        foreach (var item in items)
        {
            if (item.Broken)
                BillBroken(pay, item);
        }

        // A rebuild restoring a refit pays the refit's materials too, outside the limits.
        foreach (var item in items)
        {
            if (item.Kind != WFDrydockWorkKind.Upgrade && item.RefitMaterials != null && item.Materials != null)
                AddBill(item.Materials, item.RefitMaterials);
        }

        AssignParts(cranes, items);
    }

    /// <summary>A refit item's bill: its tiers' own materials, or the rates on its cost when they list none.</summary>
    private static Dictionary<string, int> RefitBill(WFDrydockMaterialPaymentComponent pay, WFDrydockWorkItem item)
    {
        return item.RefitMaterials is { Count: > 0 } refit
            ? new Dictionary<string, int>(refit)
            : ItemBill(pay, item.Cost);
    }

    /// <summary>
    /// How the material limits scale for a ship: its shipyard price over the reference price. A ship not bought at a
    /// shipyard uses its full rebuild price, brought to a shipyard price by the ratio of the two repair caps.
    /// </summary>
    private double BillScale(EntityUid ship, WFDrydockConsoleComponent comp, WFDrydockMaterialPaymentComponent pay)
    {
        var (value, vessel) = ShipValue(ship, comp);
        if (value <= 0 || pay.ReferenceValue <= 0)
            return 1;

        var price = vessel ? value : value * comp.SnapshotCapFraction / comp.CapFraction;
        return price / pay.ReferenceValue;
    }

    /// <summary>Scales each repair item's share of a material down so the repair bills no more of it than its limit.</summary>
    private static void LimitBill(WFDrydockMaterialPaymentComponent pay, List<WFDrydockWorkItem> items, double limitScale)
    {
        var totals = new Dictionary<string, long>();
        foreach (var item in items)
        {
            if (item.Kind == WFDrydockWorkKind.Upgrade || item.Materials == null)
                continue;

            foreach (var (id, amount) in item.Materials)
                totals[id] = totals.GetValueOrDefault(id) + amount;
        }

        foreach (var (id, total) in totals)
        {
            var limit = (long) ((pay.MaxBill.TryGetValue(id, out var max) ? max : pay.DefaultMaxBill) * limitScale);
            if (total <= limit)
                continue;

            var scale = (double) limit / total;
            foreach (var item in items)
            {
                if (item.Kind == WFDrydockWorkKind.Upgrade || item.Materials == null || !item.Materials.TryGetValue(id, out var amount))
                    continue;

                // Rounded down, so the scaled shares never add up past the limit.
                var scaled = (int) (amount * scale);
                if (scaled > 0)
                    item.Materials[id] = scaled;
                else
                    item.Materials.Remove(id);
            }
        }
    }

    /// <summary>
    /// A rebuild or heal's bill: the rates on its cost, plus a little of each construction material the rates don't
    /// charge, such as a plastitanium wall's plastitanium.
    /// </summary>
    private static Dictionary<string, int> InKindBill(WFDrydockMaterialPaymentComponent pay, WFDrydockWorkItem item, Dictionary<string, int> construction)
    {
        var bill = ItemBill(pay, item.Cost - item.RefitCost);
        foreach (var (id, amount) in construction)
        {
            if (pay.Rates.ContainsKey(id))
                continue;

            var units = (int) Math.Ceiling(amount * pay.InKindFactor * item.Share);
            if (units > 0)
                bill[id] = bill.GetValueOrDefault(id) + units;
        }

        return bill;
    }

    /// <summary>A material bill for a cost: the cost times each rate.</summary>
    private static Dictionary<string, int> ItemBill(WFDrydockMaterialPaymentComponent pay, int cost)
    {
        var bill = new Dictionary<string, int>();
        foreach (var (id, rate) in pay.Rates)
        {
            var amount = (int) Math.Ceiling(cost * rate);
            if (amount > 0)
                bill[id] = amount;
        }

        return bill;
    }

    /// <summary>Adds each material of one bill into another.</summary>
    private static void AddBill(Dictionary<string, int> bill, Dictionary<string, int> add)
    {
        foreach (var (id, amount) in add)
        {
            bill[id] = bill.GetValueOrDefault(id) + amount;
        }
    }

    /// <summary>Sums the material bills of work items.</summary>
    private static Dictionary<string, int> SumBills(IEnumerable<WFDrydockWorkItem> items)
    {
        var bill = new Dictionary<string, int>();
        foreach (var item in items)
        {
            if (item.Materials != null)
                AddBill(bill, item.Materials);
        }

        return bill;
    }

    /// <summary>The cranes a console's next job would use: its working berth cranes, or none without a berth.</summary>
    private List<EntityUid> WorkingCranes(EntityUid console, WFDrydockConsoleComponent comp)
    {
        return TryGetBerth(console, comp, out var berth) ? berth.Cranes : new List<EntityUid>();
    }

    /// <summary>The cranes a job drives, skipping empty slots.</summary>
    private static List<EntityUid> JobCranes(WFDrydockJobComponent job)
    {
        return job.Slots.Where(s => s.Crane != null).Select(s => s.Crane!.Value).ToList();
    }

    /// <summary>The silo a crane draws from, if it is linked to one that can transmit to it.</summary>
    private EntityUid? CraneSilo(EntityUid crane)
    {
        if (CompOrNull<OreSiloClientComponent>(crane)?.Silo is not { } silo || !TryComp<OreSiloComponent>(silo, out var siloComp))
            return null;

        return _oreSilo.CanTransmitMaterials((silo, siloComp, Transform(silo)), crane) ? silo : null;
    }

    /// <summary>The distinct silos that can transmit to at least one of the cranes, in crane order.</summary>
    private List<EntityUid> SourceSilos(List<EntityUid> cranes)
    {
        var silos = new List<EntityUid>();
        foreach (var crane in cranes)
        {
            if (CraneSilo(crane) is { } silo && !silos.Contains(silo))
                silos.Add(silo);
        }

        return silos;
    }

    /// <summary>An entity's own stock, ignoring any linked silo.</summary>
    private Dictionary<string, int> LocalStored(EntityUid uid)
    {
        var stored = new Dictionary<string, int>();
        if (!TryComp<MaterialStorageComponent>(uid, out var storage))
            return stored;

        foreach (var (id, amount) in _materials.GetStoredMaterials((uid, storage), localOnly: true))
        {
            stored[id] = amount;
        }

        return stored;
    }

    /// <summary>
    /// Materials a console's cranes can pay with: each crane's own stock plus each distinct linked silo once,
    /// so several cranes on one silo don't count it twice.
    /// </summary>
    private Dictionary<string, int> Availability(List<EntityUid> cranes)
    {
        var total = new Dictionary<string, int>();
        foreach (var crane in cranes)
            AddBill(total, LocalStored(crane));

        foreach (var silo in SourceSilos(cranes))
            AddBill(total, LocalStored(silo));

        return total;
    }

    /// <summary>Adds (positive) or takes (negative) materials from one entity's own storage, ignoring linked silos.</summary>
    private bool TryChangeLocal(EntityUid uid, Dictionary<string, int> change)
    {
        if (!TryComp<MaterialStorageComponent>(uid, out var storage))
            return false;

        return _materials.TryChangeMaterialAmount((uid, storage), change, localOnly: true);
    }

    /// <summary>
    /// Takes the items' bill from the console's silos, then its cranes, and their parts from the parts silos, all or
    /// nothing; denies with the shortfall.
    /// </summary>
    private bool TryPayMaterials(EntityUid console, WFDrydockConsoleComponent comp, EntityUid actor, List<WFDrydockWorkItem> items)
    {
        var bill = SumBills(items);
        var parts = SumParts(items);
        var cranes = WorkingCranes(console, comp);
        var silos = SourceSilos(cranes);

        // Each source is visited once per material, so no stock is counted or taken twice.
        var draws = new Dictionary<EntityUid, Dictionary<string, int>>();
        var missing = new Dictionary<string, int>();
        foreach (var (id, amount) in bill)
        {
            var left = amount;
            foreach (var source in silos.Concat(cranes))
            {
                if (left <= 0)
                    break;

                var take = Math.Min(left, _materials.GetMaterialAmount(source, id, localOnly: true));
                if (take <= 0)
                    continue;

                if (!draws.TryGetValue(source, out var draw))
                    draws[source] = draw = new Dictionary<string, int>();

                draw[id] = take;
                left -= take;
            }

            if (left > 0)
                missing[id] = left;
        }

        var missingParts = MissingParts(cranes, parts);
        if (missing.Count > 0 || missingParts.Count > 0)
            return Deny(console, comp, actor, Loc.GetString("drydock-console-insufficient-materials", ("missing", FormatBill(missing, missingParts))));

        var applied = new List<(EntityUid Uid, Dictionary<string, int> Draw)>();
        foreach (var (source, draw) in draws)
        {
            if (!TryChangeLocal(source, draw.ToDictionary(p => p.Key, p => -p.Value)))
            {
                foreach (var (uid, undo) in applied)
                    TryChangeLocal(uid, undo);

                return Deny(console, comp, actor, Loc.GetString("drydock-console-transaction-failed"));
            }

            applied.Add((source, draw));
        }

        TakeParts(console, cranes, parts);
        return true;
    }

    /// <summary>
    /// Returns the items' bill to the first crane with storage, or to its linked silo, and their parts to a parts silo;
    /// gives both.
    /// </summary>
    private (Dictionary<string, int> Materials, Dictionary<string, int> Parts) ReturnMaterials(EntityUid console,
        List<EntityUid> cranes, List<WFDrydockWorkItem> items)
    {
        var bill = SumBills(items);
        var parts = SumParts(items);
        ReturnParts(cranes, parts);
        if (bill.Count == 0)
            return (bill, parts);

        var crane = cranes.FirstOrDefault(c => HasComp<MaterialStorageComponent>(c));
        if (!crane.Valid || !TryChangeLocal(CraneSilo(crane) ?? crane, bill))
            Log.Warning($"Could not return drydock materials to {ToPrettyString(console)}; its cranes or silo are gone or full.");

        return (bill, parts);
    }

    /// <summary>Lists a material bill as localized names and sheet amounts, comma separated.</summary>
    private string FormatMaterials(Dictionary<string, int> bill)
    {
        var entries = new List<string>();
        foreach (var (id, amount) in bill)
        {
            var proto = _prototype.Index<MaterialPrototype>(id);
            var sheets = amount / (float) _materials.GetSheetVolume(proto);
            entries.Add(Loc.GetString("drydock-console-material-amount",
                ("name", Loc.GetString(proto.Name)),
                ("sheets", sheets.ToString("0.##", CultureInfo.InvariantCulture))));
        }

        return string.Join(", ", entries);
    }
}
