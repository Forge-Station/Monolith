using Content.Client.DamageState;
using Content.Shared.DrawDepth;
using Content.Shared._WF.Xenobiology.Components;
using Content.Shared._WF.Xenobiology.Systems;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Prototypes;

namespace Content.Client._WF.Xenobiology;

/// <summary>
/// Swaps a growing mob's base sprite to its stage's RSI.
/// </summary>
public sealed partial class MobGrowthVisualizerSystem : VisualizerSystem<MobGrowthComponent>
{
    protected override void OnAppearanceChange(EntityUid uid, MobGrowthComponent component, ref AppearanceChangeEvent args)
    {
        if (args.Sprite == null
            || !AppearanceSystem.TryGetData<string>(uid, GrowthStateVisuals.Sprite, out var rsi, args.Component))
            return;

        SpriteSystem.LayerSetRsi((uid, args.Sprite), DamageStateVisualLayers.Base, new Robust.Shared.Utility.ResPath(rsi));
    }
}

/// <summary>
/// Tints a slime to its breed's colour and applies its breed shader.
/// </summary>
public sealed partial class XenoSlimeVisualizerSystem : VisualizerSystem<SlimeComponent>
{
    [Dependency] private IPrototypeManager _proto = default!;

    protected override void OnAppearanceChange(EntityUid uid, SlimeComponent component, ref AppearanceChangeEvent args)
    {
        if (args.Sprite == null
            || !AppearanceSystem.TryGetData<Color>(uid, XenoSlimeVisuals.Color, out var color, args.Component))
            return;

        foreach (var layer in args.Sprite.AllLayers)
        {
            layer.Color = color.WithAlpha(layer.Color.A);
        }

        if (!AppearanceSystem.TryGetData<string>(uid, XenoSlimeVisuals.Shader, out var shader, args.Component)
            || !_proto.TryIndex<ShaderPrototype>(shader, out var shaderProto)
            || !SpriteSystem.LayerMapTryGet((uid, args.Sprite), DamageStateVisualLayers.Base, out var index, false))
            return;

        args.Sprite.LayerSetShader(index, shaderProto.InstanceUnique());
        args.Sprite.GetScreenTexture = true;
        args.Sprite.RaiseShaderEvent = true;
    }
}

public sealed partial class XenoVacuumSystem : SharedXenoVacuumSystem;

/// <summary>
/// Draws a latched slime over the mob it is feeding on.
/// </summary>
public sealed partial class SlimeLatchVisualsSystem : EntitySystem
{
    [Dependency] private SpriteSystem _sprite = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SlimeComponent, AfterAutoHandleStateEvent>(OnState);
    }

    private void OnState(Entity<SlimeComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        if (!TryComp<SpriteComponent>(ent, out var sprite))
            return;

        var depth = ent.Comp.LatchedTarget != null ? Content.Shared.DrawDepth.DrawDepth.OverMobs : Content.Shared.DrawDepth.DrawDepth.Mobs;
        _sprite.SetDrawDepth((ent, sprite), (int) depth);
    }
}
