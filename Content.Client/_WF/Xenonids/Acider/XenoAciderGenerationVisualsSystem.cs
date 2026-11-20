using Content.Shared._WF.Xenonids;
using Content.Shared._WF.Xenonids.AciderGeneration;
using Robust.Client.GameObjects;

namespace Content.Client._WF.Xenonids.Acider;

public sealed class XenoAciderGenerationVisualsSystem : VisualizerSystem<XenoAciderGenerationComponent>
{
    protected override void OnAppearanceChange(EntityUid uid, XenoAciderGenerationComponent component, ref AppearanceChangeEvent args)
    {
        var sprite = args.Sprite;

        if (sprite == null || !AppearanceSystem.TryGetData(uid, XenoAcidGeneratingVisuals.Generating, out bool gening))
            return;

        if (!SpriteSystem.LayerMapTryGet((uid, sprite), XenoAcidGeneratingVisualLayers.Base, out var layer, false))
            return;

        if (!gening)
        {
            SpriteSystem.LayerSetVisible((uid, sprite), layer, false);
            return;
        }

        SpriteSystem.LayerSetVisible((uid, sprite), layer, true);

        string layerState = "acid";

        if (AppearanceSystem.TryGetData(uid, RMCXenoStateVisuals.Downed, out bool downed) && downed)
            layerState += "_downed";
        else if (AppearanceSystem.TryGetData(uid, RMCXenoStateVisuals.Resting, out bool resting) && resting)
            layerState += "_rest";

        SpriteSystem.LayerSetRsiState((uid, sprite), layer, layerState);
    }
}
