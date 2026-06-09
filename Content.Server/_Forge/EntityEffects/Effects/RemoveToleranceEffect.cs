using Content.Shared.EntityEffects;
using Robust.Shared.Prototypes;
using Content.Server._Forge.Physiology.Tolerance;
using Content.Shared._Forge.Physiology.Disease;

namespace Content.Server._Forge.EntityEffects.Effects;

public sealed partial class RemoveToleranceEffect : EntityEffect
{
    [DataField(required: true)]
    public string Reagent = string.Empty;

    [DataField]
    public float Amount = 0.01f;

    [DataField]
    public Organ Target = Organ.Body;

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
    {
        return Loc.GetString("reagent-effect-remove-tolerance", ("reagent", Reagent));
    }

    public override void Effect(EntityEffectBaseArgs args)
    {
        args.EntityManager.System<PhysiologyToleranceSystem>().RemoveTolerance(args.TargetEntity, Reagent, Amount, Target);
    }
}
