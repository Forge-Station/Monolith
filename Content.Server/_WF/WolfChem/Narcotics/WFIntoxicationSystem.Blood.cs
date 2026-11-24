using Content.Server.Body.Components;
using Content.Shared._WF.WolfChem.Narcotics;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.WolfChem.Narcotics;

/// <summary>
/// Level from the blood: a reagent with a <see cref="WFDose"/> gives its profile the units left in the chemical stream times their grade times its level per unit, so the high lasts as long as the drug.
/// </summary>
public sealed partial class WFIntoxicationSystem
{
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;

    /// <summary>Each reagent whose level follows the blood, with the profiles it doses. Built on first use.</summary>
    private Dictionary<string, List<(ProtoId<WFDrugProfilePrototype> Profile, float PerUnit)>>? _bloodDrugs;

    /// <summary>This read's level from the blood per profile, before tolerance.</summary>
    private readonly Dictionary<ProtoId<WFDrugProfilePrototype>, float> _blood = new();

    /// <summary>This read's blood reagents, with the profile each doses and its level per unit.</summary>
    private readonly List<(ProtoId<WFDrugProfilePrototype> Profile, ReagentId Reagent, float PerUnit)> _bloodEntries = new();

    private void InitializeBlood()
    {
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(args =>
        {
            if (args.WasModified<ReagentPrototype>())
                _bloodDrugs = null;
        });
    }

    private Dictionary<string, List<(ProtoId<WFDrugProfilePrototype> Profile, float PerUnit)>> BloodDrugs()
    {
        if (_bloodDrugs != null)
            return _bloodDrugs;

        _bloodDrugs = new();
        foreach (var reagent in Proto.EnumeratePrototypes<ReagentPrototype>())
        {
            if (reagent.Metabolisms == null)
                continue;

            foreach (var entry in reagent.Metabolisms.Values)
            {
                foreach (var effect in entry.Effects)
                {
                    if (effect is not WFDose { DependenceOnly: false } dose)
                        continue;

                    if (!_bloodDrugs.TryGetValue(reagent.ID, out var doses))
                        _bloodDrugs[reagent.ID] = doses = new();

                    doses.Add((dose.Profile, dose.PerUnit));
                }
            }
        }

        return _bloodDrugs;
    }

    /// <summary>True when a reagent in the blood raises a profile's level.</summary>
    public bool DosesInBlood(string reagent, ProtoId<WFDrugProfilePrototype> profile)
    {
        return BloodDrugs().TryGetValue(reagent, out var doses) && doses.Exists(dose => dose.Profile == profile);
    }

    /// <summary>The profiles a reagent doses in the blood, with the level a unit gives each.</summary>
    public IReadOnlyList<(ProtoId<WFDrugProfilePrototype> Profile, float PerUnit)> Doses(string reagent)
    {
        return BloodDrugs().TryGetValue(reagent, out var doses) ? doses : Array.Empty<(ProtoId<WFDrugProfilePrototype>, float)>();
    }

    /// <summary>
    /// Reads the drugs in a body's chemical stream into <see cref="_blood"/> and <see cref="_bloodEntries"/>. Returns
    /// the stream, or null for a body without one or a neuroware host, whose blood gives no level.
    /// </summary>
    private Entity<SolutionComponent>? ReadBlood(EntityUid uid)
    {
        _blood.Clear();
        _bloodEntries.Clear();
        if (!TryComp<BloodstreamComponent>(uid, out var bloodstream)
            || !_solutions.TryGetSolution(uid, bloodstream.ChemicalSolutionName, out var soln, out var solution)
            || IsNeurowareHost(uid))
        {
            return null;
        }

        var drugs = BloodDrugs();
        foreach (var (reagent, quantity) in solution.Contents)
        {
            if (!drugs.TryGetValue(reagent.Prototype, out var doses))
                continue;

            var units = quantity.Float() * solution.GetPurity(reagent.Prototype);
            foreach (var (profile, perUnit) in doses)
            {
                _blood[profile] = _blood.GetValueOrDefault(profile) + units * perUnit;
                _bloodEntries.Add((profile, reagent, perUnit));
            }
        }

        return soln;
    }

    /// <summary>Flushes the blood's share of a profile's level above its overdose tier at its overdose half-life.</summary>
    private void Flush(Entity<SolutionComponent> soln, ProtoId<WFDrugProfilePrototype> profile,
        WFDrugProfilePrototype proto, float level, float blood, float seconds)
    {
        if (proto.OverdoseHalfLife <= 0f || proto.Tiers.Count < 2 || blood <= 0f)
            return;

        var excess = level - proto.Tiers[^1].Min;
        if (excess <= 0f)
            return;

        var share = Math.Min(1f, excess / blood) * (1f - MathF.Pow(2f, -seconds / proto.OverdoseHalfLife));
        foreach (var (entryProfile, reagent, _) in _bloodEntries)
        {
            if (entryProfile != profile)
                continue;

            var amount = FixedPoint2.New(soln.Comp.Solution.GetReagentQuantity(reagent).Float() * share);
            if (amount > FixedPoint2.Zero)
                _solutions.RemoveReagent(soln, reagent, amount);
        }
    }

    /// <summary>Also purges the family's drugs from the blood, a unit for each level per unit of the amount.</summary>
    public override void ReduceFamily(EntityUid uid, WFDrugFamily family, float amount)
    {
        base.ReduceFamily(uid, family, amount);
        if (amount <= 0f || ReadBlood(uid) is not { } soln)
            return;

        foreach (var (profile, reagent, perUnit) in _bloodEntries)
        {
            if (perUnit > 0f && Proto.TryIndex(profile, out var proto) && proto.Family == family)
                _solutions.RemoveReagent(soln, reagent, FixedPoint2.New(amount / perUnit));
        }
    }
}
