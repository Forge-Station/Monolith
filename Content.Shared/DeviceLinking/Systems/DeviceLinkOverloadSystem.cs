using Content.Shared.DeviceLinking.Components;
using Content.Shared.DeviceLinking.Events;
using Content.Shared.EntityEffects;

namespace Content.Shared.DeviceLinking.Systems;

public sealed partial class EntityEffectOnDeviceOverloadSystem : EntitySystem
{
    [SubscribeLocalEvent]
    private void OnEffectsOverload(Entity<EntityEffectOnDeviceOverloadComponent> ent, ref DeviceLinkOverloadedEvent args)
    {
        var effectArgs = new EntityEffectBaseArgs(ent, EntityManager);
        foreach (var effect in ent.Comp.Effects)
        {
            if (!effect.ShouldApply(effectArgs))
                continue;

            effect.Effect(effectArgs);
        }
    }
}
