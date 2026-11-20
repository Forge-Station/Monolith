using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.EntityEffects;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Wolfmed.EntityEffects;

/// <summary>
/// Rinses corrosive residue off a wound host. Sits on the humanoid base's existing water touch reaction,
/// so a splash, a spray bottle, an extinguisher, a puddle and any shower this fork gains all wash a
/// chemical burn without a second code path.
/// </summary>
public sealed partial class WashChemicalBurns : EntityEffect
{
    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
        => Loc.GetString("reagent-effect-guidebook-wash-chemical-burns", ("chance", Probability));

    public override void Effect(EntityEffectBaseArgs args)
    {
        args.EntityManager.System<WolfmedChemicalBurnSystem>().Wash(args.TargetEntity);
    }
}
