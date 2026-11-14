using Content.Shared._WF.Xenonids;
using Content.Shared._WF.Xenonids.Projectile.Parasite;
using Robust.Client.GameObjects;

namespace Content.Client._WF.Xenonids.Parasite;

public sealed class XenoParasitesVisualSystem : VisualizerSystem<XenoParasiteThrowerComponent>
{
    protected override void OnAppearanceChange(EntityUid uid, XenoParasiteThrowerComponent component, ref AppearanceChangeEvent args)
    {
        var sprite = args.Sprite;

        if (sprite == null || !AppearanceSystem.TryGetData(uid, ParasiteOverlayVisuals.States, out bool[] states))
            return;

        string layerState = "para_";

        if(AppearanceSystem.TryGetData(uid, RMCXenoStateVisuals.Downed, out bool downed) && downed)
            layerState = "para_downed_";
        else if(AppearanceSystem.TryGetData(uid, RMCXenoStateVisuals.Resting, out bool resting) && resting)
            layerState = "para_rest_";

        foreach(var layer in Enum.GetValues<ParasiteOverlayLayers>())
        {
            if (!SpriteSystem.LayerMapTryGet((uid, sprite), layer, out var paraLayer, false))
                continue;

            SpriteSystem.LayerSetVisible((uid, sprite), layer, states[(int)layer]);

            SpriteSystem.LayerSetRsiState((uid, sprite), layer, $"{layerState}{(int)layer}");
        }
    }
}
