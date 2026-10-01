using System.Numerics;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.Consciousness;
using Content.Shared._WF.Wolfmed.Life;
using Content.Shared.Camera;
using Content.Shared.CCVar;
using Content.Shared.Damage;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Systems;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Random;

namespace Content.Client._WF.Wolfmed.Overlays;

/// <summary>Drives the fading-out view of a body close to death: the shader overlay and a slow camera sway.</summary>
public sealed class WolfmedDyingEffectsSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private MobThresholdSystem _thresholds = default!;
    [Dependency] private SharedContentEyeSystem _eye = default!;
    // HUD: a mechanical body has its own presentation and must never get the organic one.
    [Dependency] private WolfmedSyntheticHudOverlaySystem _synthetic = default!;

    /// <summary>Damage, as a share of the crit threshold, where the effects start.</summary>
    public const float StartRatio = 0.5f;

    /// <summary>Level reached at the moment of going critical; the rest is covered by the way to death.</summary>
    public const float CritLevel = 0.55f;

    private const float SwayTiles = 0.14f;
    private const float BlackoutLevel = 0.72f;
    private const float BlackoutLength = 1.8f;

    private const float BannerFadeIn = 1.2f;
    private const float BannerHold = 5f;
    private const float BannerFadeOut = 2f;

    private WolfmedDyingOverlay _overlay = default!;
    private WolfmedDeathBannerOverlay _banner = default!;
    private bool _overlayAdded;
    private bool _bannerAdded;
    private EntityUid? _swaying;

    private bool _enabled;
    private bool _reducedMotion;
    private float _shake;

    private float _level;
    private float _dead;
    private float _deadTime;
    private float _arrestTime = -1f;
    private float _time;
    private float _beatPhase;
    private float _nextBlackout;
    private float _blackoutTime = -1f;

    public override void Initialize()
    {
        base.Initialize();
        _overlay = new WolfmedDyingOverlay();
        _banner = new WolfmedDeathBannerOverlay();
        SubscribeLocalEvent<WolfmedDyingSwayComponent, GetEyeOffsetEvent>(OnGetEyeOffset);

        Subs.CVar(_cfg, WolfmedCVars.DyingEffects, value => _enabled = value, true);
        Subs.CVar(_cfg, CCVars.ReducedMotion, value => _reducedMotion = value, true);
        Subs.CVar(_cfg, CCVars.ScreenShakeIntensity, value => _shake = value, true);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        SetOverlay(false);
        SetBanner(false);
    }

    /// <summary>True while this system, not the stock damage overlay, draws the local player's dead screen.</summary>
    public bool OwnsDeadScreen =>
        _enabled && _player.LocalEntity is { } local && !_synthetic.OwnsView(local) && IsDead(local);

    /// <summary>How far gone a body is, 0 to 1, from its mob state and how deep into each band its damage is.</summary>
    public static float Level(MobState state, float damage, float critThreshold, float deadThreshold)
    {
        switch (state)
        {
            case MobState.Alive when critThreshold > 0f:
                var ratio = damage / critThreshold;
                return Math.Clamp((ratio - StartRatio) / (1f - StartRatio), 0f, 1f) * CritLevel;
            case MobState.Critical:
                var span = deadThreshold - critThreshold;
                var depth = span > 0f ? Math.Clamp((damage - critThreshold) / span, 0f, 1f) : 1f;
                return CritLevel + (1f - CritLevel) * depth;
            default:
                return 0f;
        }
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        var local = _player.LocalEntity;
        var target = _enabled && local is { } player && !_synthetic.OwnsView(player) ? TargetLevel(player) : 0f;

        // Ease toward the target so a big hit or a heal never pops the screen.
        _level += (target - _level) * Math.Min(1f, frameTime * 1.5f);
        if (_level < 0.01f && target <= 0f)
            _level = 0f;

        UpdateDeath(local, frameTime);

        if (_level <= 0f || local == null)
        {
            _overlay.Level = 0f;
            _overlay.Beat = 0f;
            _overlay.Blackout = 0f;
            SetOverlay(_dead > 0f);
            StopSway();
            return;
        }

        _time += frameTime;

        // Pulse races as the body fails, then slows right down deep into crit.
        var bpm = _level < CritLevel
            ? 65f + 60f * (_level / CritLevel)
            : 125f - 80f * ((_level - CritLevel) / (1f - CritLevel));
        _beatPhase = (_beatPhase + frameTime * bpm / 60f) % 1f;
        var beat = MathF.Exp(-_beatPhase * 9f) + 0.6f * MathF.Exp(-((_beatPhase + 0.72f) % 1f) * 9f);

        _overlay.Level = _level;
        _overlay.Beat = Math.Clamp(beat, 0f, 1f);
        _overlay.Blackout = UpdateBlackout(frameTime);
        SetOverlay(true);

        UpdateSway(local.Value, beat);
    }

    /// <summary>Greys the view out once dead, and shows the banner for a few seconds.</summary>
    private void UpdateDeath(EntityUid? local, float frameTime)
    {
        var dead = _enabled && local is { } player && !_synthetic.OwnsView(player) && IsDead(player);

        // BRAIN: a stopped heart gets its own banner, on the same fade, until the body actually dies.
        var arrest = !dead && _enabled && local is { } arrested && !_synthetic.OwnsView(arrested) &&
                     HasComp<WolfmedCardiacArrestComponent>(arrested);
        _arrestTime = arrest ? (_arrestTime < 0f ? 0f : _arrestTime + frameTime) : -1f;
        _dead += ((dead ? 1f : 0f) - _dead) * Math.Min(1f, frameTime * 1.2f);
        if (!dead && _dead < 0.01f)
            _dead = 0f;

        _deadTime = dead ? _deadTime + frameTime : 0f;
        _overlay.Dead = _dead;

        var elapsed = dead ? _deadTime : _arrestTime;
        var alpha = 0f;
        if (elapsed >= 0f && (dead || arrest) && elapsed < BannerFadeIn + BannerHold + BannerFadeOut)
        {
            alpha = elapsed < BannerFadeIn
                ? elapsed / BannerFadeIn
                : 1f - Math.Clamp((elapsed - BannerFadeIn - BannerHold) / BannerFadeOut, 0f, 1f);
        }

        // M4 (OD16): a heartless species' arrest is circulatory collapse.
        var heartless = local is { } body && CompOrNull<WolfmedConsciousnessComponent>(body)?.Heartless == true;
        _banner.TitleKey = dead ? "wolfmed-death-banner" : heartless ? "wolfmed-collapse-banner" : "wolfmed-arrest-banner";
        _banner.SubKey = dead ? "wolfmed-death-banner-sub" : "wolfmed-arrest-banner-sub";
        _banner.Alpha = alpha;
        SetBanner(alpha > 0f);
    }

    private bool IsDead(EntityUid player)
    {
        return TryComp(player, out MobStateComponent? mob) && mob.CurrentState == MobState.Dead &&
               TryComp(player, out MobThresholdsComponent? thresholds) && thresholds.ShowOverlays;
    }

    private float TargetLevel(EntityUid player)
    {
        if (!TryComp(player, out MobStateComponent? mob) ||
            !TryComp(player, out DamageableComponent? damageable) ||
            !TryComp(player, out MobThresholdsComponent? thresholds) ||
            !thresholds.ShowOverlays)
            return 0f;

        // CONSC: a wound host's damage total says nothing about how far gone it is. Consciousness does, so
        // the view reads its depth instead: Downed 0.35 to 0.55, Unconscious 0.55 to 1.
        if (TryComp(player, out WolfmedConsciousnessComponent? consciousness) &&
            mob.CurrentState != MobState.Dead)
        {
            // M2 (plan §5.2): a faint gets its own short white-out (WolfmedExplanationCardSystem), not the dying view.
            if (consciousness.State == WolfmedConsciousness.Unconscious && WolfmedCauses.IsFaint(consciousness.Cause))
                return 0f;

            // BRAIN: in arrest the screen keeps fading toward black as the brain runs out of oxygen.
            if (HasComp<WolfmedCardiacArrestComponent>(player))
                return MathF.Max(consciousness.Depth,
                    CritLevel + (1f - CritLevel) * (1f - Math.Clamp(consciousness.Oxygenation, 0f, 1f)));

            return consciousness.Depth;
        }

        if (!_thresholds.TryGetThresholdForState(player, MobState.Dead, out var dead, thresholds))
            return 0f;

        var crit = _thresholds.TryGetThresholdForState(player, MobState.Critical, out var found, thresholds)
            ? found.Value
            : dead.Value;

        return Level(mob.CurrentState, damageable.TotalDamage.Float(), crit.Float(), dead.Value.Float());
    }

    /// <summary>Eyes drifting shut for a moment, at random, once far enough gone.</summary>
    private float UpdateBlackout(float frameTime)
    {
        if (_level < BlackoutLevel)
        {
            _blackoutTime = -1f;
            _nextBlackout = Math.Max(_nextBlackout, _time + 4f);
            return 0f;
        }

        if (_blackoutTime < 0f)
        {
            if (_time < _nextBlackout)
                return 0f;

            _blackoutTime = 0f;
        }

        _blackoutTime += frameTime;
        if (_blackoutTime >= BlackoutLength)
        {
            _blackoutTime = -1f;
            // More often the closer to the end: every 5 to 11 seconds at worst.
            _nextBlackout = _time + _random.NextFloat(5f, 11f) * (1.6f - _level * 0.6f);
            return 0f;
        }

        var depth = 0.55f + 0.4f * ((_level - BlackoutLevel) / (1f - BlackoutLevel));
        return MathF.Sin(_blackoutTime / BlackoutLength * MathF.PI) * depth;
    }

    private void UpdateSway(EntityUid player, float beat)
    {
        if (_reducedMotion || _shake <= 0f || !TryComp(player, out EyeComponent? eye))
        {
            StopSway();
            return;
        }

        if (_swaying != player)
        {
            StopSway();
            _swaying = player;
        }

        var sway = EnsureComp<WolfmedDyingSwayComponent>(player);
        var amplitude = _level * _level * SwayTiles * _shake;
        sway.Offset = new Vector2(
            MathF.Sin(_time * 0.9f) + 0.5f * MathF.Sin(_time * 2.3f + 1f),
            MathF.Cos(_time * 0.7f) + 0.5f * MathF.Sin(_time * 1.9f)) * amplitude;
        // A small nod on each heartbeat.
        sway.Offset += new Vector2(0f, -beat * 0.02f * _level * _shake);
        _eye.UpdateEyeOffset((player, eye));
    }

    private void StopSway()
    {
        if (_swaying is not { } old)
            return;

        _swaying = null;
        if (TerminatingOrDeleted(old))
            return;

        RemComp<WolfmedDyingSwayComponent>(old);
        if (TryComp(old, out EyeComponent? eye))
            _eye.UpdateEyeOffset((old, eye));
    }

    private void OnGetEyeOffset(Entity<WolfmedDyingSwayComponent> ent, ref GetEyeOffsetEvent args)
    {
        args.Offset += ent.Comp.Offset;
    }

    private void SetBanner(bool on)
    {
        if (on == _bannerAdded)
            return;

        _bannerAdded = on;
        if (on)
            _overlays.AddOverlay(_banner);
        else
            _overlays.RemoveOverlay(_banner);
    }

    private void SetOverlay(bool on)
    {
        if (on == _overlayAdded)
            return;

        _overlayAdded = on;
        if (on)
            _overlays.AddOverlay(_overlay);
        else
            _overlays.RemoveOverlay(_overlay);
    }
}

/// <summary>Client-only: the camera drift the dying effects add to the local player's eye.</summary>
[RegisterComponent]
public sealed partial class WolfmedDyingSwayComponent : Component
{
    [ViewVariables] public Vector2 Offset;
}
