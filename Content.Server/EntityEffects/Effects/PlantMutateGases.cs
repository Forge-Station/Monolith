using Content.Server.Botany;
using Content.Server.Botany.Components;
using Content.Shared.Atmos;
using Content.Shared.EntityEffects;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server.EntityEffects.Effects;

/// <summary>
///     Weighted gas mutation: rarer gases have a much lower chance.
///     Also shifts the plant's atmospheric growing requirements to match the new gas.
/// </summary>
public sealed partial class PlantMutateExudeGasses : EntityEffect
{
    [DataField]
    public float MinValue = 0.05f;

    [DataField]
    public float MaxValue = 0.8f;

    public override void Effect(EntityEffectBaseArgs args)
    {
        var plantholder = args.EntityManager.GetComponent<PlantHolderComponent>(args.TargetEntity);
        if (plantholder.Seed == null)
            return;

        var random = IoCManager.Resolve<IRobustRandom>();
        var gas = PlantGasMutationHelpers.PickWeightedGas(random);
        var amount = random.NextFloat(MinValue, MaxValue);
        PlantGasMutationHelpers.AddGas(plantholder.Seed.ExudeGasses, gas, amount);
        PlantGasMutationHelpers.ApplyAtmosSideEffects(plantholder.Seed, gas, exude: true);
    }

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
    {
        return Loc.GetString("plant-mutation-exude-gas-guidebook");
    }
}

/// <summary>
///     Changes the gases that a plant consumes and retunes its growing atmosphere.
/// </summary>
public sealed partial class PlantMutateConsumeGasses : EntityEffect
{
    [DataField]
    public float MinValue = 0.01f;

    [DataField]
    public float MaxValue = 0.25f;

    public override void Effect(EntityEffectBaseArgs args)
    {
        var plantholder = args.EntityManager.GetComponent<PlantHolderComponent>(args.TargetEntity);
        if (plantholder.Seed == null)
            return;

        var random = IoCManager.Resolve<IRobustRandom>();
        var gas = PlantGasMutationHelpers.PickWeightedGas(random);
        var amount = random.NextFloat(MinValue, MaxValue);
        PlantGasMutationHelpers.AddGas(plantholder.Seed.ConsumeGasses, gas, amount);
        PlantGasMutationHelpers.ApplyAtmosSideEffects(plantholder.Seed, gas, exude: false);
    }

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
    {
        return Loc.GetString("plant-mutation-consume-gas-guidebook");
    }
}

public static class PlantGasMutationHelpers
{
    /// <summary>
    /// Combined odds stay at or below ~1% for common gases and drop sharply for rare ones.
    /// Weights are relative; the mutation itself already has a small baseOdds.
    /// </summary>
    private static readonly (Gas Gas, float Weight)[] WeightedGases =
    {
        (Gas.Oxygen, 24f),
        (Gas.Nitrogen, 24f),
        (Gas.CarbonDioxide, 22f),
        (Gas.WaterVapor, 14f),
        (Gas.Ammonia, 7f),
        (Gas.NitrousOxide, 4f),
        (Gas.Plasma, 2.2f),
        (Gas.Tritium, 1.1f),
        (Gas.BZ, 0.6f),
        (Gas.Pluoxium, 0.4f),
        (Gas.Healium, 0.3f),
        (Gas.Nitrium, 0.25f),
        (Gas.Frezon, 0.15f),
    };

    public static Gas PickWeightedGas(IRobustRandom random)
    {
        var total = 0f;
        foreach (var (_, weight) in WeightedGases)
            total += weight;

        var roll = random.NextFloat(0f, total);
        foreach (var (gas, weight) in WeightedGases)
        {
            roll -= weight;
            if (roll <= 0f)
                return gas;
        }

        return Gas.Oxygen;
    }

    public static void AddGas(Dictionary<Gas, float> gasses, Gas gas, float amount)
    {
        if (gasses.ContainsKey(gas))
            gasses[gas] += amount;
        else
            gasses.Add(gas, amount);
    }

