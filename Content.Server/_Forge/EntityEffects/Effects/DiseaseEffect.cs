using Robust.Shared.Prototypes;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.EntityEffects;
using Content.Shared._Forge.Physiology.Disease;
using Content.Server._Forge.Physiology.Disease;

namespace Content.Server._Forge.EntityEffects.Effects;

public sealed partial class DiseaseEffect : EntityEffect
{
    [DataField(required: true)]
    public string Disease { get; set; } = string.Empty;

    [DataField]
    public bool RefreshFlare { get; set; } = true;

    [DataField]
    public float Severity { get; set; } = 2.0f;

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
    {
        if (!prototype.TryIndex<DiseasePrototype>(Disease, out var disease))
            return null;

        return Loc.GetString("reagent-effect-add-disease", ("disease", Loc.GetString(disease.Name)));
    }

    public override void Effect(EntityEffectBaseArgs args)
    {
        args.EntityManager.System<PhysiologyDiseaseSystem>().AddDisease(args.TargetEntity, Disease, Severity, refreshFlare: RefreshFlare);
    }
}
