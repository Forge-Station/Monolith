using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;

namespace Content.Client._WF.Wolfmed.Overlays;

/// <summary>
/// The screen pass under the synthetic readout: the red phosphor edge tint, the occasional torn slice and the
/// standby drain. <see cref="WolfmedSyntheticHudSystem"/> owns every value.
/// </summary>
public sealed class WolfmedSyntheticScreenOverlay : Overlay
{
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;

    private static readonly ProtoId<ShaderPrototype> Shader = "WFWolfmedSynthetic";

    public override OverlaySpace Space => OverlaySpace.WorldSpace;
    public override bool RequestScreenTexture => true;

    private readonly ShaderInstance _shader;

    public float Tint;
    public float Glitch;
    public float Slice;
    public float Standby;

    public WolfmedSyntheticScreenOverlay()
    {
        IoCManager.InjectDependencies(this);
        _shader = _prototypes.Index(Shader).InstanceUnique();
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        return (Tint > 0.004f || Glitch > 0.004f || Standby > 0.004f) &&
               _entities.TryGetComponent(_player.LocalEntity, out EyeComponent? eye) &&
               args.Viewport.Eye == eye.Eye;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (ScreenTexture == null)
            return;

        _shader.SetParameter("SCREEN_TEXTURE", ScreenTexture);
        _shader.SetParameter("tint", Tint);
        _shader.SetParameter("glitch", Glitch);
        _shader.SetParameter("slice", Slice);
        _shader.SetParameter("standby", Standby);

        var handle = args.WorldHandle;
        handle.UseShader(_shader);
        handle.DrawRect(args.WorldBounds, Color.White);
        handle.UseShader(null);
    }
}
