/// Forge-Chane-Start
using Content.Shared.SprayPainter.Prototypes;
using Content.Shared.Storage;
using Robust.Client.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Client.Storage.Visualizers;

public sealed partial class EntityStorageVisualizerSystem : VisualizerSystem<EntityStorageVisualsComponent>
{
    [Dependency] private IPrototypeManager _prototypeManager = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<EntityStorageVisualsComponent, ComponentInit>(OnComponentInit);
    }

    /// <summary>
    /// Sets the base sprite to this layer. Exists to make the inheritance tree less boilerplate-y.
    /// </summary>
    private void OnComponentInit(EntityUid uid, EntityStorageVisualsComponent comp, ComponentInit args)
    {
        if (comp.StateBaseClosed == null)
            return;

        comp.StateBaseOpen ??= comp.StateBaseClosed;
        if (!TryComp<SpriteComponent>(uid, out var sprite))
            return;

        // Preview / incomplete sprites may not have layers yet (map-load adds Sprite before CopyTo).
        if (!SpriteSystem.LayerMapTryGet((uid, sprite), StorageVisualLayers.Base, out _, false))
            return;

        SpriteSystem.LayerSetRsiState((uid, sprite), StorageVisualLayers.Base, comp.StateBaseClosed);
    }

    protected override void OnAppearanceChange(EntityUid uid,
        EntityStorageVisualsComponent comp,
        ref AppearanceChangeEvent args)
    {
        if (args.Sprite == null
            || !AppearanceSystem.TryGetData<bool>(uid, StorageVisuals.Open, out var open, args.Component))
            return;

        var forceRedrawBase = false;
        /// Forge-Change-Start
        if (AppearanceSystem.TryGetData<string>(uid, PaintableVisuals.Prototype, out var prototype, args.Component))
        {
            if (_prototypeManager.TryIndex(prototype, out EntityPrototype? proto))
            {
                if (proto.TryGetComponent(out SpriteComponent? sprite, Factory) && sprite.BaseRSI != null)
                {
                    SpriteSystem.SetBaseRsi((uid, args.Sprite), sprite.BaseRSI);
                }

                if (proto.TryGetComponent(out EntityStorageVisualsComponent? visuals, Factory))
                {
                    comp.StateBaseOpen = visuals.StateBaseOpen;
                    comp.StateBaseClosed = visuals.StateBaseClosed;
                    comp.StateDoorOpen = visuals.StateDoorOpen;
                    comp.StateDoorClosed = visuals.StateDoorClosed;
                    forceRedrawBase = true;
                }
            }
        }
        /// Forge-Change-End

        var hasBase = SpriteSystem.LayerMapTryGet((uid, args.Sprite), StorageVisualLayers.Base, out _, false);

        // Open/Closed state for the storage entity.
        if (SpriteSystem.LayerMapTryGet((uid, args.Sprite), StorageVisualLayers.Door, out _, false))
        {
            if (open)
            {
                if (comp.OpenDrawDepth != null)
                    SpriteSystem.SetDrawDepth((uid, args.Sprite), comp.OpenDrawDepth.Value);

                if (comp.StateDoorOpen != null)
                {
                    SpriteSystem.LayerSetRsiState((uid, args.Sprite), StorageVisualLayers.Door, comp.StateDoorOpen);
                    SpriteSystem.LayerSetVisible((uid, args.Sprite), StorageVisualLayers.Door, true);
                }
                else
                {
                    SpriteSystem.LayerSetVisible((uid, args.Sprite), StorageVisualLayers.Door, false);
                }

                if (hasBase && comp.StateBaseOpen != null)
                    SpriteSystem.LayerSetRsiState((uid, args.Sprite), StorageVisualLayers.Base, comp.StateBaseOpen);
                else if (hasBase && forceRedrawBase && comp.StateBaseClosed != null)
                    SpriteSystem.LayerSetRsiState((uid, args.Sprite), StorageVisualLayers.Base, comp.StateBaseClosed);
            }
            else
            {
                if (comp.ClosedDrawDepth != null)
                    SpriteSystem.SetDrawDepth((uid, args.Sprite), comp.ClosedDrawDepth.Value);

                if (comp.StateDoorClosed != null)
                {
                    SpriteSystem.LayerSetRsiState((uid, args.Sprite), StorageVisualLayers.Door, comp.StateDoorClosed);
                    SpriteSystem.LayerSetVisible((uid, args.Sprite), StorageVisualLayers.Door, true);
                }
                else
                    SpriteSystem.LayerSetVisible((uid, args.Sprite), StorageVisualLayers.Door, false);

                if (hasBase && comp.StateBaseClosed != null)
                    SpriteSystem.LayerSetRsiState((uid, args.Sprite), StorageVisualLayers.Base, comp.StateBaseClosed);
                else if (hasBase && forceRedrawBase && comp.StateBaseOpen != null)
                    SpriteSystem.LayerSetRsiState((uid, args.Sprite), StorageVisualLayers.Base, comp.StateBaseOpen);
            }
        }
    }
}

public enum StorageVisualLayers : byte
{
    Base,
    Door
}
/// Forge-Chane-End
///
