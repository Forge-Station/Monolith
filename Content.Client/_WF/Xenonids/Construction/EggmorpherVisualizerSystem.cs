using Content.Shared._WF.Xenonids.Construction.EggMorpher;
using Robust.Client.GameObjects;

namespace Content.Client._WF.Xenonids.Construction;

public sealed class EggmorpherVisualizerSystem : VisualizerSystem<EggMorpherComponent>
{
    protected override void OnAppearanceChange(EntityUid uid, EggMorpherComponent component, ref AppearanceChangeEvent args)
    {
        var sprite = args.Sprite;

        if (sprite == null || !AppearanceSystem.TryGetData(uid, EggmorpherOverlayVisuals.Number, out int number) ||
            !SpriteSystem.LayerMapTryGet((uid, sprite), EggmorpherOverlayLayers.Overlay, out var layer, false) ||
            !SpriteSystem.LayerMapTryGet((uid, sprite), EggmorpherOverlayLayers.Base, out var layer2, false))
            return;

        //Same as parasite number calc
        int level = (int)Math.Min(Math.Ceiling(((double)number / component.MaxParasites) * component.OverlayCount), component.OverlayCount);

        if (level == 0)
        {
            SpriteSystem.LayerSetVisible((uid, sprite), layer, false);
            return;
        }

        var wasVisible = true;

        if (!sprite[layer].Visible)
        {
            SpriteSystem.LayerSetVisible((uid, sprite), layer, true);
            wasVisible = false;
        }

        string state = component.OverlayPrefix + "_" + (level - 1);

        if (state != SpriteSystem.LayerGetRsiState((uid, sprite), layer) || !wasVisible)
        {
            SpriteSystem.LayerSetRsiState((uid, sprite), layer, state);
            var stat = SpriteSystem.LayerGetRsiState((uid, sprite), layer2);
            SpriteSystem.LayerSetRsiState((uid, sprite), layer2, state);
            SpriteSystem.LayerSetRsiState((uid, sprite), layer2, stat);
        }
    }
}
