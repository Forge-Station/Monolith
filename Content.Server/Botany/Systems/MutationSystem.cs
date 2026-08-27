using Content.Shared.Atmos;
using Content.Shared.EntityEffects;
using Content.Shared.Random;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using System.Linq;

namespace Content.Server.Botany;

public sealed partial class MutationSystem : EntitySystem
{
    [Dependency] private IRobustRandom _robustRandom = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    private RandomPlantMutationListPrototype _randomMutations = default!;

    public override void Initialize()
    {
        _randomMutations = _prototypeManager.Index<RandomPlantMutationListPrototype>("RandomPlantMutations");
    }

    /// <summary>
    /// For each random mutation, see if it occurs on this plant this check.
    /// </summary>
    /// <param name="seed"></param>
    /// <param name="severity"></param>
    public void CheckRandomMutations(EntityUid plantHolder, ref SeedData seed, float severity)
    {
        foreach (var mutation in _randomMutations.mutations)
        {
            if (Random(Math.Min(mutation.BaseOdds * severity, 1.0f)))
            {
                if (mutation.AppliesToPlant)
                {
                    var args = new EntityEffectBaseArgs(plantHolder, EntityManager);
                    mutation.Effect.Effect(args);
                }
                // Stat adjustments do not persist by being an attached effect, they just change the stat.
                if (mutation.Persists && !seed.Mutations.Any(m => m.Name == mutation.Name))
                    seed.Mutations.Add(mutation);
            }
        }
    }

    /// <summary>
    /// Checks all defined mutations against a seed to see which of them are applied.
    /// </summary>
    public void MutateSeed(EntityUid plantHolder, ref SeedData seed, float severity)
    {
        if (!seed.Unique)
        {
            Log.Error($"Attempted to mutate a shared seed");
            return;
        }

        if (seed.GeneLocked)
            return;

        CheckRandomMutations(plantHolder, ref seed, severity);
    }

    public SeedData Cross(SeedData a, SeedData b)
    {
        SeedData result = b.Clone();

        CrossChemicals(ref result.Chemicals, a.Chemicals);

        CrossFloat(ref result.NutrientConsumption, a.NutrientConsumption);
        CrossFloat(ref result.WaterConsumption, a.WaterConsumption);
        CrossFloat(ref result.IdealHeat, a.IdealHeat);
        CrossFloat(ref result.HeatTolerance, a.HeatTolerance);
        CrossFloat(ref result.IdealLight, a.IdealLight);
        CrossFloat(ref result.LightTolerance, a.LightTolerance);
        CrossFloat(ref result.ToxinsTolerance, a.ToxinsTolerance);
        CrossFloat(ref result.LowPressureTolerance, a.LowPressureTolerance);
        CrossFloat(ref result.HighPressureTolerance, a.HighPressureTolerance);
        CrossFloat(ref result.PestTolerance, a.PestTolerance);
        CrossFloat(ref result.WeedTolerance, a.WeedTolerance);

        CrossFloat(ref result.Endurance, a.Endurance);
        CrossInt(ref result.Yield, a.Yield);
        CrossFloat(ref result.Lifespan, a.Lifespan);
        CrossFloat(ref result.Maturation, a.Maturation);
        CrossFloat(ref result.Production, a.Production);
        CrossFloat(ref result.Potency, a.Potency);

        if (a.HarvestRepeat > result.HarvestRepeat)
            result.HarvestRepeat = a.HarvestRepeat;

        CrossBool(ref result.Seedless, a.Seedless);
        CrossBool(ref result.Ligneous, a.Ligneous);
        CrossBool(ref result.TurnIntoKudzu, a.TurnIntoKudzu);
        CrossBool(ref result.CanScream, a.CanScream);
        CrossBool(ref result.Radioactive, a.Radioactive);
        CrossBool(ref result.CarnivorousGrab, a.CarnivorousGrab);
        CrossBool(ref result.CarnivorousPestEater, a.CarnivorousPestEater);
        CrossBool(ref result.GeneLocked, a.GeneLocked);
        CrossFloat(ref result.RadiationIntensity, a.RadiationIntensity);
        CrossFloat(ref result.GrabRange, a.GrabRange);

        CrossGasses(ref result.ExudeGasses, a.ExudeGasses);
        CrossGasses(ref result.ConsumeGasses, a.ConsumeGasses);

        // Frontier: ensure clip/swab/seed safety propagates
        result.PreventClipping |= a.PreventClipping;
        result.PreventSwabbing |= a.PreventSwabbing;
        result.PermanentlySeedless |= a.PermanentlySeedless;
        // End Frontier

        // LINQ Explanation
        // For the list of mutation effects on both plants, use a 50% chance to pick each one.
        // Union all of the chosen mutations into one list, and pick ones with a Distinct (unique) name.
        result.Mutations = result.Mutations.Where(m => Random(0.5f)).Union(a.Mutations.Where(m => Random(0.5f))).DistinctBy(m => m.Name).ToList();

        // Hybrids have a high chance of being seedless. Balances very
        // effective hybrid crossings.
        if (a.Name != result.Name && Random(0.7f))
        {
            result.Seedless = true;
        }

        return result;
    }

    /// <summary>
    /// Copy a seed for pollen / planting without sharing the live tray reference.
    /// </summary>
    public SeedData Duplicate(SeedData seed)
    {
        return seed.Clone();
    }

