using Content.Shared._Forge.KIAS;

namespace Content.Server._Forge.KIAS;

public sealed class KiasTransformSystem : EntitySystem
{
    public override void Initialize()
    {
        SubscribeLocalEvent<KiasDeviceComponent, MetaFlagRemoveAttemptEvent>(KeepEvents);
        SubscribeLocalEvent<KiasDataCableComponent, MetaFlagRemoveAttemptEvent>(KeepEvents);
        SubscribeLocalEvent<KiasTrackedEntityComponent, MetaFlagRemoveAttemptEvent>(KeepEvents);
        SubscribeLocalEvent<KiasTransponderComponent, MetaFlagRemoveAttemptEvent>(KeepEvents);
        SubscribeLocalEvent<KiasLightGroupComponent, MetaFlagRemoveAttemptEvent>(KeepEvents);
        SubscribeLocalEvent<KiasPowerCableComponent, MetaFlagRemoveAttemptEvent>(KeepEvents);
    }

    private void KeepEvents<T>(Entity<T> ent, ref MetaFlagRemoveAttemptEvent args) where T : Component
    {
        if (ent.Comp.LifeStage <= ComponentLifeStage.Running)
            args.ToRemove &= ~MetaDataFlags.ExtraTransformEvents;
    }
}
