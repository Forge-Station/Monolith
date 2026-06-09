using Robust.Shared.Prototypes;
using Content.Shared.EntityEffects;
using Content.Shared._Forge.Physiology.Disease;
using Content.Server._Forge.Physiology.Disease;

namespace Content.Server._Forge.EntityEffects.Effects;

public sealed partial class RemoveDiseaseEffect : EntityEffect
{
    [DataField(required: true)]
    public string Disease = string.Empty;

    [DataField]
    public float Severity = 2f;

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
    {
        if (!prototype.TryIndex<DiseasePrototype>(Disease, out var disease))
            return null;

        return Loc.GetString("reagent-effect-remove-disease", ("disease", Loc.GetString(disease.Name)));
    }

    public override void Effect(EntityEffectBaseArgs args)
    {
        args.EntityManager.System<PhysiologyDiseaseSystem>().ModifySeverity(args.TargetEntity, Disease, -Severity);
    }
}
