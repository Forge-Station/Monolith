using Content.Shared._WF.Xenonids.Fruit.Components;
using Robust.Client.GameObjects;

namespace Content.Client._WF.Xenonids.Fruit;

// System for updating the sprite of a fruit planter based on their selected fruit
public sealed class XenoFruitPlanterVisualsSystem : VisualizerSystem<XenoFruitPlanterVisualsComponent>
{
    protected override void OnAppearanceChange(EntityUid uid, XenoFruitPlanterVisualsComponent component, ref AppearanceChangeEvent args)
    {
        var sprite = args.Sprite;

        if (sprite == null ||
            !AppearanceSystem.TryGetData(uid, XenoFruitPlanterVisuals.Color, out Color color) ||
            !SpriteSystem.LayerMapTryGet((uid, sprite), XenoFruitVisualLayers.Base, out var layer, false))
        {
            return;
        }

        if (color == null)
        {
            SpriteSystem.LayerSetVisible((uid, sprite), layer, false);
            return;
        }

        SpriteSystem.LayerSetVisible((uid, sprite), layer, true);
        SpriteSystem.LayerSetColor((uid, sprite), layer, color);

        if (AppearanceSystem.TryGetData(uid, XenoFruitPlanterVisuals.Downed, out bool downed) && downed)
        {
            SpriteSystem.LayerSetRsiState((uid, sprite), layer, $"{component.Prefix}_downed");
            return;
        }

        if (AppearanceSystem.TryGetData(uid, XenoFruitPlanterVisuals.Resting, out bool resting) && resting)
        {
            SpriteSystem.LayerSetRsiState((uid, sprite), layer, $"{component.Prefix}_rest");
            return;
        }

        SpriteSystem.LayerSetRsiState((uid, sprite), layer, $"{component.Prefix}_walk");
    }
}
