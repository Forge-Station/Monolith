using System.Linq;
using Content.Server._WF.Lathe;
using Content.Shared._WF.Drydock;
using Content.Shared._WF.Lathe;
using Content.Shared.FixedPoint;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Drydock;

/// <summary>
/// Parts and chemicals from the fabrication silos linked to the cranes. A refit unit billed in full takes the stock part
/// it installs from a parts silo, in place of its materials. Chemical silos are read for the console only.
/// </summary>
public sealed partial class WFDrydockSystem
{
    [Dependency] private FabricationSiloSystem _fabricationSilo = default!;
    [Dependency] private WFPartRefitSystem _partRefits = default!;
    [Dependency] private TurfSystem _turf = default!;

    /// <summary>Tiles out from a crane searched for free floor to leave things on.</summary>
    private const int DropSearch = 3;

    /// <summary>
    /// Pays the items' full-price refit units with matching parts from the cranes' parts silos where there are any,
    /// taking each unit's materials off the item's bill.
    /// </summary>
    private void AssignParts(List<EntityUid> cranes, List<WFDrydockWorkItem> items)
    {
        var stock = PartStock(cranes);
        if (stock.Count == 0)
            return;

        foreach (var item in items)
        {
            if (item.RefitUnits == null || item.Materials == null)
                continue;

            foreach (var units in item.RefitUnits)
            {
                if (!_prototype.TryIndex<WFShipRefitPrototype>(units.Refit, out var refit))
                    continue;

                var upgrade = item.Kind == WFDrydockWorkKind.Upgrade;
                var left = (int[]) units.Full.Clone();
                foreach (var part in _partRefits.PartsBelow(refit, units.Tier, upgrade ? item.Target : null, upgrade ? null : item.Prototype))
                {
                    if (part.Reached >= left.Length)
                        continue;

                    var taken = TakeStock(stock, part, Math.Min(part.Count, left[part.Reached]), item);
                    if (taken == 0)
                        continue;

                    left[part.Reached] -= taken;
                    foreach (var (id, amount) in WFShipRefitSystem.UnitMaterials(refit, part.Reached, units.Tier))
                    {
                        var remaining = item.Materials.GetValueOrDefault(id) - amount * taken;
                        if (remaining > 0)
                            item.Materials[id] = remaining;
                        else
                            item.Materials.Remove(id);
                    }
                }
            }
        }
    }

    /// <summary>Moves up to a count of the part a refit installs from the stock onto an item; returns how many.</summary>
    private static int TakeStock(Dictionary<string, int> stock, WFRefitPart part, int wanted, WFDrydockWorkItem item)
    {
        var proto = part.Replacement.Id;
        var taken = Math.Min(wanted, stock.GetValueOrDefault(proto));
        if (taken <= 0)
            return 0;

        stock[proto] -= taken;
        item.Parts ??= new Dictionary<string, int>();
        item.Parts[proto] = item.Parts.GetValueOrDefault(proto) + taken;
        return taken;
    }

    /// <summary>Everything in the cranes' parts silos, by prototype and name, for the console's supplies list.</summary>
    private List<WFDrydockStoredPart> SiloContents(List<EntityUid> cranes)
    {
        var counts = new Dictionary<(string Prototype, string Name), int>();
        foreach (var (silo, _) in PartsSilos(cranes))
        {
            foreach (var part in silo.Comp.Parts!.ContainedEntities)
            {
                var meta = MetaData(part);
                if (meta.EntityPrototype?.ID is not { } id)
                    continue;

                var key = (id, meta.EntityName);
                counts[key] = counts.GetValueOrDefault(key) + _fabricationSilo.GetItemCount(part);
            }
        }

        return counts.Select(c => new WFDrydockStoredPart(c.Key.Prototype, c.Key.Name, c.Value)).ToList();
    }

    /// <summary>
    /// Parts refits install in the cranes' parts silos, by prototype. Other parts of the same type and rating, such as
    /// trade goods, are left alone.
    /// </summary>
    private Dictionary<string, int> PartStock(List<EntityUid> cranes)
    {
        var stock = new Dictionary<string, int>();
        foreach (var (silo, _) in PartsSilos(cranes))
        {
            foreach (var part in silo.Comp.Parts!.ContainedEntities)
            {
                if (MetaData(part).EntityPrototype?.ID is { } id && _partRefits.IsReplacement(id))
                    stock[id] = stock.GetValueOrDefault(id) + _fabricationSilo.GetItemCount(part);
            }
        }

        return stock;
    }

    /// <summary>Every reagent the cranes' chemical silos hold, by prototype.</summary>
    private Dictionary<string, FixedPoint2> StoredChemicals(List<EntityUid> cranes)
    {
        var stored = new Dictionary<string, FixedPoint2>();
        foreach (var (silo, _) in Silos(cranes, FabricationSiloKind.Chemicals))
        {
            if (!_fabricationSilo.TryGetSolution(silo, out _, out var solution))
                continue;

            foreach (var (reagent, quantity) in solution.Contents)
            {
                stored[reagent.Prototype] = stored.GetValueOrDefault(reagent.Prototype) + quantity;
            }
        }

        return stored;
    }

