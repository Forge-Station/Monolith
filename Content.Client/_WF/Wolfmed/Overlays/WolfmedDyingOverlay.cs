using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;

namespace Content.Client._WF.Wolfmed.Overlays;

/// <summary>Fullscreen pass for <see cref="WolfmedDyingEffectsSystem"/>; the system owns every value it draws with.</summary>
public sealed class WolfmedDyingOverlay : Overlay
{
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;

    private static readonly ProtoId<ShaderPrototype> Shader = "WFWolfmedDying";

    public override OverlaySpace Space => OverlaySpace.WorldSpace;
    public override bool RequestScreenTexture => true;

    private readonly ShaderInstance _shader;

    public float Level;
    public float Beat;
    public float Blackout;
    public float Dead;

    public WolfmedDyingOverlay()
    {
        IoCManager.InjectDependencies(this);
        _shader = _prototypes.Index(Shader).InstanceUnique();
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        return (Level > 0f || Dead > 0f) &&
               _entities.TryGetComponent(_player.LocalEntity, out EyeComponent? eye) &&
               args.Viewport.Eye == eye.Eye;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (ScreenTexture == null)
            return;

        _shader.SetParameter("SCREEN_TEXTURE", ScreenTexture);
        _shader.SetParameter("level", Level);
        _shader.SetParameter("beat", Beat);
        _shader.SetParameter("blackout", Blackout);
        _shader.SetParameter("dead", Dead);

        var handle = args.WorldHandle;
        handle.UseShader(_shader);
        handle.DrawRect(args.WorldBounds, Color.White);
        handle.UseShader(null);
    }
}
