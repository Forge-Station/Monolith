using JetBrains.Annotations;
using Robust.Shared.Prototypes;
using Content.Shared.EntityEffects;
using Content.Server._Forge.Physiology.Disease;
using Content.Shared._Forge.Physiology.Disease;

namespace Content.Server._Forge.EntityEffects.Effects;

[UsedImplicitly]
public sealed partial class SuppressEffect : EntityEffect
{
    [DataField(required: true)]
    public string Disease = string.Empty;

    [DataField]
    public float Duration = 60f;

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
    {
        if (!prototype.TryIndex<DiseasePrototype>(Disease, out var disease))
            return null;

        return Loc.GetString("reagent-effect-guidebook-suppress-withdrawal", ("disease", Loc.GetString(disease.Name)));
    }

    public override void Effect(EntityEffectBaseArgs args)
    {
        var diseaseSystem = args.EntityManager.System<PhysiologyDiseaseSystem>();
        diseaseSystem.SuppressFlare(args.TargetEntity, Disease, Duration);
    }
}
