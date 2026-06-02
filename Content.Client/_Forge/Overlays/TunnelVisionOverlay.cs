using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;
using Content.Shared.StatusEffect;
using System.Numerics;

namespace Content.Client.Overlays;

public sealed partial class TunnelVisionOverlay : Overlay
{
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private IEntityManager _entityManager = default!;
    public override bool RequestScreenTexture => true;
    public override OverlaySpace Space => OverlaySpace.WorldSpace;
    private readonly ShaderInstance _shader;

    public TunnelVisionOverlay()
    {
        IoCManager.InjectDependencies(this);
        _shader = _prototypeManager.Index<ShaderPrototype>("GradientCircleMask").InstanceUnique();
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        if (!_entityManager.TryGetComponent(_playerManager.LocalSession?.AttachedEntity, out EyeComponent? eyeComp))
            return false;

        if (args.Viewport.Eye != eyeComp.Eye)
            return false;

        var playerEntity = _playerManager.LocalSession?.AttachedEntity;
        return playerEntity != null && _entityManager.HasComponent<TunnelVisionComponent>(playerEntity);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (ScreenTexture == null)
            return;

        var playerEntity = _playerManager.LocalSession?.AttachedEntity;
        if (!_entityManager.TryGetComponent<TunnelVisionComponent>(playerEntity, out var comp))
            return;

        var worldHandle = args.WorldHandle;
        var viewport = args.WorldBounds;

        _shader.SetParameter("SCREEN_TEXTURE", ScreenTexture);
        _shader.SetParameter("outerCircleRadius", comp.OuterRadius);
        _shader.SetParameter("innerCircleRadius", comp.InnerRadius);
        _shader.SetParameter("darknessAlphaOuter", comp.DarknessAlpha);
        _shader.SetParameter("color", comp.Color);

        worldHandle.UseShader(_shader);
        worldHandle.DrawRect(viewport, Color.White);
        worldHandle.UseShader(null);
    }
}
