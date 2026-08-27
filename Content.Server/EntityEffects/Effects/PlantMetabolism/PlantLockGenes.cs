using Content.Server.Botany.Components;
using Content.Server.Botany.Systems;
using Content.Shared.EntityEffects;
using JetBrains.Annotations;
using Robust.Shared.Prototypes;

namespace Content.Server.EntityEffects.Effects.PlantMetabolism;

/// <summary>
/// Locks the plant's genome so mutagen no longer applies random mutations.
/// </summary>
[UsedImplicitly]
[DataDefinition]
public sealed partial class PlantLockGenes : EntityEffect
{
    public override void Effect(EntityEffectBaseArgs args)
    {
        if (!args.EntityManager.TryGetComponent(args.TargetEntity, out PlantHolderComponent? plantHolderComp)
            || plantHolderComp.Seed == null
            || plantHolderComp.Dead)
            return;

        var plantHolder = args.EntityManager.System<PlantHolderSystem>();
        plantHolder.EnsureUniqueSeed(args.TargetEntity, plantHolderComp);
        if (plantHolderComp.Seed == null)
            return;

        plantHolderComp.Seed.GeneLocked = true;
        plantHolderComp.MutationLevel = 0;
    }

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
        => Loc.GetString("reagent-effect-guidebook-plant-lock-genes", ("chance", Probability));
}
