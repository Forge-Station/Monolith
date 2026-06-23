using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Content.Shared._Forge.Overlay;

namespace Content.Client._Forge.Overlay;

public sealed partial class TunnelVisionOverlay : Robust.Client.Graphics.Overlay
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private IPlayerManager _playerManager = default!;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;
    private readonly ShaderInstance _shader;
    private float _displayIntensity;

    public TunnelVisionOverlay()
    {
        IoCManager.InjectDependencies(this);
        _shader = _prototypes.Index<ShaderPrototype>("GradientCircleMask").InstanceUnique();
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var localEntity = _playerManager.LocalEntity;
        if (localEntity == null)
            return;

        if (!_entityManager.TryGetComponent<EyeComponent>(localEntity, out var eyeComp))
            return;

        if (args.Viewport.Eye != eyeComp.Eye)
            return;

        var targetIntensity = 0f;
        var pulseRate = 3f;
        var darknessAlpha = 0.8f;
        var color = Color.Red;

        if (_entityManager.TryGetComponent<TunnelVisionComponent>(localEntity, out var comp))
        {
            targetIntensity = comp.Intensity;
            pulseRate = comp.PulseRate;
            darknessAlpha = comp.DarknessAlpha;
            color = comp.Color;
        }

        var frameTime = (float)_timing.FrameTime.TotalSeconds;
        var diff = targetIntensity - _displayIntensity;
        if (!MathHelper.CloseTo(_displayIntensity, targetIntensity, 0.001f))
            _displayIntensity += Math.Clamp(diff * 5f * frameTime, -MathF.Abs(diff), MathF.Abs(diff));
        else
            _displayIntensity = targetIntensity;

        if (_displayIntensity <= 0f)
            return;

        var viewport = args.WorldAABB;
        var handle = args.WorldHandle;
        var distance = args.ViewportBounds.Width;
        var time = (float)_timing.RealTime.TotalSeconds;

        var level = _displayIntensity;

        var outerRadius = 2.0f * distance - level * (2.0f * distance - 0.8f * distance);
        var innerRadius = 0.6f * distance - level * (0.6f * distance - 0.2f * distance);

        var pulse = MathF.Max(0f, MathF.Sin(time * pulseRate));

        _shader.SetParameter("time", pulse);
        _shader.SetParameter("color", new Vector3(color.R, color.G, color.B));
        _shader.SetParameter("darknessAlphaOuter", darknessAlpha);
        _shader.SetParameter("outerCircleRadius", outerRadius);
        _shader.SetParameter("outerCircleMaxRadius", outerRadius + 0.2f * distance);
        _shader.SetParameter("innerCircleRadius", innerRadius);
        _shader.SetParameter("innerCircleMaxRadius", innerRadius + 0.02f * distance);

        handle.UseShader(_shader);
        handle.DrawRect(viewport, Color.White);
        handle.UseShader(null);
    }
}
