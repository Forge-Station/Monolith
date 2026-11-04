using Content.Shared._WF.Warmth;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._WF.Warmth;

/// <summary>Frost creeping in from the screen's edges while the cold warns the local body, a heat shimmer while heat does.</summary>
public sealed class WFWarmthOverlay : Overlay
{
    [Dependency] private IEntityManager _entMan = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IPrototypeManager _proto = default!;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;
    public override bool RequestScreenTexture => true;

    /// <summary>Strength at the warning line; the rest grows with severity.</summary>
    private const float BaseStrength = 0.35f;

    /// <summary>Strength per second the effect moves toward its target, so it fades rather than pops.</summary>
    private const float FadeRate = 0.25f;

    private readonly ShaderInstance _shader;
    private float _frost;
    private float _heat;

    public WFWarmthOverlay()
    {
        IoCManager.InjectDependencies(this);
        _shader = _proto.Index<ShaderPrototype>("WFWarmthScreen").InstanceUnique();
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        var frost = 0f;
        var heat = 0f;
        if (_entMan.TryGetComponent(_player.LocalEntity, out WFWarmthComponent? warmth))
        {
            var strength = BaseStrength + (1f - BaseStrength) * warmth.Severity;
            if (warmth.Strain == WFWarmthStrain.Cold)
                frost = strength;
            else if (warmth.Strain == WFWarmthStrain.Hot)
                heat = strength;
        }

        var step = FadeRate * args.DeltaSeconds;
        _frost += Math.Clamp(frost - _frost, -step, step);
        _heat += Math.Clamp(heat - _heat, -step, step);
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        if (!_entMan.TryGetComponent(_player.LocalEntity, out EyeComponent? eye) || args.Viewport.Eye != eye.Eye)
            return false;

        return _frost > 0.001f || _heat > 0.001f;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (ScreenTexture == null)
            return;

        var handle = args.WorldHandle;
        _shader.SetParameter("SCREEN_TEXTURE", ScreenTexture);
        _shader.SetParameter("frost", _frost);
        _shader.SetParameter("heat", _heat);
        handle.UseShader(_shader);
        handle.DrawRect(args.WorldBounds, Color.White);
        handle.UseShader(null);
    }
}
