using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.EntityEffects;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Wolfmed.EntityEffects;

/// <summary>
/// An antibiotic working through the bloodstream: clears contamination in every infected wound and pulls
/// a septic patient back. The only thing that reaches an infection the wound's own dressing cannot.
/// </summary>
public sealed partial class WolfmedTreatInfection : EntityEffect
{
    /// <summary>Dose per metabolism tick, before reagent scaling.</summary>
    [DataField]
    public float Units = 1f;

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
        => Loc.GetString("reagent-effect-guidebook-treat-infection", ("chance", Probability));

    public override void Effect(EntityEffectBaseArgs args)
    {
        var scale = args is EntityEffectReagentArgs reagent ? reagent.Scale.Float() : 1f;
        var treated = new WolfmedAntibioticEvent(Units * scale);
        args.EntityManager.EventBus.RaiseLocalEvent(args.TargetEntity, ref treated);
    }
}