    /// <summary>Each distinct parts silo linked to a crane that can reach it, with one such crane.</summary>
    private List<(Entity<FabricationSiloComponent> Silo, EntityUid Crane)> PartsSilos(List<EntityUid> cranes)
    {
        return Silos(cranes, FabricationSiloKind.Parts).Where(s => s.Silo.Comp.Parts != null).ToList();
    }

    /// <summary>Each distinct silo of a kind linked to a crane that can reach it, with one such crane, in crane order.</summary>
    private List<(Entity<FabricationSiloComponent> Silo, EntityUid Crane)> Silos(List<EntityUid> cranes, FabricationSiloKind kind)
    {
        var silos = new List<(Entity<FabricationSiloComponent>, EntityUid)>();
        foreach (var crane in cranes)
        {
            if (_fabricationSilo.GetLinkedSilo(crane, kind) is { } silo && silos.All(s => s.Item1.Owner != silo.Owner))
                silos.Add((silo, crane));
        }

        return silos;
    }

    /// <summary>Sums the parts work items take.</summary>
    private static Dictionary<string, int> SumParts(IEnumerable<WFDrydockWorkItem> items)
    {
        var parts = new Dictionary<string, int>();
        foreach (var item in items)
        {
            if (item.Parts != null)
                AddBill(parts, item.Parts);
        }

        return parts;
    }

    /// <summary>The parts of a bill the cranes' parts silos are short of.</summary>
    private Dictionary<string, int> MissingParts(List<EntityUid> cranes, Dictionary<string, int> parts)
    {
        var silos = PartsSilos(cranes);
        var missing = new Dictionary<string, int>();
        foreach (var (id, amount) in parts)
        {
            var have = silos.Sum(s => _fabricationSilo.GetPartAmount(s.Crane, id));
            if (have < amount)
                missing[id] = amount - have;
        }

        return missing;
    }

    /// <summary>Takes a bill of parts from the cranes' parts silos; call after <see cref="MissingParts"/> finds none short.</summary>
    private void TakeParts(EntityUid console, List<EntityUid> cranes, Dictionary<string, int> parts)
    {
        var silos = PartsSilos(cranes);
        foreach (var (id, amount) in parts)
        {
            var left = amount;
            foreach (var (_, crane) in silos)
            {
                if (left <= 0)
                    break;

                left -= _fabricationSilo.ConsumeParts(crane, id, left);
            }

            if (left > 0)
                Log.Warning($"Drydock {ToPrettyString(console)} took {amount - left} of {amount} {id} it was paid.");
        }
    }

    /// <summary>
    /// Returns parts to the first crane's parts silo that has room, or drops them at the first crane.
    /// </summary>
    private void ReturnParts(List<EntityUid> cranes, Dictionary<string, int> parts)
    {
        if (parts.Count == 0 || cranes.FirstOrDefault(c => !TerminatingOrDeleted(c)) is not { Valid: true } drop)
            return;

        var silos = PartsSilos(cranes);
        var coords = DropSpot(drop);
        foreach (var (id, amount) in parts)
        {
            for (var i = 0; i < amount; i++)
            {
                // Left at the crane when no silo takes it.
                var part = Spawn(id, coords);
                foreach (var (_, crane) in silos)
                {
                    if (_fabricationSilo.ReturnPart(crane, part))
                        break;
                }
            }
        }
    }

    /// <summary>
    /// Where things a crane can't store are left: the free floor tile nearest it, so they aren't hidden under its
    /// footprint. The crane's own position when no tile nearby is free.
    /// </summary>
    private EntityCoordinates DropSpot(EntityUid crane)
    {
        var xform = Transform(crane);
        if (xform.GridUid is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var grid))
            return xform.Coordinates;

        var origin = _map.TileIndicesFor(gridUid, grid, xform.Coordinates);
        Vector2i? best = null;
        var bestDistance = int.MaxValue;
        for (var dx = -DropSearch; dx <= DropSearch; dx++)
        {
            for (var dy = -DropSearch; dy <= DropSearch; dy++)
            {
                var distance = dx * dx + dy * dy;
                var tile = origin + new Vector2i(dx, dy);
                if (distance >= bestDistance ||
                    _map.GetTileRef(gridUid, grid, tile).Tile.IsEmpty ||
                    _turf.IsTileBlocked(gridUid, tile, CollisionGroup.Impassable | CollisionGroup.MidImpassable, grid))
                    continue;

                best = tile;
                bestDistance = distance;
            }
        }

        return best is { } free ? _map.GridTileToLocal(gridUid, grid, free) : xform.Coordinates;
    }

    /// <summary>Lists parts as localized names and counts, comma separated.</summary>
    private string FormatParts(Dictionary<string, int> parts)
    {
        return string.Join(", ", parts.Select(p => Loc.GetString("drydock-console-part-amount",
            ("name", _prototype.TryIndex<EntityPrototype>(p.Key, out var proto) ? proto.Name : p.Key),
            ("count", p.Value))));
    }

    /// <summary>Lists a bill of materials and parts, materials first.</summary>
    private string FormatBill(Dictionary<string, int> materials, Dictionary<string, int> parts)
    {
        return string.Join(", ", new[] { FormatMaterials(materials), FormatParts(parts) }.Where(s => s.Length > 0));
    }
}
