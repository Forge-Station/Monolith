using System.Numerics;
using Content.Shared._WF.WolfChem.Narcotics;
using Content.Shared.CCVar;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Timing;

namespace Content.Client._WF.WolfChem.Narcotics;

/// <summary>
/// The exotic matter sense: a soft violet light on the local player's screen, over the rock it points to or at the
/// edge of the view towards it.
/// </summary>
public sealed class WFExoticSenseOverlay : Overlay
{
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IEyeManager _eye = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IGameTiming _timing = default!;

    private static readonly Color Light = Color.FromHex("#b48cff");

    /// <summary>Pixels the light keeps from the edge of the view.</summary>
    private const float Margin = 56f;

    private const float Radius = 90f;
    private const int Bands = 14;
    private const float FadeSeconds = 1.5f;

    private float _shown;
    private float _richness;
    private Vector2 _target;

    public override OverlaySpace Space => OverlaySpace.ScreenSpace;

    public WFExoticSenseOverlay()
    {
        IoCManager.InjectDependencies(this);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        var lit = _entities.TryGetComponent(_player.LocalEntity, out WFExoticSenseComponent? sense) && sense.HasTarget;
        if (lit)
        {
            _target = sense!.Target;
            _richness = sense.Richness;
        }

        var step = args.DeltaSeconds / FadeSeconds;
        _shown = Math.Clamp(_shown + (lit ? step : -step), 0f, 1f);
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        return _shown > 0f
            && _player.LocalEntity is { } local
            && _entities.TryGetComponent(local, out TransformComponent? xform)
            && xform.MapID == args.MapId
            && _entities.TryGetComponent(local, out EyeComponent? eye)
            && args.Viewport.Eye == eye.Eye;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var bounds = args.ViewportBounds;
        var centre = new Vector2(bounds.Left + bounds.Width / 2f, bounds.Top + bounds.Height / 2f);
        var half = new Vector2(bounds.Width / 2f - Margin, bounds.Height / 2f - Margin);
        if (half.X <= 0f || half.Y <= 0f)
            return;

        // Over the rock while it is in view, otherwise held at the edge towards it.
        var offset = _eye.WorldToScreen(_target) - centre;
        var scale = MathF.Max(MathF.Abs(offset.X) / half.X, MathF.Abs(offset.Y) / half.Y);
        if (scale > 1f)
            offset /= scale;

        var pulse = _config.GetCVar(CCVars.ReducedMotion)
            ? 0.85f
            : 0.75f + 0.25f * MathF.Sin((float) _timing.RealTime.TotalSeconds * 1.6f);
        var strength = _shown * pulse * (0.5f + 0.5f * _richness);
        var radius = Radius * (0.7f + 0.3f * _richness);

        // Stacked discs, smallest last, fall off like a glow without a shader.
        var handle = args.ScreenHandle;
        for (var i = 0; i < Bands; i++)
        {
            var share = 1f - i / (float) Bands;
            handle.DrawCircle(centre + offset, radius * share, Light.WithAlpha(0.05f * strength));
        }
    }
}
