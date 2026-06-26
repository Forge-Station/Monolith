using JetBrains.Annotations;
using Robust.Shared.Random;
using Content.Shared.EntityEffects;
using Content.Shared._Forge.Body.Syndromes;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs;

namespace Content.Server._Forge.Body.Syndromes.Symptoms;

[UsedImplicitly]
public sealed partial class EntityEffectList : SyndromeSymptom
{
    [DataField]
    public List<EntityEffect> Effects { get; set; } = new();

    public override void Apply(EntityUid organUid, EntityUid bodyUid, SyndromeData syndrome, IEntityManager entMan)
    {
        if (entMan.TryGetComponent<MobStateComponent>(bodyUid, out var state) && state.CurrentState == MobState.Dead)
            return;
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
