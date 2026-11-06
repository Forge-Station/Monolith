using System.Numerics;
using Content.Client.Parallax;
using Content.Shared._WF.Lattice;
using Content.Shared.CCVar;
using Robust.Client.Graphics;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._WF.Lattice;

/// <summary>
/// Regrades the space background by how far out of the Lattice's hold the view is: the Slack drains it, Adrift turns
/// it sick and swimming, Untethered red and dark, and the Deep nearly black, with veins, a heartbeat and things looking back.
/// </summary>
public sealed class WFLatticeSkyOverlay : Overlay
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IEntityManager _ent = default!;
    [Dependency] private IEyeManager _eye = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;

    private static readonly ProtoId<ShaderPrototype> Shader = "WFLatticeSky";

    /// <summary>How fast the shown sky follows the view's place, per second.</summary>
    private const float Follow = 0.8f;

    private readonly ShaderInstance _shader;
    private readonly List<(Vector2 Position, float Strength)> _anchors = new();
    private float _shown;
    private TimeSpan _last;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowWorld;

    public override bool RequestScreenTexture => true;

    public WFLatticeSkyOverlay()
    {
        IoCManager.InjectDependencies(this);
        ZIndex = ParallaxSystem.ParallaxZIndex + 2;
        _shader = _proto.Index(Shader).InstanceUnique();
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        if (!_cfg.GetCVar(CCVars.ParallaxEnabled))
            return false;

        Advance();
        return _shown > 0.01f && _ent.HasComponent<WFLatticeFieldComponent>(args.MapUid);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (ScreenTexture == null)
            return;

        var eye = args.Viewport.Eye?.Position.Position ?? args.WorldAABB.Center;
        _shader.SetParameter("SCREEN_TEXTURE", ScreenTexture);
        _shader.SetParameter("viewportMin", args.WorldAABB.BottomLeft);
        _shader.SetParameter("viewportSize", args.WorldAABB.Size);
        _shader.SetParameter("eyePos", eye);
        _shader.SetParameter("reach", _shown);

        args.WorldHandle.UseShader(_shader);
        args.WorldHandle.DrawRect(args.WorldAABB, Color.White);
        args.WorldHandle.UseShader(null);
    }

    /// <summary>Eases the shown sky towards the main eye's place; off the sector map it holds where it was.</summary>
    private void Advance()
    {
        var now = _timing.RealTime;
        var dt = (float) Math.Clamp((now - _last).TotalSeconds, 0, 1);
        if (dt <= 0f)
            return;

        _last = now;
        if (Reach() is not { } target)
            return;

        _shown += (target - _shown) * (1f - MathF.Exp(-dt * Follow));
    }

    /// <summary>
    /// How far out the main eye is, in bands: 0 inside the hold, 1 at the Slack's edge, 4 at the Deep's, towards 5 far
    /// out in it. Null off a map with a Lattice.
    /// </summary>
    private float? Reach()
    {
        var position = _eye.CurrentEye.Position;
        if (!_ent.System<SharedMapSystem>().TryGetMap(position.MapId, out var map)
            || !_ent.TryGetComponent<WFLatticeFieldComponent>(map, out var field)
            || !_proto.TryIndex(field.Profile, out var profile))
            return null;

        _anchors.Clear();
        for (var i = 0; i < field.Anchors.Count && i < field.Strengths.Count; i++)
        {
            _anchors.Add((field.Anchors[i], field.Strengths[i]));
        }

        var thresholds = field.Thresholds;
        var strength = WFLatticeMath.Field(position.Position, _anchors, profile.Reach);
        var band = WFLatticeMath.Band(strength, thresholds);
        if (band == 0)
            return 0f;

        float through;
        if (band < thresholds.Count)
        {
            var inner = thresholds[band - 1];
            var outer = thresholds[band];
            through = inner - outer <= 0f ? 0f : Math.Clamp((inner - strength) / (inner - outer), 0f, 1f);
        }
        else
        {
            through = WFLatticeMath.Depth(strength, band, thresholds);
        }

        return band + through;
    }
}
