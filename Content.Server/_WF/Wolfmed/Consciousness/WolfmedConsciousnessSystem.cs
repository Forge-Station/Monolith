using Content.Server._WF.Wolfmed.Life;
using Content.Server.Body.Components;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.Consciousness;
using Content.Shared._WF.Wolfmed.Life;
using Content.Shared._WF.Wolfmed.Reagents;
using Content.Shared.Body.Components;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Rejuvenate;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;

namespace Content.Server._WF.Wolfmed.Consciousness;

/// <summary>
/// Owns Alive and Critical on a wound host. Damage totals decide nothing here: pain, blood, working legs and
/// outside pressures are each read against their own thresholds, the worst one wins, and hysteresis keeps a
/// bleeding body from flickering between states.
/// </summary>
/// <remarks>
/// <para>
/// M1a: every input is kept with the cause it stands for, so the state carries a <see cref="WolfmedCause"/>
/// and the other inputs still holding the body as Blockers (plan §5.1). Pain no longer holds a body
/// unconscious: crossing the faint line starts a pain faint of fixed length instead (plan §3.1).
/// </para>
/// <para>
/// Dead is not this system's to give. It stays on the paths that never went through the damage thresholds (a
/// destroyed brain, a gib, an admin).
/// </para>
/// </remarks>
public sealed class WolfmedConsciousnessSystem : SharedWolfmedConsciousnessSystem
{
    [Dependency] private IConfigurationManager _configuration = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private PainSystem _pain = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private WolfmedCallForHelpSystem _callForHelp = default!;
    [Dependency] private WolfmedConditionAlertSystem _conditionAlerts = default!;
    [Dependency] private WolfmedDyingActionsSystem _dyingActions = default!;
    [Dependency] private WolfmedPainAlertSystem _painAlert = default!; // playtest 4, VISUALS
    [Dependency] private WolfmedPainReliefSystem _relief = default!;
    [Dependency] private WolfmedShutdownSystem _shutdown = default!;
    [Dependency] private WolfmedCrawlActionsSystem _crawlActions = default!; // M2
    [Dependency] private WolfmedToxinSystem _toxin = default!; // M5
    [Dependency] private WolfmedRadiationSystem _radiation = default!;
    [Dependency] private WolfmedBodyTemperatureSystem _temperature = default!;

    /// <summary>
    /// How much of an external pressure is enough to put a body on the floor. 1 is unconscious, so anything
    /// most of the way there is Downed first.
    /// </summary>
    private const float PressureDownShare = 0.7f;

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(0.5);

    /// <summary>
    /// How long Downed lasts at the least. Hysteresis alone is not enough on the edge itself: pain, relief
    /// and an outside pressure all move every tick, and each flip out of Downed and back put the body on the
    /// floor again with another fall sound.
    /// </summary>
    private static readonly TimeSpan MinDownedTime = TimeSpan.FromSeconds(2);

    private float _painDown;
    private float _painOut;
    private float _bloodDown;
    private float _bloodOut;
    private float _hysteresis;
    private float _faintSeconds;
    private float _faintRise;
    private float _faintCooldown;

    /// <summary>One input per cause, rebuilt on every evaluation.</summary>
    private readonly Dictionary<WolfmedCause, (float Down, float Out)> _inputs = new();

    public override void Initialize()
    {
        base.Initialize();
        Subs.CVar(_configuration, WolfmedCVars.ConsciousnessPainDown, value => _painDown = value, true);
        Subs.CVar(_configuration, WolfmedCVars.ConsciousnessPainOut, value => _painOut = value, true);
        Subs.CVar(_configuration, WolfmedCVars.ConsciousnessBloodDown, value => _bloodDown = value, true);
        Subs.CVar(_configuration, WolfmedCVars.ConsciousnessBloodOut, value => _bloodOut = value, true);
        Subs.CVar(_configuration, WolfmedCVars.ConsciousnessHysteresis, value => _hysteresis = value, true);
        Subs.CVar(_configuration, WolfmedCVars.PainFaintSeconds, value => _faintSeconds = value, true);
        Subs.CVar(_configuration, WolfmedCVars.PainFaintRise, value => _faintRise = value, true);
        Subs.CVar(_configuration, WolfmedCVars.PainFaintCooldown, value => _faintCooldown = value, true);

        SubscribeLocalEvent<WoundHostComponent, ComponentStartup>(OnWoundHostStartup);
        SubscribeLocalEvent<WolfmedConsciousnessComponent, PainChangedEvent>(OnPainChanged);
        SubscribeLocalEvent<WolfmedConsciousnessComponent, DamageChangedEvent>(OnDamageChanged);
        SubscribeLocalEvent<WolfmedConsciousnessComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<WolfmedConsciousnessComponent, RejuvenateEvent>(OnRejuvenate);
        SubscribeLocalEvent<BodyPartFunctionalityComponent, BodyPartFunctionalityChangedEvent>(OnFunctionality);
    }

