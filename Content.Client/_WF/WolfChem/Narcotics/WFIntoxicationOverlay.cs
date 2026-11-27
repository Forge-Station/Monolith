using System.Numerics;
using Content.Shared._WF.WolfChem.Narcotics;
using Content.Shared.CCVar;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._WF.WolfChem.Narcotics;

/// <summary>
/// Draws the local player's strongest high and any crash with one shader. Each phase has its own look: light is a
/// steady grade with a slight bend, heavy adds the family's motion, warp and ghost trails, and the overdose pushes it
/// to an extreme; strength builds with the level inside each phase, and stepping up a phase kicks the screen. Static
/// and trail-free under reduced motion. A neuroware host sees its programs through a digital shader of its own.
/// </summary>
public sealed class WFIntoxicationOverlay : Overlay
{
    [Dependency] private IClyde _clyde = default!;
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IPrototypeManager _proto = default!;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;
    public override bool RequestScreenTexture => true;

    /// <summary>How fast the drawn intensity follows the target, per second.</summary>
    private const float Follow = 1.5f;

    /// <summary>How fast the heavy and overdose looks come in or go, per second.</summary>
    private const float PhaseFollow = 2.5f;

    /// <summary>How fast a phase kick fades, per second.</summary>
    private const float KickFade = 1.2f;

    /// <summary>
    /// Share of the last trail frame kept each frame at 60 fps, in the heavy phase and in an overdose. Families left
    /// out leave no trails.
    /// </summary>
    private static readonly Dictionary<WFDrugFamily, (float Heavy, float Overdose)> Trails = new()
    {
        [WFDrugFamily.Psychedelic] = (0.85f, 0.93f),
        [WFDrugFamily.Opioid] = (0.7f, 0.88f),
        [WFDrugFamily.Cannabinoid] = (0.6f, 0.85f),
        [WFDrugFamily.Empathogen] = (0.55f, 0.8f),
        [WFDrugFamily.Stimulant] = (0f, 0.65f),
    };

    /// <summary>How far old trail frames drift outwards each frame.</summary>
    private const float TrailSpread = 0.0015f;
    private const float PsychedelicSpread = 0.004f;

    private readonly ShaderInstance _shader;
    private readonly ShaderInstance _neurowareShader;
    private readonly ShaderInstance _trailShader;
    private readonly WFIntoxicationSystem _system;

    private WFDrugFamily _family;
    private bool _synthetic;
    private WFDrugPhase _phase;
    private float _intensity;
    private float _heavy;
    private float _overdose;
    private float _kick;
    private float _crash;
    private float _pulseRate = 1.2f;
    private float _trail;
    private float _frameSeconds;

    private IRenderTexture? _trailA;
    private IRenderTexture? _trailB;
    private bool _trailFlip;
    private bool _trailValid;

