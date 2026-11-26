using Content.Shared._WF.Xenonids;
using Content.Shared._WF.Xenonids.Damage;
using Robust.Client.GameObjects;

namespace Content.Client._WF.Xenonids.Damage;

public sealed class RMCXenoDamageVisualsSystem : VisualizerSystem<RMCXenoDamageVisualsComponent>
{
    protected override void OnAppearanceChange(EntityUid uid, RMCXenoDamageVisualsComponent component, ref AppearanceChangeEvent args)
    {
        var sprite = args.Sprite;
        if (sprite == null ||
            !AppearanceSystem.TryGetData(uid, RMCDamageVisuals.State, out int level) ||
            !SpriteSystem.LayerMapTryGet((uid, sprite), RMCDamageVisualLayers.Base, out var layer, false))
        {
            return;
        }

        if (level == 0)
        {
            SpriteSystem.LayerSetVisible((uid, sprite), layer, false);
            return;
        }

        SpriteSystem.LayerSetVisible((uid, sprite), layer, true);

        var state = component.States - level + 1;
        if (AppearanceSystem.TryGetData(uid, RMCXenoStateVisuals.Downed, out bool downed) && downed)
        {
            SpriteSystem.LayerSetRsiState((uid, sprite), layer, $"{component.Prefix}_downed_{state}");
            return;
        }

        if (AppearanceSystem.TryGetData(uid, RMCXenoStateVisuals.Fortified, out bool fortified) && fortified)
        {
            SpriteSystem.LayerSetRsiState((uid, sprite), layer, $"{component.Prefix}_fortify_{state}");
            return;
        }

        if (AppearanceSystem.TryGetData(uid, RMCXenoStateVisuals.Resting, out bool resting) && resting)
        {
            SpriteSystem.LayerSetRsiState((uid, sprite), layer, $"{component.Prefix}_rest_{state}");
            return;
        }

        SpriteSystem.LayerSetRsiState((uid, sprite), layer, $"{component.Prefix}_walk_{state}");
    }
}
