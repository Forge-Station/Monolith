using Content.Server.Botany;
using Content.Server.Botany.Components;
using Content.Shared.EntityEffects;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server.EntityEffects.Effects;

/// <summary>
/// Adds a specific reagent to harvested produce. Used for rare hunt-able phytochemical niches.
/// </summary>
public sealed partial class PlantMutateAddChemical : EntityEffect
{
    [DataField(required: true)]
    public string Reagent = string.Empty;

    [DataField]
    public int Min = 1;

    [DataField]
    public int Max = 4;

    public override void Effect(EntityEffectBaseArgs args)
    {
        if (!args.EntityManager.TryGetComponent(args.TargetEntity, out PlantHolderComponent? holder) ||
            holder.Seed == null)
            return;

        var random = IoCManager.Resolve<IRobustRandom>();
        var amount = random.Next(Min, Max + 1);
        var chemicals = holder.Seed.Chemicals;

        if (chemicals.TryGetValue(Reagent, out var existing))
        {
            existing.Max += amount;
            existing.PotencyDivisor = (int)Math.Ceiling(100.0f / Math.Max(existing.Max, 1));
            chemicals[Reagent] = existing;
            return;
        }

        chemicals[Reagent] = new SeedChemQuantity
        {
            Min = 1,
            Max = 1 + amount,
            Inherent = false,
            PotencyDivisor = (int)Math.Ceiling(100.0f / Math.Max(1 + amount, 1))
        };
    }

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
    {
        return Loc.GetString("plant-mutation-add-chemical-guidebook", ("reagent", Reagent));
    }
}