    public WFIntoxicationOverlay(WFIntoxicationSystem system)
    {
        IoCManager.InjectDependencies(this);
        _system = system;
        _shader = _proto.Index<ShaderPrototype>("WFIntoxication").InstanceUnique();
        _neurowareShader = _proto.Index<ShaderPrototype>("WFNeuroware").InstanceUnique();
        _trailShader = _proto.Index<ShaderPrototype>("WFIntoxicationTrail").InstanceUnique();
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        var targetIntensity = 0f;
        var targetCrash = 0f;
        var phase = WFDrugPhase.None;

        if (_player.LocalEntity is { } local
            && _entities.TryGetComponent(local, out WFIntoxicationComponent? comp))
        {
            _synthetic = _system.IsNeurowareHost(local);
            if (_system.DominantHigh(comp) is { } high)
            {
                phase = high.Phase;
                targetIntensity = high.Strength;
                _family = high.Family;
                _pulseRate = 1.2f + 0.6f * high.Tier;
            }

            foreach (var profile in _system.ActiveCrashes(comp))
            {
                if (_proto.TryIndex(profile, out var proto) && proto.Crash != null)
                    targetCrash = Math.Max(targetCrash, proto.Crash.Overlay);
            }
        }

        if (phase > _phase && phase != WFDrugPhase.None)
            _kick = 1f;

        _phase = phase;
        _frameSeconds = args.DeltaSeconds;

        var step = Math.Min(1f, args.DeltaSeconds * Follow);
        _intensity += (targetIntensity - _intensity) * step;
        _crash += (targetCrash - _crash) * step;

        var phaseStep = Math.Min(1f, args.DeltaSeconds * PhaseFollow);
        _heavy += ((phase >= WFDrugPhase.Heavy ? 1f : 0f) - _heavy) * phaseStep;
        _overdose += ((phase == WFDrugPhase.Overdose ? 1f : 0f) - _overdose) * phaseStep;
        _kick = Math.Max(0f, _kick - args.DeltaSeconds * KickFade);

        var targetTrail = 0f;
        if (Trails.TryGetValue(_family, out var trail))
            targetTrail = (trail.Heavy * _heavy + (trail.Overdose - trail.Heavy) * _overdose) * (0.5f + 0.5f * _intensity);
        _trail += (targetTrail - _trail) * phaseStep;
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        if (_intensity < 0.01f && _crash < 0.01f && _kick < 0.01f)
        {
            _trailValid = false;
            return false;
        }

        return _entities.TryGetComponent(_player.LocalEntity, out EyeComponent? eye) && args.Viewport.Eye == eye.Eye;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (ScreenTexture == null)
            return;

        var handle = args.WorldHandle;
        var motion = !_config.GetCVar(CCVars.ReducedMotion);
        Texture screen = ScreenTexture;
        if (motion && _trail > 0.01f)
            screen = Trail(handle, ScreenTexture, MathF.Pow(_trail, _frameSeconds * 60f));
        else
            _trailValid = false;

        var shader = _synthetic ? _neurowareShader : _shader;
        shader.SetParameter("SCREEN_TEXTURE", screen);
        shader.SetParameter("family", (float) _family);
        shader.SetParameter("intensity", _intensity);
        shader.SetParameter("heavy", _heavy);
        shader.SetParameter("overdose", _overdose);
        shader.SetParameter("kick", _kick);
        shader.SetParameter("crash", _crash);
        shader.SetParameter("motion", motion ? 1f : 0f);
        shader.SetParameter("pulseRate", _pulseRate);
        handle.UseShader(shader);
        handle.DrawRect(args.WorldBounds, Color.White);
        handle.UseShader(null);
    }

    /// <summary>
    /// Blends the screen into the trail frame, ping-ponging between two targets, and returns the blended frame. The
    /// trail holds the world before the effect, so the effect never feeds back into itself.
    /// </summary>
    private Texture Trail(DrawingHandleWorld handle, Texture screen, float persistence)
    {
        var size = screen.Size;
        if (_trailA == null || _trailB == null || _trailA.Size != size)
        {
            _trailA?.Dispose();
            _trailB?.Dispose();
            var format = new RenderTargetFormatParameters(RenderTargetColorFormat.Rgba8Srgb);
            _trailA = _clyde.CreateRenderTarget(size, format, name: "wf-intoxication-trail-a");
            _trailB = _clyde.CreateRenderTarget(size, format, name: "wf-intoxication-trail-b");
            _trailValid = false;
        }

        var (from, to) = _trailFlip ? (_trailB, _trailA) : (_trailA, _trailB);
        _trailFlip = !_trailFlip;

        _trailShader.SetParameter("SCREEN_TEXTURE", screen);
        _trailShader.SetParameter("HISTORY", from.Texture);
        _trailShader.SetParameter("persistence", _trailValid ? persistence : 0f);
        _trailShader.SetParameter("spread", _family == WFDrugFamily.Psychedelic ? PsychedelicSpread : TrailSpread);
        handle.RenderInRenderTarget(to, () =>
        {
            handle.UseShader(_trailShader);
            handle.SetTransform(Matrix3x2.Identity);
            handle.DrawRect(new Box2(0, 0, size.X, size.Y), Color.White);
            handle.UseShader(null);
        }, null);

        _trailValid = true;
        return to.Texture;
    }

    protected override void DisposeBehavior()
    {
        _trailA?.Dispose();
        _trailB?.Dispose();
        base.DisposeBehavior();
    }
}
