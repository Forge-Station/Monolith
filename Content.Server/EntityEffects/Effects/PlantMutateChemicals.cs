using Content.Server.Botany;
using Content.Server.Botany.Components;
using Content.Shared.EntityEffects;
using Content.Shared.Random;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server.EntityEffects.Effects;

/// <summary>
///     Changes the chemicals available in a plant's produce. Common reagents come from
///     RandomPickBotanyReagent; unique phytochemicals only appear when the plant already
///     meets extra mutation / environment conditions.
/// </summary>
public sealed partial class PlantMutateChemicals : EntityEffect
{
    public override void Effect(EntityEffectBaseArgs args)
    {
        var plantholder = args.EntityManager.GetComponent<PlantHolderComponent>(args.TargetEntity);
        if (plantholder.Seed == null)
            return;

        var random = IoCManager.Resolve<IRobustRandom>();
        var prototypeManager = IoCManager.Resolve<IPrototypeManager>();
        var chemicals = plantholder.Seed.Chemicals;

        if (random.Prob(0.6f) && TryAddSpecialReagent(plantholder.Seed, chemicals, random))
            return;

        var randomChems = prototypeManager.Index<WeightedRandomFillSolutionPrototype>("RandomPickBotanyReagent").Fills;
        if (randomChems == null)
            return;

        var pick = random.Pick(randomChems);
        var chemicalId = random.Pick(pick.Reagents);
        AddOrBoostChemical(chemicals, chemicalId, random.Next(1, (int)pick.Quantity));
    }

    private static bool TryAddSpecialReagent(SeedData seed, Dictionary<string, SeedChemQuantity> chemicals, IRobustRandom random)
    {
        var candidates = new List<string>();
        foreach (var (id, condition) in SpecialReagents)
        {
            if (condition(seed) && !chemicals.ContainsKey(id))
                candidates.Add(id);
        }

        if (candidates.Count == 0)
            return false;

        AddOrBoostChemical(chemicals, random.Pick(candidates), random.Next(1, 4));
        return true;
    }

    private static void AddOrBoostChemical(Dictionary<string, SeedChemQuantity> chemicals, string chemicalId, int amount)
    {
        var seedChemQuantity = new SeedChemQuantity();
        if (chemicals.TryGetValue(chemicalId, out var existing))
        {
            seedChemQuantity.Min = existing.Min;
            seedChemQuantity.Max = existing.Max + amount;
            seedChemQuantity.Inherent = existing.Inherent;
        }
        else
        {
            seedChemQuantity.Min = 1;
            seedChemQuantity.Max = 1 + amount;
            seedChemQuantity.Inherent = false;
        }

        seedChemQuantity.PotencyDivisor = (int)Math.Ceiling(100.0f / Math.Max(seedChemQuantity.Max, 1));
        chemicals[chemicalId] = seedChemQuantity;
    }

    /// <summary>
    /// Unique reagents that only appear from plants with matching mutations or growing conditions.
    /// </summary>
    private static readonly (string Id, Func<SeedData, bool> Condition)[] SpecialReagents =
    {
        ("PhytoAether", s => s.Potency >= 50f),
        ("LigninResin", s => s.Ligneous),
        ("CryoChlorophyll", s => s.IdealHeat <= 280f),
        ("Photozyme", s => s.IdealLight >= 8f),
        ("NecrotoxinSap", s => s.TurnIntoKudzu),
        ("RadbloomExtract", s => s.Radioactive),
        ("VineIchor", s => s.CarnivorousGrab),
        ("AtmoPhyte", s => s.ExudeGasses.Count > 0 || s.ConsumeGasses.Count > 0),
    };

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
    {
        return Loc.GetString("plant-mutation-chemicals-guidebook");
    }
}
