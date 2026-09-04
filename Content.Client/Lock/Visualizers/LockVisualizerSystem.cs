using Content.Shared.Storage;
using Content.Shared.Lock;
using Robust.Client.GameObjects;

namespace Content.Client.Lock.Visualizers;

public sealed class LockVisualizerSystem : VisualizerSystem<LockVisualsComponent>
{
    protected override void OnAppearanceChange(EntityUid uid, LockVisualsComponent comp, ref AppearanceChangeEvent args)
    {
        if (args.Sprite == null
            || !args.Sprite.LayerMapTryGet(LockVisualLayers.Lock, out _)
            || !AppearanceSystem.TryGetData<bool>(uid, LockVisuals.Locked, out var locked, args.Component))
            return;

        var unlockedStateExist = args.Sprite.BaseRSI != null
            && args.Sprite.BaseRSI.TryGetState(comp.StateUnlocked, out _);

        var hasOpen = AppearanceSystem.TryGetData<bool>(uid, StorageVisuals.Open, out var open, args.Component);

        if (hasOpen)
        {
            args.Sprite.LayerSetVisible(LockVisualLayers.Lock, !open);
        }
        else if (!unlockedStateExist)
            args.Sprite.LayerSetVisible(LockVisualLayers.Lock, locked);

        // When Open is missing, treat as closed (open defaults false) — same as upstream.
        if ((!hasOpen || !open) && unlockedStateExist)
        {
            args.Sprite.LayerSetState(LockVisualLayers.Lock, locked ? comp.StateLocked : comp.StateUnlocked);
        }
    }
}

public enum LockVisualLayers : byte
{
    Lock
}