    public static void ApplyAtmosSideEffects(SeedData seed, Gas gas, bool exude)
    {
        switch (gas)
        {
            case Gas.Plasma:
                seed.IdealHeat = Math.Clamp(seed.IdealHeat + 12f, 270f, 340f);
                seed.HeatTolerance = Math.Clamp(seed.HeatTolerance + 4f, 4f, 30f);
                break;
            case Gas.Tritium:
                seed.IdealHeat = Math.Clamp(seed.IdealHeat + 8f, 270f, 340f);
                seed.HighPressureTolerance = Math.Clamp(seed.HighPressureTolerance + 8f, 100f, 180f);
                break;
            case Gas.Frezon:
            case Gas.NitrousOxide:
            case Gas.Healium:
                seed.IdealHeat = Math.Clamp(seed.IdealHeat - 14f, 250f, 320f);
                seed.HeatTolerance = Math.Clamp(seed.HeatTolerance + 6f, 4f, 30f);
                break;
            case Gas.WaterVapor:
                seed.WaterConsumption = Math.Clamp(seed.WaterConsumption + (exude ? -0.08f : 0.12f), 0.15f, 1.2f);
                seed.IdealHeat = Math.Clamp(seed.IdealHeat + 4f, 270f, 330f);
                break;
            case Gas.Ammonia:
                seed.NutrientConsumption = Math.Clamp(seed.NutrientConsumption + (exude ? -0.1f : 0.15f), 0.05f, 1.4f);
                break;
            case Gas.CarbonDioxide:
                if (!exude)
                    seed.IdealLight = Math.Clamp(seed.IdealLight + 1f, 2f, 12f);
                break;
            case Gas.Oxygen:
                seed.LowPressureTolerance = Math.Clamp(seed.LowPressureTolerance - 4f, 50f, 100f);
                break;
            case Gas.BZ:
            case Gas.Nitrium:
                seed.ToxinsTolerance = Math.Clamp(seed.ToxinsTolerance + 1.5f, 1f, 12f);
                seed.PestTolerance = Math.Clamp(seed.PestTolerance + 1f, 0f, 15f);
                break;
            case Gas.Pluoxium:
                seed.IdealLight = Math.Clamp(seed.IdealLight - 1f, 1f, 12f);
                break;
        }
    }
}

/// <summary>
/// Adds a specific consumed gas (not a weighted random pick). Used for useful niches like CO2 filters.
/// </summary>
public sealed partial class PlantAddConsumeGas : EntityEffect
{
    [DataField(required: true)]
    public Gas Gas;

    [DataField]
    public float MinValue = 0.08f;

    [DataField]
    public float MaxValue = 0.2f;

    public override void Effect(EntityEffectBaseArgs args)
    {
        if (!args.EntityManager.TryGetComponent(args.TargetEntity, out PlantHolderComponent? holder) ||
            holder.Seed == null)
            return;

        var random = IoCManager.Resolve<IRobustRandom>();
        var amount = random.NextFloat(MinValue, MaxValue);
        PlantGasMutationHelpers.AddGas(holder.Seed.ConsumeGasses, Gas, amount);
        PlantGasMutationHelpers.ApplyAtmosSideEffects(holder.Seed, Gas, exude: false);
    }

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
    {
        return Loc.GetString("plant-mutation-add-consume-gas-guidebook", ("gas", Gas.ToString()));
    }
}

/// <summary>
/// Adds a specific exuded gas. Used for useful niches like oxygen-producing plants.
/// </summary>
public sealed partial class PlantAddExudeGas : EntityEffect
{
    [DataField(required: true)]
    public Gas Gas;

    [DataField]
    public float MinValue = 0.08f;

    [DataField]
    public float MaxValue = 0.25f;

    public override void Effect(EntityEffectBaseArgs args)
    {
        if (!args.EntityManager.TryGetComponent(args.TargetEntity, out PlantHolderComponent? holder) ||
            holder.Seed == null)
            return;

        var random = IoCManager.Resolve<IRobustRandom>();
        var amount = random.NextFloat(MinValue, MaxValue);
        PlantGasMutationHelpers.AddGas(holder.Seed.ExudeGasses, Gas, amount);
        PlantGasMutationHelpers.ApplyAtmosSideEffects(holder.Seed, Gas, exude: true);
    }

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
    {
        return Loc.GetString("plant-mutation-add-exude-gas-guidebook", ("gas", Gas.ToString()));
    }
}
