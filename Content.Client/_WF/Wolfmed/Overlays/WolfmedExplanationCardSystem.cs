using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.Consciousness;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Configuration;

namespace Content.Client._WF.Wolfmed.Overlays;

/// <summary>
/// M2 (plan §5.2): shows the explanation card while the local player is unconscious (faint, unconscious or Dying) and a
/// faint's white-out while the cause is a faint. A machine's own readout already says all of it, so a body whose
/// synthetic HUD owns the view gets neither.
/// </summary>
public sealed class WolfmedExplanationCardSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private WolfmedSyntheticHudOverlaySystem _synthetic = default!;

    private const float FaintWhite = 0.7f;

    private WolfmedExplanationCardOverlay _card = default!;
    private WolfmedFaintOverlay _faint = default!;
    private bool _enabled;
    private float _time;

    public override void Initialize()
    {
        base.Initialize();
        _card = new WolfmedExplanationCardOverlay();
        _faint = new WolfmedFaintOverlay();
        Subs.CVar(_cfg, WolfmedCVars.DyingEffects, value => _enabled = value, true);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlays.RemoveOverlay(_card);
        _overlays.RemoveOverlay(_faint);
    }

    /// <summary>True while the local player should see the card: unconscious, alive, flesh presentation.</summary>
    public bool ShowsCard(EntityUid player) =>
        TryComp(player, out WolfmedConsciousnessComponent? consciousness) &&
        consciousness.State == WolfmedConsciousness.Unconscious &&
        !(TryComp(player, out MobStateComponent? mob) && mob.CurrentState == MobState.Dead) &&
        !_synthetic.OwnsView(player);

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);
        _time += frameTime;

        var local = _player.LocalEntity;
        var card = local is { } player && ShowsCard(player);
        var faint = card && _enabled && TryComp(local, out WolfmedConsciousnessComponent? consciousness) &&
                    WolfmedCauses.IsFaint(consciousness.Cause);

        _card.Alpha = Ease(_card.Alpha, card ? 1f : 0f, frameTime * 2f);
        _faint.Alpha = Ease(_faint.Alpha, faint ? FaintWhite * (0.9f + 0.1f * MathF.Sin(_time * 1.3f)) : 0f,
            frameTime * 3f);

        Set(_card, _card.Alpha > 0.001f);
        Set(_faint, _faint.Alpha > 0.001f);
    }

    private static float Ease(float from, float to, float step)
    {
        var next = from + (to - from) * Math.Min(1f, step);
        return MathF.Abs(next - to) < 0.002f ? to : next;
    }

    private void Set(Overlay overlay, bool on)
    {
        if (on == _overlays.HasOverlay(overlay.GetType()))
            return;

        if (on)
            _overlays.AddOverlay(overlay);
        else
            _overlays.RemoveOverlay(overlay);
    }
}
