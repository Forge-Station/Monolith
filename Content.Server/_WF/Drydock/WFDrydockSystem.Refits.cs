using System.Linq;
using Content.Shared._WF.Drydock;

namespace Content.Server._WF.Drydock;

/// <summary>
/// Ship refits at the drydock: selling a line's next tier, restoring owned tiers in repairs, and recording what a ship
/// owns once the work is done.
/// </summary>
public sealed partial class WFDrydockSystem
{
    /// <summary>
    /// Adds the work that brings each owned refit back to tier: its share of every machine this repair rebuilds stock,
    /// then a refit item for each target below tier. Covered units cost the console's repair share.
    /// </summary>
    private void AddRefitRepairs(EntityUid ship, WFDrydockConsoleComponent comp, List<WFDrydockWorkItem> items)
    {
        var rebuilds = items.Where(i => i.Kind == WFDrydockWorkKind.Entity && i.Prototype != null).ToList();
        foreach (var (refit, owned) in _refits.Owned(ship))
        {
            var survey = _refits.Survey(ship, refit, owned.Tier);
            var left = _refits.CoverageLeft(ship, refit, survey);

            foreach (var rebuild in rebuilds)
            {
                var lacking = _refits.Prospect(refit, rebuild.Prototype!.Value, owned.Tier);
                if (lacking.Sum() == 0)
                    continue;

                var materials = new Dictionary<string, int>();
                var full = new int[lacking.Length];
                var cost = _refits.Price(refit, owned.Tier, lacking, ref left, comp.RefitRepairShare, materials, full);
                rebuild.Cost += cost;
                rebuild.RefitCost += cost;
                rebuild.RefitMaterials ??= new Dictionary<string, int>();
                AddBill(rebuild.RefitMaterials, materials);
                rebuild.RefitUnits ??= new List<WFDrydockRefitUnits>();
                rebuild.RefitUnits.Add(new WFDrydockRefitUnits(refit.ID, owned.Tier, full));
            }

            foreach (var target in survey.Below)
            {
                items.Add(RefitItem(refit, owned.Tier, target, ref left, comp));
            }
        }
    }

    /// <summary>Lists the work to buy a refit line's next tier, or the reason it can't be sold.</summary>
    private string? BuildRefitWork(EntityUid ship, WFDrydockConsoleComponent comp, string refitId, out List<WFDrydockWorkItem> items)
    {
        items = new List<WFDrydockWorkItem>();
        if (!comp.Refits.Contains(refitId) ||!_prototype.TryIndex<WFShipRefitPrototype>(refitId, out var refit))
            return Loc.GetString("drydock-console-unknown-refit");

        var tier = _refits.NextTier(ship, refit, out var survey);
        if (tier == 0)
        {
            return survey != null && WFShipRefitSystem.Units(survey) == 0
                ? Loc.GetString("drydock-console-no-parts")
                : Loc.GetString("drydock-console-refit-maxed");
        }

        var owned = _refits.GetRefit(ship, refit.ID).Tier;
        var left = _refits.CoverageLeft(ship, refit, survey!);
        foreach (var target in survey!.Below)
        {
            items.Add(RefitItem(refit, owned, target, ref left, comp));
        }

        return null;
    }

    /// <summary>A refit item bringing one target up to its survey's tier, priced against the ship's coverage.</summary>
    private WFDrydockWorkItem RefitItem(WFShipRefitPrototype refit, int owned, WFRefitTarget target, ref int left, WFDrydockConsoleComponent comp)
    {
        var materials = new Dictionary<string, int>();
        var full = new int[target.Lacking.Length];
        var cost = _refits.Price(refit, owned, target.Lacking, ref left, comp.RefitRepairShare, materials, full);
        return new WFDrydockWorkItem
        {
            Kind = WFDrydockWorkKind.Upgrade,
            Target = target.Entity,
            Upgrade = refit.ID,
            Tier = target.Lacking.Length,
            Cost = cost,
            RefitCost = cost,
            RefitMaterials = materials,
            RefitUnits = new List<WFDrydockRefitUnits> { new(refit.ID, target.Lacking.Length, full) },
            LocalPos = target.LocalPos,
        };
    }

    /// <summary>
    /// One refit line for the console: the tier the ship owns, its units, and the price of the next tier, with the
    /// parts the cranes would take for it.
    /// </summary>
    private WFDrydockUpgradeEntry BuildRefitEntry(WFShipRefitPrototype refit, EntityUid? ship, WFDrydockConsoleComponent comp,
        WFDrydockMaterialPaymentComponent? pay, List<EntityUid> cranes, bool scan)
    {
        var entry = new WFDrydockUpgradeEntry
        {
            Id = refit.ID,
            Name = Loc.GetString(refit.Name),
            Mark = refit.Mark is { } mark ? Loc.GetString(mark) : null,
            Lamp = refit.Lamp is { } lamp ? Loc.GetString(lamp) : null,
            MaxTier = refit.Tiers.Count,
        };

        if (ship is not { } shipUid)
            return entry;

        var owned = _refits.GetRefit(shipUid, refit.ID).Tier;
        entry.Tier = owned;

        // Skipped during a job, when the refit keys are disabled.
        if (!scan || refit.Tiers.Count == 0)
            return entry;

        entry.NextTier = _refits.NextTier(shipUid, refit, out var survey);
        survey ??= _refits.Survey(shipUid, refit, Math.Max(1, owned));
        entry.Units = WFShipRefitSystem.Units(survey);
        if (entry.NextTier == 0)
            return entry;

        var left = _refits.CoverageLeft(shipUid, refit, survey);
        var items = new List<WFDrydockWorkItem>();
        foreach (var target in survey.Below)
        {
            items.Add(RefitItem(refit, owned, target, ref left, comp));
        }

        entry.NextPrice = items.Sum(i => i.Cost);
        if (pay != null)
        {
            foreach (var item in items)
            {
                item.Materials = RefitBill(pay, item);
            }

            AssignParts(cranes, items);
            entry.NextMaterials = SumBills(items);
            entry.NextParts = SumParts(items);
        }

        return entry;
    }

    /// <summary>
    /// Records the refit lines a job brought up. A tier is bought only by a job that finished with nothing skipped; the
    /// coverage then counts the units that reach it. Any job widens the coverage to the units now at the owned tier.
    /// </summary>
    private void RecordRefits(WFDrydockJobComponent job, JobEnd end)
    {
        if (TerminatingOrDeleted(job.Ship))
            return;

        foreach (var (refitId, tier) in job.Refits)
        {
            if (!_prototype.TryIndex<WFShipRefitPrototype>(refitId, out var refit))
                continue;

            var (owned, coverage) = _refits.GetRefit(job.Ship, refitId);
            var bought = end == JobEnd.Finished && job.Skipped == 0 && tier > owned;
            var newTier = bought ? tier : owned;
            if (newTier == 0)
                continue;

            var reaching = _refits.Survey(job.Ship, refit, newTier).UnitsAtTier;
            _refits.SetRefit(job.Ship, refitId, newTier, bought ? reaching : Math.Max(coverage, reaching));
        }
    }
}
