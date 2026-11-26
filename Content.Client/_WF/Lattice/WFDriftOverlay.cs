using Content.Shared._WF.Lattice;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;

namespace Content.Client._WF.Lattice;

/// <summary>A sway of the screen as the local player's Drift climbs past halfway, turning violent in the last tenth.</summary>
public sealed partial class WFDriftOverlay : Overlay
{
    [Dependency] private IEntitySystemManager _systems = default!;
    [Dependency] private IPrototypeManager _proto = default!;

    /// <summary>Drift at which the warping starts.</summary>
    private const float StartDrift = 50f;

    /// <summary>How strong the warping gets by <see cref="BreakDrift"/>, in the drunk shader's terms.</summary>
    private const float SwayPower = 0.15f;

    private const float PowerPerDrift = 1f / 300f;

    /// <summary>Drift past which the warping climbs fast, to <see cref="MaxPower"/> at full Drift.</summary>
    private const float BreakDrift = 90f;

    private const float MaxPower = 0.5f;

    /// <summary>Share of the gap to the target closed each frame, so steps in Drift fade in.</summary>
    private const float Ease = 0.02f;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;
    public override bool RequestScreenTexture => true;

    private readonly ShaderInstance _shader;
    private float _power;

    public WFDriftOverlay()
    {
        IoCManager.InjectDependencies(this);
        _shader = _proto.Index<ShaderPrototype>("Drunk").InstanceUnique();
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        var drift = _systems.GetEntitySystem<WFLatticeHallucinationSystem>().LocalLevel() * WFDriftComponent.LevelStep;
        var target = Math.Clamp((drift - StartDrift) * PowerPerDrift, 0f, SwayPower);
        if (drift > BreakDrift)
            target = MathHelper.Lerp(SwayPower, MaxPower, Math.Clamp((drift - BreakDrift) / (100f - BreakDrift), 0f, 1f));

        _power += (target - _power) * Ease;
        return _power > 0.005f;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (ScreenTexture == null)
            return;

        var handle = args.WorldHandle;
        _shader.SetParameter("SCREEN_TEXTURE", ScreenTexture);
        _shader.SetParameter("boozePower", _power);
        handle.UseShader(_shader);
        handle.DrawRect(args.WorldBounds, Color.White);
        handle.UseShader(null);
    }
}