    /// <summary>
    /// Copy mutation traits from pollen onto a host plant, keeping the host species.
    /// Does not apply the hybrid seedless tax.
    /// </summary>
    public SeedData Graft(SeedData donor, SeedData host)
    {
        var result = host.Clone();

        result.Ligneous |= donor.Ligneous;
        result.CanScream |= donor.CanScream;
        result.TurnIntoKudzu |= donor.TurnIntoKudzu;
        result.Radioactive |= donor.Radioactive;
        result.CarnivorousGrab |= donor.CarnivorousGrab;
        result.CarnivorousPestEater |= donor.CarnivorousPestEater;
        result.GeneLocked |= donor.GeneLocked;
        if (donor.Radioactive)
            result.RadiationIntensity = MathF.Max(result.RadiationIntensity, donor.RadiationIntensity);
        if (donor.CarnivorousGrab)
            result.GrabRange = MathF.Max(result.GrabRange, donor.GrabRange);
        if (donor.HarvestRepeat > result.HarvestRepeat)
            result.HarvestRepeat = donor.HarvestRepeat;

        MergeChemicals(result.Chemicals, donor.Chemicals);
        MergeGasses(result.ConsumeGasses, donor.ConsumeGasses);
        MergeGasses(result.ExudeGasses, donor.ExudeGasses);

        result.Mutations = result.Mutations.Union(donor.Mutations).DistinctBy(m => m.Name).ToList();

        CrossFloat(ref result.Potency, donor.Potency);
        CrossInt(ref result.Yield, donor.Yield);
        CrossFloat(ref result.Endurance, donor.Endurance);
        CrossFloat(ref result.Lifespan, donor.Lifespan);
        CrossFloat(ref result.IdealHeat, donor.IdealHeat);
        CrossFloat(ref result.IdealLight, donor.IdealLight);
        CrossFloat(ref result.WaterConsumption, donor.WaterConsumption);
        CrossFloat(ref result.NutrientConsumption, donor.NutrientConsumption);

        return result;
    }

    private static void MergeChemicals(Dictionary<string, SeedChemQuantity> host, Dictionary<string, SeedChemQuantity> donor)
    {
        foreach (var (id, chem) in donor)
        {
            if (host.TryGetValue(id, out var existing))
            {
                if (chem.Max > existing.Max)
                {
                    var copy = chem;
                    copy.Inherent = false;
                    host[id] = copy;
                }

                continue;
            }

            var added = chem;
            added.Inherent = false;
            host[id] = added;
        }
    }

    private static void MergeGasses(Dictionary<Gas, float> host, Dictionary<Gas, float> donor)
    {
        foreach (var (gas, amount) in donor)
        {
            if (host.TryGetValue(gas, out var existing))
                host[gas] = MathF.Max(existing, amount);
            else
                host[gas] = amount;
        }
    }

    private void CrossChemicals(ref Dictionary<string, SeedChemQuantity> val, Dictionary<string, SeedChemQuantity> other)
    {
        // Go through chemicals from the pollen in swab
        foreach (var otherChem in other)
        {
            // if both have same chemical, randomly pick potency ratio from the two.
            if (val.ContainsKey(otherChem.Key))
            {
                val[otherChem.Key] = Random(0.5f) ? otherChem.Value : val[otherChem.Key];
            }
            // if target plant doesn't have this chemical, has 50% chance to add it.
            else
            {
                if (Random(0.5f))
                {
                    var fixedChem = otherChem.Value;
                    fixedChem.Inherent = false;
                    val.Add(otherChem.Key, fixedChem);
                }
            }
        }

        // if the target plant has chemical that the pollen in swab does not, 50% chance to remove it.
        foreach (var thisChem in val)
        {
            if (!other.ContainsKey(thisChem.Key))
            {
                if (Random(0.5f))
                {
                    if (val.Count > 1)
                    {
                        val.Remove(thisChem.Key);
                    }
                }
            }
        }
    }

    private void CrossGasses(ref Dictionary<Gas, float> val, Dictionary<Gas, float> other)
    {
        // Go through gasses from the pollen in swab
        foreach (var otherGas in other)
        {
            // if both have same gas, randomly pick ammount from the two.
            if (val.ContainsKey(otherGas.Key))
            {
                val[otherGas.Key] = Random(0.5f) ? otherGas.Value : val[otherGas.Key];
            }
            // if target plant doesn't have this gas, has 50% chance to add it.
            else
            {
                if (Random(0.5f))
                {
                    val.Add(otherGas.Key, otherGas.Value);
                }
            }
        }
        // if the target plant has gas that the pollen in swab does not, 50% chance to remove it.
        foreach (var thisGas in val)
        {
            if (!other.ContainsKey(thisGas.Key))
            {
                if (Random(0.5f))
                {
                    val.Remove(thisGas.Key);
                }
            }
        }
    }
    private void CrossFloat(ref float val, float other)
    {
        val = Random(0.5f) ? val : other;
    }

    private void CrossInt(ref int val, int other)
    {
        val = Random(0.5f) ? val : other;
    }

    private void CrossBool(ref bool val, bool other)
    {
        val = Random(0.5f) ? val : other;
    }

    private bool Random(float p)
    {
        return _robustRandom.Prob(p);
    }
}
