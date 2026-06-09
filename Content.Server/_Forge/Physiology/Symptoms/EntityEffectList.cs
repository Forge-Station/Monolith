using Content.Shared._Forge.Physiology.Disease;
using Content.Shared.EntityEffects;
using Robust.Shared.Random;
using Robust.Shared.Serialization.Manager.Attributes;
using Content.Shared._Forge.Physiology.Disease.Symptoms;

namespace Content.Server._Forge.Physiology.Disease.Symptoms;

[DataDefinition]
public sealed partial class EntityEffectList : DiseaseSymptom
{
    [DataField]
    public List<EntityEffect> Effects { get; set; } = new();

    public override void Apply(EntityUid organUid, EntityUid bodyUid, DiseaseData disease, IEntityManager entMan)
    {
        if (Effects.Count == 0)
            return;

        var random = IoCManager.Resolve<IRobustRandom>();
        var args = new EntityEffectBaseArgs(bodyUid, entMan);

        foreach (var effect in Effects)
        {
            if (entMan.Deleted(bodyUid))
                return;
            if (effect.ShouldApply(args, random))
                effect.Effect(args);
        }
    }
}
