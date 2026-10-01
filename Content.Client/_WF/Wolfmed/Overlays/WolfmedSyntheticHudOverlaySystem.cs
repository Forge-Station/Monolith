using System.Linq;
using Content.Client._WF.Wolfmed.Autodoc;
using Content.Shared.CCVar;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.Consciousness;
using Content.Shared._WF.Wolfmed.Hud;
using Content.Shared._Shitmed.Targeting;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using JetBrains.Annotations;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Robust.Shared.Random;

namespace Content.Client._WF.Wolfmed.Overlays;

/// <summary>
/// Drives the synthetic diagnostics readout a mechanical body sees instead of the organic vignette and
/// dying view. Everything it draws comes off <see cref="WolfmedSyntheticHudComponent"/>; this system only
/// decides how loud the readout is, keeps the fault list animating and makes the noise.
/// </summary>
[UsedImplicitly]
public sealed class WolfmedSyntheticHudOverlaySystem : EntitySystem
{
    /// <summary>The blip a new fault line announces itself with. Quiet, local, and rate limited.</summary>
    private const string FaultSound = "/Audio/Machines/beep.ogg";

    /// <summary>The one-off tone when the readout drops to standby.</summary>
    private const string StandbySound = "/Audio/Machines/buzz-sigh.ogg";

    private const float SlideSeconds = 0.25f;
    private const float PanicSeconds = 2f;
    private const float BannerFadeIn = 1.2f;
    private const float BannerHold = 5f;
    private const float BannerFadeOut = 2f;

    private static readonly string[] SpinnerFrames = { "|", "/", "-", "\\" };
    private const int IdleLines = 6;

    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedAudioSystem _audio = default!;

    private WolfmedSyntheticHudOverlay _hud = default!;
    private WolfmedSyntheticScreenOverlay _screen = default!;
    private bool _hudAdded;
    private bool _screenAdded;

    private bool _enabled = true;
    private float _scale = 1f;
    private bool _reducedMotion;

    private readonly Dictionary<(TargetBodyPart, string), float> _slides = new();
    private float _time;
    private float _spinner;
    private float _nextBlip;
    private float _nextGlitch;
    private float _jitterUntil;
    private float _deadTime;
    private bool _wasStandby;

    /// <summary>True while the local player is the mechanical body this readout belongs to.</summary>
    public bool OwnsView(EntityUid player) => _enabled && HasComp<WolfmedSyntheticHudComponent>(player);

    public override void Initialize()
    {
        base.Initialize();
        _hud = new WolfmedSyntheticHudOverlay();
        _screen = new WolfmedSyntheticScreenOverlay();

        Subs.CVar(_cfg, WolfmedCVars.SyntheticHud, value => _enabled = value, true);
        Subs.CVar(_cfg, WolfmedCVars.SyntheticHudScale, value => _scale = Math.Clamp(value, 0.6f, 2.5f), true);
        Subs.CVar(_cfg, CCVars.ReducedMotion, value => _reducedMotion = value, true);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        SetOverlays(false);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);
        _time += frameTime;

        if (_player.LocalEntity is not { } local || !_enabled ||
            !TryComp(local, out WolfmedSyntheticHudComponent? hud))
        {
            Fade(frameTime, out var idle);
            if (idle)
            {
                SetOverlays(false);
                _slides.Clear();
                _deadTime = 0f;
                _wasStandby = false;
            }

            return;
        }

        var consciousness = CompOrNull<WolfmedConsciousnessComponent>(local);
        var depth = consciousness?.Depth ?? 0f;
        var dead = CompOrNull<MobStateComponent>(local)?.CurrentState == MobState.Dead;
        var standby = !dead && (hud.Shutdown || consciousness?.State == WolfmedConsciousness.Unconscious);
        var downed = !dead && !standby && consciousness?.State == WolfmedConsciousness.Downed;

        var tier = WolfmedSyntheticHudLineSystem.Tier(depth, hud.Integrity);
        var strain = WolfmedSyntheticHudLineSystem.Strain(depth, hud.Integrity);

        _hud.Scale = _scale;
        BuildRows(hud, frameTime);
        BuildSystem(hud, tier);
        UpdateBanner(hud, tier, dead, standby, downed, consciousness);
        UpdateDeath(hud, dead, frameTime);
        UpdateMotion(tier, strain, standby, frameTime);

