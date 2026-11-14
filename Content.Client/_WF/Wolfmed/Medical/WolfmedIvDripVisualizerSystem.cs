using Content.Shared._WF.Wolfmed.Medical;
using Robust.Client.GameObjects;
using Robust.Shared.Maths;

namespace Content.Client._WF.Wolfmed.Medical;

/// <summary>
/// Draws the IV drip the way tg's update_icon does: the stand by mode and whether it flows, the hung container
/// ("beakeridle" or "beakeractive" with a needle in somebody) and the fill overlay tinted by the contents.
/// </summary>
public sealed class WolfmedIvDripVisualizerSystem : VisualizerSystem<WolfmedIvDripComponent>
{
    protected override void OnAppearanceChange(EntityUid uid, WolfmedIvDripComponent component, ref AppearanceChangeEvent args)
    {
        if (args.Sprite == null)
            return;

        var sprite = (uid, args.Sprite);
        AppearanceSystem.TryGetData<WolfmedIvMode>(uid, WolfmedIvDripVisuals.Mode, out var mode, args.Component);
        AppearanceSystem.TryGetData<bool>(uid, WolfmedIvDripVisuals.Attached, out var attached, args.Component);
        AppearanceSystem.TryGetData<bool>(uid, WolfmedIvDripVisuals.Flowing, out var flowing, args.Component);
        AppearanceSystem.TryGetData<bool>(uid, WolfmedIvDripVisuals.Container, out var hung, args.Component);

        var inject = mode == WolfmedIvMode.Inject;
        SpriteSystem.LayerSetRsiState(sprite, WolfmedIvDripLayers.Base, flowing
            ? inject ? "iv_drip_injecting" : "iv_drip_donating"
            : inject ? "iv_drip_injectidle" : "iv_drip_donateidle");

        SpriteSystem.LayerSetVisible(sprite, WolfmedIvDripLayers.Container, hung);
        if (hung)
            SpriteSystem.LayerSetRsiState(sprite, WolfmedIvDripLayers.Container, attached ? "beakeractive" : "beakeridle");

        if (!hung || !AppearanceSystem.TryGetData<int>(uid, WolfmedIvDripVisuals.Fill, out var fill, args.Component))
        {
            SpriteSystem.LayerSetVisible(sprite, WolfmedIvDripLayers.Fill, false);
            return;
        }

        SpriteSystem.LayerSetVisible(sprite, WolfmedIvDripLayers.Fill, true);

        SpriteSystem.LayerSetRsiState(sprite, WolfmedIvDripLayers.Fill, $"reagent{fill}");
        SpriteSystem.LayerSetColor(sprite, WolfmedIvDripLayers.Fill,
            AppearanceSystem.TryGetData<Color>(uid, WolfmedIvDripVisuals.FillColor, out var colour, args.Component)
                ? colour
                : Color.White);
    }
}
