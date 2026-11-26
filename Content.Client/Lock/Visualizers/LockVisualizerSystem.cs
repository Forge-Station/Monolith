using Content.Shared.Storage;
using Content.Shared.Lock;
using Robust.Client.GameObjects;

namespace Content.Client.Lock.Visualizers;

public sealed partial class LockVisualizerSystem : VisualizerSystem<LockVisualsComponent>
{
    [Dependency] private SpriteSystem _sprite = default!;

    protected override void OnAppearanceChange(EntityUid uid, LockVisualsComponent comp, ref AppearanceChangeEvent args)
    {
        if (args.Sprite == null
            || !SpriteSystem.LayerMapTryGet((uid, args.Sprite), LockVisualLayers.Lock, out _, false)
            || !AppearanceSystem.TryGetData<bool>(uid, LockVisuals.Locked, out var locked, args.Component))
            return;

        var unlockedStateExist = args.Sprite.BaseRSI != null
            && args.Sprite.BaseRSI.TryGetState(comp.StateUnlocked, out _);

        var hasOpen = AppearanceSystem.TryGetData<bool>(uid, StorageVisuals.Open, out var open, args.Component);

        if (hasOpen)
        {
            _sprite.LayerSetVisible((uid, args.Sprite), LockVisualLayers.Lock, !open);
        }
        else if (!(bool)unlockedStateExist!)
            _sprite.LayerSetVisible((uid, args.Sprite), LockVisualLayers.Lock, locked);

        // When Open is missing, treat as closed (open defaults false) — same as upstream.
        if (!open && (bool)unlockedStateExist!)
        {
            _sprite.LayerSetRsiState((uid, args.Sprite), LockVisualLayers.Lock, locked ? comp.StateLocked : comp.StateUnlocked);
        }
    }
}

public enum LockVisualLayers : byte
{
    Lock
}