        // Playtest 3 (IPC): a healthy chassis sees nothing; the idle "::" glyph read as four stray dots.
        var (system, diagnostics) = WolfmedSyntheticHudLayout.Visible(tier, standby, dead, _hud.Rows.Count);
        Approach(ref _hud.SystemAlpha, system ? 1f : 0f, frameTime);
        Approach(ref _hud.DiagnosticsAlpha, diagnostics ? 1f : 0f, frameTime);
        Approach(ref _hud.StandbyAlpha, standby ? 1f : 0f, frameTime);
        Approach(ref _screen.Standby, standby ? 0.85f : 0f, frameTime);

        if (standby != _wasStandby)
        {
            _wasStandby = standby;
            if (standby)
                _audio.PlayGlobal(StandbySound, Filter.Local(), false, AudioParams.Default.WithVolume(-12f));
        }

        SetOverlays(true);
    }

    /// <summary>Turns the wire's fault list into drawable rows, sliding the new ones in and blipping once.</summary>
    private void BuildRows(WolfmedSyntheticHudComponent hud, float frameTime)
    {
        _hud.Rows.Clear();
        var seen = new HashSet<(TargetBodyPart, string)>();
        var fresh = false;

        foreach (var fault in hud.Faults)
        {
            var key = (fault.Part, fault.Line);
            seen.Add(key);
            if (!_slides.TryGetValue(key, out var slide))
            {
                slide = _reducedMotion ? 1f : 0f;
                fresh = true;
            }

            slide = Math.Min(1f, slide + frameTime / SlideSeconds);
            _slides[key] = slide;

            _hud.Rows.Add(new WolfmedSyntheticHudOverlay.Row(
                Loc.GetString(Tag(fault.Severity)),
                Loc.GetString(WolfmedSyntheticHudLineSystem.PartKey(fault.Part)),
                Loc.GetString(fault.Line),
                fault.Severity,
                slide));
        }

        foreach (var key in _slides.Keys.Where(key => !seen.Contains(key)).ToArray())
            _slides.Remove(key);

        if (fresh && _time >= _nextBlip)
        {
            _nextBlip = _time + 0.2f;
            _audio.PlayGlobal(FaultSound, Filter.Local(), false, AudioParams.Default.WithVolume(-14f));
        }
    }

    private void BuildSystem(WolfmedSyntheticHudComponent hud, WolfmedSyntheticTier tier)
    {
        // The processing spinner stutters as the chassis fails: slower, and skipping frames.
        var rate = 3.5f * Math.Clamp(hud.Integrity, 0.15f, 1f);
        _spinner += rate * 0.016f;
        if (tier >= WolfmedSyntheticTier.Moderate && !_reducedMotion && _random.Prob(0.04f))
            _spinner += 1f;

        _hud.Spinner = SpinnerFrames[(int) _spinner % SpinnerFrames.Length];

        // Playtest 3 (IPC): label, fixed-width bar and value apart, so the overlay can line the values up.
        _hud.SystemRows.Clear();
        _hud.SystemRows.Add(GaugeRow("wolfmed-synthetic-row-integrity", hud.Integrity));
        if (hud.Power >= 0f)
            _hud.SystemRows.Add(GaugeRow("wolfmed-synthetic-row-power", hud.Power));

        _hud.SystemRows.Add(GaugeRow("wolfmed-synthetic-row-fluid", hud.Fluid));
        _hud.SystemRows.Add(GaugeRow("wolfmed-synthetic-row-servo", hud.Servos));
        // M1a D (plan §5.6): what the damage sensors are reporting, and how hot the chassis is running.
        _hud.SystemRows.Add(GaugeRow("wolfmed-synthetic-row-sensor", hud.Sensors));
        if (hud.CoreTemperature >= 0f)
        {
            _hud.SystemRows.Add(new WolfmedSyntheticHudOverlay.SystemRow(Loc.GetString("wolfmed-synthetic-row-core"),
                null, Loc.GetString("wolfmed-synthetic-row-core-temp", ("value", (int) MathF.Round(hud.CoreTemperature)))));
        }

        _hud.FaultsLabel = Loc.GetString("wolfmed-synthetic-row-faults");
        _hud.FaultCount = hud.Faults.Count;

        if (hud.Advice.Length > 0)
        {
            var advice = Loc.GetString(hud.Advice);
            _hud.Status = Loc.GetString("wolfmed-synthetic-advice", ("advice", advice));
            _hud.StatusPrefix = Math.Max(0, _hud.Status.Length - advice.Length);
        }
        else
        {
            _hud.Status = Loc.GetString($"wolfmed-synthetic-idle-{(int) (_time / 6f) % IdleLines + 1}");
            _hud.StatusPrefix = 0;
        }
    }

    private WolfmedSyntheticHudOverlay.SystemRow GaugeRow(string label, float fraction) =>
        new(Loc.GetString(label), AutodocStyle.Gauge(fraction, 6), Percent(fraction));

    private void UpdateBanner(
        WolfmedSyntheticHudComponent hud,
        WolfmedSyntheticTier tier,
        bool dead,
        bool standby,
        bool downed,
        WolfmedConsciousnessComponent? consciousness)
    {
        // M1a: the cause's own line replaces the blanket STANDBY (plan §5.6).
        var causeLine = hud.CauseLine.Length > 0
            ? Loc.GetString(hud.CauseLine, ("source", (consciousness?.CauseSource ?? WolfmedCauseSource.None).ToString()))
            : null;
        _hud.StandbyTitle = causeLine ?? Loc.GetString("wolfmed-synthetic-banner-standby");
        _hud.RebootText = Loc.GetString("wolfmed-synthetic-banner-reboot");
        _hud.RebootVisible = (int) (_time * 1.6f) % 2 == 0;

        if (dead || standby)
        {
            _hud.BannerAlpha = Math.Max(0f, _hud.BannerAlpha - 0.08f);
            return;
        }

        if (downed)
        {
            _hud.Banner = causeLine ?? Loc.GetString("wolfmed-synthetic-banner-downed");
            _hud.BannerSeverity = WolfmedSyntheticSeverity.Crit;
        }
        else if (tier == WolfmedSyntheticTier.Heavy)
        {
            _hud.Banner = Loc.GetString("wolfmed-synthetic-banner-integrity", ("value", Percent(hud.Integrity)));
            _hud.BannerSeverity = WolfmedSyntheticSeverity.Crit;
        }
        else
        {
            _hud.BannerAlpha = Math.Max(0f, _hud.BannerAlpha - 0.08f);
            return;
        }

        _hud.BannerAlpha = Math.Min(1f, _hud.BannerAlpha + 0.08f);
    }

    /// <summary>
    /// The end of a chassis: a frozen kernel dump for two seconds, then the core-offline banner on the same
    /// fade the organic death banner uses.
    /// </summary>
    private void UpdateDeath(WolfmedSyntheticHudComponent hud, bool dead, float frameTime)
    {
        if (!dead)
        {
            _deadTime = 0f;
            _hud.PanicAlpha = Math.Max(0f, _hud.PanicAlpha - frameTime * 3f);
            _hud.DeathAlpha = Math.Max(0f, _hud.DeathAlpha - frameTime * 2f);
            return;
        }

        if (_deadTime == 0f)
        {
            _hud.PanicTitle = Loc.GetString("wolfmed-synthetic-banner-panic");
            _hud.Panic.Clear();
            for (var i = 0; i < 6; i++)
            {
                _hud.Panic.Add(Loc.GetString("wolfmed-synthetic-panic-dump",
                    ("address", _random.Next(0x10000000, 0x7fffffff).ToString("x8"))));
            }
        }

        _deadTime += frameTime;
        _hud.DeathTitle = Loc.GetString("wolfmed-synthetic-death-banner");
        _hud.DeathSub = Loc.GetString("wolfmed-synthetic-death-banner-sub");

        _hud.PanicAlpha = _deadTime < PanicSeconds
            ? Math.Min(1f, _deadTime * 6f)
            : Math.Max(0f, 1f - (_deadTime - PanicSeconds) * 2f);

        var since = _deadTime - PanicSeconds;
        _hud.DeathAlpha = since <= 0f || since >= BannerFadeIn + BannerHold + BannerFadeOut
            ? 0f
            : since < BannerFadeIn
                ? since / BannerFadeIn
                : 1f - Math.Clamp((since - BannerFadeIn - BannerHold) / BannerFadeOut, 0f, 1f);
    }

    /// <summary>The edge tint, the jitter and the torn slice. Reduced motion keeps the tint and drops the rest.</summary>
    private void UpdateMotion(WolfmedSyntheticTier tier, float strain, bool standby, float frameTime)
    {
        var pulse = 0.5f + 0.5f * MathF.Sin(_time * 2.4f);
        var tint = tier switch
        {
            WolfmedSyntheticTier.Heavy => 0.45f + 0.3f * (_reducedMotion ? 0.5f : pulse),
            WolfmedSyntheticTier.Moderate => 0.22f + 0.2f * strain,
            _ => 0f,
        };

        Approach(ref _screen.Tint, standby ? 0f : tint, frameTime);

        if (_reducedMotion || tier != WolfmedSyntheticTier.Heavy || standby)
        {
            _screen.Glitch = 0f;
            _hud.Jitter = 0f;
            return;
        }

        // A single-frame slice tear now and then, and a one-pixel wobble for a fraction of a second.
        if (_time >= _nextGlitch)
        {
            _nextGlitch = _time + _random.NextFloat(1.1f, 3.4f) * (1.4f - strain);
            _screen.Glitch = _random.NextFloat(0.35f, 1f);
            _screen.Slice = _random.NextFloat(0.1f, 0.9f);
            _jitterUntil = _time + 0.18f;
        }
        else
        {
            _screen.Glitch = 0f;
        }

        _hud.Jitter = _time < _jitterUntil ? (_random.Prob(0.5f) ? 1f : -1f) : 0f;
    }

    private void Fade(float frameTime, out bool idle)
    {
        Approach(ref _hud.SystemAlpha, 0f, frameTime);
        Approach(ref _hud.DiagnosticsAlpha, 0f, frameTime);
        Approach(ref _hud.StandbyAlpha, 0f, frameTime);
        Approach(ref _screen.Tint, 0f, frameTime);
        Approach(ref _screen.Standby, 0f, frameTime);
        _hud.BannerAlpha = Math.Max(0f, _hud.BannerAlpha - frameTime * 3f);
        _hud.PanicAlpha = Math.Max(0f, _hud.PanicAlpha - frameTime * 3f);
        _hud.DeathAlpha = Math.Max(0f, _hud.DeathAlpha - frameTime * 2f);
        _screen.Glitch = 0f;
        _hud.Jitter = 0f;

        idle = _hud.SystemAlpha <= 0f && _hud.DiagnosticsAlpha <= 0f &&
               _hud.StandbyAlpha <= 0f && _hud.BannerAlpha <= 0f && _hud.PanicAlpha <= 0f &&
               _hud.DeathAlpha <= 0f && _screen.Tint <= 0f && _screen.Standby <= 0f;
    }

    private static void Approach(ref float value, float target, float frameTime)
    {
        value += (target - value) * Math.Min(1f, frameTime * 4f);
        if (MathF.Abs(target - value) < 0.004f)
            value = target;
    }

    private static string Percent(float fraction) => $"{(int) MathF.Round(Math.Clamp(fraction, 0f, 1f) * 100f)}%";

    private static string Tag(WolfmedSyntheticSeverity severity) => severity switch
    {
        WolfmedSyntheticSeverity.Warn => "wolfmed-synthetic-tag-warn",
        WolfmedSyntheticSeverity.Crit => "wolfmed-synthetic-tag-crit",
        WolfmedSyntheticSeverity.Fail => "wolfmed-synthetic-tag-fail",
        _ => "wolfmed-synthetic-tag-info",
    };

    private void SetOverlays(bool on)
    {
        if (on != _hudAdded)
        {
            _hudAdded = on;
            if (on)
                _overlays.AddOverlay(_hud);
            else
                _overlays.RemoveOverlay(_hud);
        }

        if (on == _screenAdded)
            return;

        _screenAdded = on;
        if (on)
            _overlays.AddOverlay(_screen);
        else
            _overlays.RemoveOverlay(_screen);
    }
}