    private void OnWoundHostStartup(Entity<WoundHostComponent> ent, ref ComponentStartup args)
    {
        EnsureComp<WolfmedConsciousnessComponent>(ent);

        // M1a: the stock health alerts read damage totals, which say nothing on a wound host.
        _conditionAlerts.TakeOver(ent);
    }

    /// <inheritdoc/>
    public override void SetExternalPressure(EntityUid body, string key, float level)
    {
        if (!TryComp(body, out WolfmedConsciousnessComponent? consciousness))
            return;

        if (level <= 0f)
        {
            if (!consciousness.Pressures.Remove(key))
                return;
        }
        else
        {
            // M2: a small step onto the full line still lands, or a ramp stalls just short of 1 and never crosses.
            if (consciousness.Pressures.TryGetValue(key, out var old) && MathF.Abs(old - level) < 0.001f &&
                old >= 1f == level >= 1f)
                return;

            consciousness.Pressures[key] = level;
        }

        Evaluate((body, consciousness));
    }

    /// <inheritdoc/>
    public override void Refresh(EntityUid body)
    {
        if (TryComp(body, out WolfmedConsciousnessComponent? consciousness))
            Evaluate((body, consciousness));
    }

    /// <summary>
    /// The bloodstream raises nothing when its volume changes, so it hands the level over on the tick where
    /// it already knows it. One marked call in <c>BloodstreamSystem.Update</c>.
    /// </summary>
    public void OnBloodLevelChanged(EntityUid body, float fraction)
    {
        if (!TryComp(body, out WolfmedConsciousnessComponent? consciousness) ||
            MathF.Abs(consciousness.BloodFraction - fraction) < 0.001f)
            return;

        consciousness.BloodFraction = fraction;
        Evaluate((body, consciousness));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // Only bodies with something still moving are polled; a healthy crew costs nothing.
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WolfmedConsciousnessComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (!comp.Watching || now < comp.NextEvaluation)
                continue;

            comp.NextEvaluation = now + PollInterval;
            Evaluate((uid, comp));
        }
    }

    /// <summary>Reads every input, picks the worst, and applies the state and cause it names.</summary>
    public void Evaluate(Entity<WolfmedConsciousnessComponent> body)
    {
        if (TerminatingOrDeleted(body) || !OwnsMobState(body))
            return;

        if (_mobState.IsDead(body))
        {
            Apply(body, WolfmedConsciousness.Unconscious, 1f, false, WolfmedCause.None, WolfmedCauseSource.None,
                WolfmedCauseFlags.None);
            return;
        }

        var mechanical = _shutdown.IsMechanical(body);
        _inputs.Clear();

        var (painDown, faintLevel) = GetPainLevels(body, mechanical);
        AddInput(WolfmedCause.Pain, painDown, 0f);

        if (UpdatePainFaint(body, mechanical))
            AddInput(WolfmedCause.PainFaint, 0f, 1f);

        // M3: a heavy blow to the head, on the same fixed-length rules as the pain faint (plan §3.6).
        if (UpdateHeadBlow(body, mechanical))
            AddInput(WolfmedCause.HeadBlow, 0f, 1f);

        var (bloodDown, bloodOut) = GetBloodLevels(body);
        AddInput(mechanical ? WolfmedCause.Oil : WolfmedCause.Blood, bloodDown, bloodOut);

        foreach (var (key, level) in body.Comp.Pressures)
            AddInput(PressureCause(key, mechanical), level / PressureDownShare, level);

        // M5 (plan §3.8-3.10): toxins, radiation, cold and heat, each read against its own lines. Machines have none.
        if (!mechanical)
        {
            var (toxinDown, toxinOut) = _toxin.GetLevels(body);
            AddInput(WolfmedCause.Toxin, toxinDown, toxinOut);
            AddInput(WolfmedCause.Radiation, _radiation.GetDownLevel(body), 0f);
            var temperature = _temperature.GetLevels(body);
            AddInput(WolfmedCause.Cold, temperature.ColdDown, temperature.ColdOut);
            AddInput(WolfmedCause.Heat, temperature.HeatDown, temperature.HeatOut);
        }

        if (!LegsGone(body))
            body.Comp.HadLegs = true;
        else if (body.Comp.HadLegs)
            AddInput(WolfmedCause.Legs, 1f, 0f);

        if (_relief.InCrash(body.Owner))
            AddInput(WolfmedCause.Crash, 1f, 0f);

        var downLevel = 0f;
        var outLevel = 0f;
        foreach (var (down, outOf) in _inputs.Values)
        {
            downLevel = MathF.Max(downLevel, down);
            outLevel = MathF.Max(outLevel, outOf);
        }

        body.Comp.DownLevel = downLevel;
        body.Comp.OutLevel = outLevel;

        // Hysteresis: entering a state takes 1, leaving it takes falling back below 1 - the CVar.
        var leaving = 1f - _hysteresis;
        var target = body.Comp.State switch
        {
            WolfmedConsciousness.Unconscious when outLevel >= leaving => WolfmedConsciousness.Unconscious,
            WolfmedConsciousness.Unconscious when downLevel >= leaving => WolfmedConsciousness.Downed,
            WolfmedConsciousness.Downed when outLevel >= 1f => WolfmedConsciousness.Unconscious,
            WolfmedConsciousness.Downed when downLevel >= leaving => WolfmedConsciousness.Downed,
            WolfmedConsciousness.Downed => WolfmedConsciousness.Up,
            _ when outLevel >= 1f => WolfmedConsciousness.Unconscious,
            _ when downLevel >= 1f => WolfmedConsciousness.Downed,
            _ => WolfmedConsciousness.Up,
        };

        // A body that has only just gone down stays down, whatever the inputs have done since.
        if (target == WolfmedConsciousness.Up && body.Comp.State == WolfmedConsciousness.Downed &&
            _timing.CurTime < body.Comp.DownedUntil)
            target = WolfmedConsciousness.Downed;

        var (cause, blockers) = PickCause(body, target, leaving);

        var watching = downLevel > 0f || outLevel > 0f || faintLevel > 0f || target != WolfmedConsciousness.Up ||
                       _relief.GetTier(body.Owner) != WolfmedPainReliefTier.None;

        // Not a double count: the PainFaint input puts 1 in outLevel only while a faint runs, and then the body is
        // Unconscious. faintLevel is summed pain against the faint line, the only thing that deepens a Downed
        // body's view as its pain climbs towards a faint.
        var depth = Depth(target, downLevel, MathF.Max(outLevel, faintLevel), body.Comp);
        Apply(body, target, depth, watching, cause, GetSource(body, cause), blockers);
    }

    private void AddInput(WolfmedCause cause, float down, float outOf)
    {
        if (down <= 0f && outOf <= 0f)
            return;

        if (_inputs.TryGetValue(cause, out var existing))
        {
            down = MathF.Max(down, existing.Down);
            outOf = MathF.Max(outOf, existing.Out);
        }

        _inputs[cause] = (down, outOf);
    }

    /// <summary>Picks the cause of a consciousness state and the other inputs that block leaving it.</summary>
    // The cause is the input that meets the state's line, first in the tie order; an input only inside its leave band
    // names it only when nothing crosses the line. Every other input at or past its leave line is a blocker. Critical
    // states read the Unconscious line, Downed the Downed line.
    private (WolfmedCause Cause, WolfmedCauseFlags Blockers) PickCause(Entity<WolfmedConsciousnessComponent> body,
        WolfmedConsciousness state, float leaving)
    {
        if (state == WolfmedConsciousness.Up)
            return (WolfmedCause.None, WolfmedCauseFlags.None);

        var critical = state == WolfmedConsciousness.Unconscious;
        var crossing = WolfmedCause.None;
        var inBand = WolfmedCause.None;
        var meeting = WolfmedCauseFlags.None;

        foreach (var cause in WolfmedCauses.Priority)
        {
            if (!_inputs.TryGetValue(cause, out var input))
                continue;

            var level = critical ? input.Out : input.Down;
            if (level < leaving)
                continue;

            meeting |= WolfmedCauses.Flag(cause);
            if (level >= 1f && crossing == WolfmedCause.None)
                crossing = cause;
            else if (inBand == WolfmedCause.None)
                inBand = cause;
        }

        var picked = crossing != WolfmedCause.None ? crossing : inBand;

        // Only the Downed dwell holds a body with nothing past a line: keep naming what put it there.
        if (picked == WolfmedCause.None)
            picked = body.Comp.State == state && body.Comp.Cause != WolfmedCause.None ? body.Comp.Cause : Strongest(critical);

        return (picked, meeting & ~WolfmedCauses.Flag(picked));
    }

    private WolfmedCause Strongest(bool critical)
    {
        var best = WolfmedCause.None;
        var bestLevel = 0f;
        foreach (var (cause, input) in _inputs)
        {
            var level = critical ? input.Out : input.Down;
            if (level > bestLevel)
            {
                best = cause;
                bestLevel = level;
            }
        }

        return best;
    }

    private static WolfmedCause PressureCause(string key, bool mechanical) => key switch
    {
        WolfmedLifeSystem.ArrestPressure => WolfmedCause.Arrest,
        WolfmedLifeSystem.HypoxiaPressure => WolfmedCause.Hypoxia,
        WolfmedPainReliefSystem.SedationPressure => WolfmedCause.Sedation,
        WolfmedShutdownSystem.ShutdownPressure => WolfmedCause.Shutdown,
        WolfmedLifeSystem.InjuryPressure => mechanical ? WolfmedCause.Core : WolfmedCause.Brain, // M3
        WolfmedOverheatSystem.CoreHeatPressure => WolfmedCause.CoreHeat, // M4
        Wounds.WolfmedInfectionSystem.ShockPressure => WolfmedCause.SepticShock, // Playtest 5
        _ => WolfmedCause.Other,
    };

    /// <summary>The sub-source a cause with several routes carries: the drain, the trigger, the reason.</summary>
    private WolfmedCauseSource GetSource(Entity<WolfmedConsciousnessComponent> body, WolfmedCause cause)
    {
        return cause switch
        {
            WolfmedCause.Hypoxia => body.Comp.HypoxiaSource,
            WolfmedCause.Arrest => CompOrNull<WolfmedCardiacArrestComponent>(body)?.Cause switch
            {
                "blood" => WolfmedCauseSource.ArrestBlood,
                "oxygen" => WolfmedCauseSource.ArrestOxygen,
                "heart" => WolfmedCauseSource.ArrestHeart,
                "sepsis" => WolfmedCauseSource.ArrestSepsis,
                "shock" => WolfmedCauseSource.ArrestShock,
                "cold" => WolfmedCauseSource.ArrestCold, // M5
                "toxin" => WolfmedCauseSource.ArrestToxin,
                "heat" => WolfmedCauseSource.ArrestHeat,
                _ => WolfmedCauseSource.ArrestOther,
            },
            WolfmedCause.Shutdown => CompOrNull<WolfmedShutdownComponent>(body)?.Reason ?? WolfmedCauseSource.Power,
            _ => WolfmedCauseSource.None,
        };
    }

    /// <summary>
    /// Downed carries the first third of the dying view, Unconscious the rest. M2 (plan §5.2): the Unconscious depth
    /// follows the active route toward arrest (blood 35% to 30%, oxygenation 0.45 to 0.15) instead of a pressure past 1.
    /// </summary>
    private float Depth(WolfmedConsciousness state, float downLevel, float outLevel, WolfmedConsciousnessComponent comp)
    {
        return state switch
        {
            WolfmedConsciousness.Downed => 0.35f + 0.2f * Math.Clamp(outLevel, 0f, 1f),
            WolfmedConsciousness.Unconscious => WolfmedDyingDepth.Unconscious(WolfmedDyingDepth.RouteProgress(
                comp.BloodFraction, _bloodOut, _configuration.GetCVar(WolfmedCVars.ArrestBlood),
                comp.Oxygenation, _configuration.GetCVar(WolfmedCVars.BrainPressureOut),
                _configuration.GetCVar(WolfmedCVars.ArrestOxygenation))),
            _ => 0.35f * Math.Clamp(downLevel, 0f, 1f),
        };
    }

    private void Apply(Entity<WolfmedConsciousnessComponent> body, WolfmedConsciousness state, float depth,
        bool watching, WolfmedCause cause, WolfmedCauseSource source, WolfmedCauseFlags blockers)
    {
        var was = body.Comp.State;
        var oldCause = body.Comp.Cause;
        var oldBlockers = body.Comp.Blockers;
        body.Comp.WasUp |= state == WolfmedConsciousness.Up;
        body.Comp.Watching = watching;

        if (body.Comp.State != state || MathF.Abs(body.Comp.Depth - depth) >= 0.005f ||
            body.Comp.Cause != cause || body.Comp.CauseSource != source || body.Comp.Blockers != blockers)
        {
            body.Comp.State = state;
            body.Comp.Depth = depth;
            body.Comp.Cause = cause;
            body.Comp.CauseSource = source;
            body.Comp.Blockers = blockers;
            Dirty(body);
        }

        if (state == WolfmedConsciousness.Downed)
        {
            // Only a body that has been on its feet dwells. One still being assembled has no legs yet,
            // which reads as both legs gone, and a dwell there would hold a fresh crewman down for two
            // seconds before they had ever stood up.
            if (was != WolfmedConsciousness.Downed && body.Comp.WasUp)
                body.Comp.DownedUntil = _timing.CurTime + MinDownedTime;

            EnsureComp<WolfmedDownedComponent>(body);
        }
        else
        {
            RemComp<WolfmedDownedComponent>(body);
        }

        // M1a: Call for help exists while Downed and nowhere else (plan §5.3).
        _callForHelp.Refresh(body, state == WolfmedConsciousness.Downed);

        // M2: Check yourself while Up or Downed, Play dead while Downed (plan §5.3).
        _crawlActions.Refresh(body, state);

        if (!_mobState.IsDead(body))
        {
            var mobState = state == WolfmedConsciousness.Unconscious ? MobState.Critical : MobState.Alive;
            if (_mobState.HasState(body, mobState))
                _mobState.ChangeMobState(body, mobState);
        }

        if (was != state || oldCause != cause || oldBlockers != blockers)
        {
            var ev = new WolfmedConsciousnessChangedEvent(was, state, oldCause, cause, oldBlockers, blockers);
            RaiseLocalEvent(body, ref ev);
        }

        if (state == WolfmedConsciousness.Up)
            _conditionAlerts.RefreshHealthSeverity(body);

        // Playtest 4, VISUALS: the pain HUD follows every evaluation, as the health doll does.
        _painAlert.Refresh(body);
    }

    /// <summary>
    /// Downed reads the effective pain the vignette reads: the body's value, which is min(soft cap, Σ parts)
    /// since P13, less relief. The second value is summed pain against the faint line, for the dying view.
    /// </summary>
    private (float Down, float Faint) GetPainLevels(Entity<WolfmedConsciousnessComponent> body, bool mechanical)
    {
        if (!TryComp(body, out PainComponent? pain) || pain.SoftPainCap <= FixedPoint2.Zero)
            return (0f, 0f);

        var cap = pain.SoftPainCap.Float();
        var down = 0f;

        if (!_relief.LiftsDowned(body.Owner) && _painDown > 0f)
        {
            var effective = _pain.GetPain((body.Owner, pain)).Float() - _relief.GetRelief(body.Owner);
            down = MathF.Max(0f, effective) / (cap * _painDown);
        }

        var faint = mechanical || _painOut <= 0f ? 0f : GetUncappedPain(body) / (cap * _painOut);
        return (down, faint);
    }

    /// <summary>Runs the pain faint and returns whether one is running now.</summary>
    // A faint lasts a fixed time that nothing extends. It re-arms only when summed pain falls under the leave line or
    // rises by wolfmed.pain_faint_rise over the pain at waking, never inside the cooldown after waking. A strong or
    // emergency painkiller ends it and blocks the next; a mechanical body never faints.
    private bool UpdatePainFaint(Entity<WolfmedConsciousnessComponent> body, bool mechanical)
    {
        var comp = body.Comp;
        if (mechanical || _painOut <= 0f || !TryComp(body, out PainComponent? pain) ||
            pain.SoftPainCap <= FixedPoint2.Zero)
        {
            comp.PainFaintUntil = null;
            comp.PainFaintStart = null;
            return false;
        }

        var now = _timing.CurTime;
        var summed = GetUncappedPain(body);
        var line = pain.SoftPainCap.Float() * _painOut;
        var blocked = _relief.EndsFaint(body.Owner);

        if (comp.PainFaintUntil is { } until)
        {
            if (now < until && !blocked)
                return true;

            // Waking. The baseline is the pain now, so what was added during the faint never counts.
            comp.PainFaintUntil = null;
            comp.PainFaintStart = null;
            comp.PainFaintBaseline = summed;
            comp.PainFaintCooldownUntil = now + TimeSpan.FromSeconds(MathF.Max(0f, _faintCooldown));
            comp.PainFaintArmed = false;
            return false;
        }

        if (summed < line * (1f - _hysteresis))
            comp.PainFaintArmed = true;

        if (blocked || now < comp.PainFaintCooldownUntil || summed < line)
            return false;

        if (!comp.PainFaintArmed && summed < comp.PainFaintBaseline + _faintRise)
            return false;

        comp.PainFaintStart = now;
        comp.PainFaintUntil = now + TimeSpan.FromSeconds(MathF.Max(0f, _faintSeconds));
        comp.PainFaintArmed = false;
        return true;
    }

    /// <summary>
    /// M3: knocks the patient out for <c>wolfmed.head_knockout_seconds</c> after a heavy blow to the head (plan
    /// §3.6). A blow during the knockout never lengthens it. Machines never faint (OD9), and nor does a body
    /// consciousness does not run. Called by <c>WolfmedOrganThresholdSystem</c> on the hit.
    /// </summary>
    public bool StartHeadBlow(EntityUid body)
    {
        if (!TryComp(body, out WolfmedConsciousnessComponent? comp) || !OwnsMobState(body) ||
            _mobState.IsDead(body) || _shutdown.IsMechanical(body))
            return false;

        var now = _timing.CurTime;
        if (comp.HeadBlowUntil > now)
            return false;

        comp.HeadBlowStart = now;
        comp.HeadBlowUntil = now + TimeSpan.FromSeconds(MathF.Max(0f,
            _configuration.GetCVar(WolfmedCVars.HeadKnockoutSeconds)));
        Evaluate((body, comp));
        return true;
    }

    /// <summary>Whether a head-blow knockout is running; clears it once it has run out.</summary>
    private bool UpdateHeadBlow(Entity<WolfmedConsciousnessComponent> body, bool mechanical)
    {
        if (body.Comp.HeadBlowUntil is not { } until)
            return false;

        if (!mechanical && _timing.CurTime < until)
            return true;

        body.Comp.HeadBlowUntil = null;
        body.Comp.HeadBlowStart = null;
        return false;
    }

    /// <summary>
    /// The body is out in a faint: Critical with a faint as its cause, a pain faint or (M3) a head blow. What the
    /// autodoc asks before treating Critical as an emergency (plan §7.2).
    /// </summary>
    public bool InFaint(EntityUid body) =>
        TryComp(body, out WolfmedConsciousnessComponent? comp) && !_mobState.IsDead(body) &&
        comp.State == WolfmedConsciousness.Unconscious && WolfmedCauses.IsFaint(comp.Cause);

    /// <summary>
    /// Playtest 2: the running faint's start and end, for the alert's countdown and the seconds in the text.
    /// M3: the head blow's while it is the cause, else the pain faint's. Null when no faint runs.
    /// </summary>
    public (TimeSpan Start, TimeSpan End)? GetFaintWindow(EntityUid body)
    {
        if (!TryComp(body, out WolfmedConsciousnessComponent? comp))
            return null;

        var now = _timing.CurTime;
        if (comp.HeadBlowUntil is { } blow && blow > now &&
            (comp.Cause == WolfmedCause.HeadBlow || comp.PainFaintUntil is not { } pain || pain <= now))
            return (comp.HeadBlowStart ?? now, blow);

        if (comp.PainFaintUntil is not { } until || until <= now)
            return null;

        return (comp.PainFaintStart ?? now, until);
    }

    /// <summary>
    /// Playtest 3: when the body comes round by itself, the one window the faint alert's countdown and the
    /// explanation card both read. Only while it is out in a timed faint (pain, head blow) and nothing untimed holds it
    /// too; with both faints running, the later end. Null otherwise: blood, sedation, arrest and the rest have no clock.
    /// </summary>
    public (TimeSpan Start, TimeSpan End)? GetWakeWindow(EntityUid body)
    {
        if (!TryComp(body, out WolfmedConsciousnessComponent? comp) || _mobState.IsDead(body) ||
            comp.State != WolfmedConsciousness.Unconscious || !WolfmedCauses.IsFaint(comp.Cause) ||
            (comp.Blockers & ~(WolfmedCauseFlags.PainFaint | WolfmedCauseFlags.HeadBlow)) != WolfmedCauseFlags.None)
            return null;

        var now = _timing.CurTime;
        (TimeSpan Start, TimeSpan End)? window = null;
        if (comp.PainFaintUntil is { } pain && pain > now)
            window = (comp.PainFaintStart ?? now, pain);

        if (comp.HeadBlowUntil is { } blow && blow > now && (window is not { } running || blow > running.End))
            window = (comp.HeadBlowStart ?? now, blow);

        return window;
    }

    /// <summary>Whole seconds left in the running faint (pain or head blow), rounded up; null when none runs.</summary>
    public int? GetFaintSecondsLeft(EntityUid body) =>
        GetFaintWindow(body) is { } window ? (int) Math.Ceiling((window.End - _timing.CurTime).TotalSeconds) : null;

    /// <summary>A pain faint is running on this body.</summary>
    public bool IsFainted(EntityUid body) =>
        TryComp(body, out WolfmedConsciousnessComponent? comp) && comp.PainFaintUntil > _timing.CurTime;

    /// <summary>
    /// Pain before the soft clamp. The body's own value is clamped to the soft cap, so it tops out at exactly
    /// 1.0 of it; the parts still carry the rest.
    /// </summary>
    public float GetUncappedPain(EntityUid body)
    {
        var total = 0f;
        foreach (var (part, _) in _body.GetBodyChildren(body))
        {
            if (TryComp(part, out PainComponent? pain))
                total += _pain.GetPain((part, pain)).Float();
        }

        return total;
    }

    /// <summary>Blood volume, as how far past each threshold the body is. No bloodstream, no input.</summary>
    private (float Down, float Out) GetBloodLevels(Entity<WolfmedConsciousnessComponent> body)
    {
        if (!HasComp<BloodstreamComponent>(body))
            return (0f, 0f);

        var fraction = Math.Clamp(body.Comp.BloodFraction, 0f, 1f);
        var down = _bloodDown < 1f ? (1f - fraction) / (1f - _bloodDown) : 0f;
        var outLevel = _bloodOut < 1f ? (1f - fraction) / (1f - _bloodOut) : 0f;
        return (MathF.Max(0f, down), MathF.Max(0f, outLevel));
    }

    /// <summary>
    /// Both legs gone or disabled. Shitmed already drops the body when the last leg leaves; this reads the
    /// same state as a consciousness input, so the two agree instead of fighting. Playtest 2: a leg Shitmed has
    /// switched off (<see cref="BodyPartComponent.Enabled"/>) counts as disabled too, since it is what drops the body.
    /// </summary>
    private bool LegsGone(EntityUid body)
    {
        if (!TryComp(body, out BodyComponent? comp) || comp.RequiredLegs <= 0)
            return false;

        foreach (var (part, bodyPart) in _body.GetBodyChildren(body, comp))
        {
            if (bodyPart.PartType != BodyPartType.Leg)
                continue;

            if (bodyPart.Enabled && CompOrNull<BodyPartFunctionalityComponent>(part)?.State !=
                BodyPartFunctionalityState.Disabled)
                return false;
        }

        return true;
    }

    private void OnPainChanged(Entity<WolfmedConsciousnessComponent> body, ref PainChangedEvent args)
    {
        Evaluate(body);
    }

    /// <summary>
    /// Damage still moves pain and blood indirectly, so a re-evaluation is worth the event. Suffocation is
    /// no longer read here at all: BRAIN's oxygenation clock owns it and pushes its own pressure in.
    /// </summary>
    private void OnDamageChanged(Entity<WolfmedConsciousnessComponent> body, ref DamageChangedEvent args)
    {
        Evaluate(body);
    }

    private void OnMobStateChanged(EntityUid uid, WolfmedConsciousnessComponent comp,
        MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Dead)
        {
            // M1a: the dead are past Succumb and Last Words (plan §5.4).
            _dyingActions.Revoke(uid);
            comp.PainFaintUntil = null;
            comp.PainFaintStart = null;
            comp.HeadBlowUntil = null; // M3
            comp.HeadBlowStart = null;

            // Death is someone else's (a destroyed brain, a gib, an admin). Apply drops the Downed restrictions
            // and Call for help and raises the change like any other; the alert system says nothing for the dead.
            Apply((uid, comp), WolfmedConsciousness.Unconscious, 1f, false,
                WolfmedCause.None, WolfmedCauseSource.None, WolfmedCauseFlags.None);
        }

        // M1a: the Dead alert, and the way back out of it, are the condition alert system's.
        _conditionAlerts.Refresh(uid);
    }

    private void OnRejuvenate(EntityUid uid, WolfmedConsciousnessComponent comp, RejuvenateEvent args)
    {
        comp.Pressures.Clear();
        comp.BloodFraction = 1f;
        comp.Oxygenation = 1f;
        comp.Breathing = WolfmedBreathing.Normal;
        comp.BreathingSource = WolfmedBreathingSource.None;
        comp.BloodBand = WolfmedBloodBand.Normal;
        comp.PulseIrregular = false; // M3
        comp.DownLevel = 0f;
        comp.OutLevel = 0f;
        comp.State = WolfmedConsciousness.Up;
        comp.Cause = WolfmedCause.None;
        comp.CauseSource = WolfmedCauseSource.None;
        comp.Blockers = WolfmedCauseFlags.None;
        comp.HypoxiaSource = WolfmedCauseSource.None;
        comp.PainFaintUntil = null;
        comp.PainFaintStart = null;
        comp.PainFaintArmed = true;
        comp.PainFaintBaseline = 0f;
        comp.PainFaintCooldownUntil = TimeSpan.Zero;
        comp.HeadBlowUntil = null; // M3
        comp.HeadBlowStart = null;
        comp.Depth = 0f;
        Dirty(uid, comp);
        RemComp<WolfmedDownedComponent>(uid);

        // The damage thresholds no longer revive a wound host, so this is the only thing that does. The
        // re-evaluation waits for the next tick, by which time every other rejuvenate handler has run.
        if (_mobState.HasState(uid, MobState.Alive))
            _mobState.ChangeMobState(uid, MobState.Alive);

        _conditionAlerts.Refresh(uid);
        comp.Watching = true;
        comp.NextEvaluation = TimeSpan.Zero;
    }

    private void OnFunctionality(Entity<BodyPartFunctionalityComponent> part,
        ref BodyPartFunctionalityChangedEvent args)
    {
        Refresh(args.Body);
    }
}
