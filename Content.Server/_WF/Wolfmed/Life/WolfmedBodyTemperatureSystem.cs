using Content.Server._WF.Wolfmed.Consciousness;
using Content.Server.Body.Components;
using Content.Server.Temperature.Components;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.Consciousness;
using Content.Shared.Atmos.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Rejuvenate;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;

namespace Content.Server._WF.Wolfmed.Life;

/// <summary>Hypothermia and heat illness on a wound host, staged from the species' temperature thresholds.</summary>
// Cold: hypothermic (Downed), then Unconscious and breathing, then the heart stops (cause "cold", the existing cold
// protection on the brain). Heat: heat exhaustion (Downed), then heat stroke (Unconscious) with its own brain drain.
// A fire grants a grace that only delays entering a heat cause.
/// <remarks>
/// The lines read a core temperature, not the surface one the atmosphere moves. In space the surface reaches about
/// 16 K within a minute and in a 235 K freezer it settles near 256 K within one (the M5 measurement), so read
/// directly every exposure would be a cold arrest within seconds. The core closes the gap to a colder surface over
/// wolfmed.core_cooling_seconds, and a hotter one over wolfmed.core_heating_seconds; it hurries back toward normal.
/// A container that protects its occupant from cold damage (a cryo pod) holds the core where it is.
/// </remarks>
public sealed class WolfmedBodyTemperatureSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private WolfmedConsciousnessSystem _consciousness = default!;
    [Dependency] private WolfmedShutdownSystem _shutdown = default!;

    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(1);
    private readonly List<EntityUid> _due = new();
    private TimeSpan _nextTick;

    /// <summary>A species' temperature lines in kelvin. Unreachable lines are infinities.</summary>
    public readonly record struct Lines(
        float Normal, float ColdDown, float ColdOut, float ColdArrest, float HeatDown, float HeatOut);

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WolfmedBodyTemperatureComponent, RejuvenateEvent>(OnRejuvenate);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_timing.CurTime < _nextTick)
            return;

        _nextTick = _timing.CurTime + TickInterval;
        _due.Clear();
        var query = EntityQueryEnumerator<WolfmedConsciousnessComponent, TemperatureComponent>();
        while (query.MoveNext(out var uid, out _, out _))
            _due.Add(uid);

        foreach (var body in _due)
        {
            if (!TerminatingOrDeleted(body))
                Tick(body, (float) TickInterval.TotalSeconds);
        }
    }

    /// <summary>
    /// One body's core and inputs. Public so a test can fast-forward the core against a surface it holds still; the
    /// life tick does not run it, because the surface it follows only moves in real time.
    /// </summary>
    public void Tick(EntityUid body, float seconds)
    {
        if (seconds <= 0f || !_consciousness.OwnsMobState(body) || !TryComp(body, out TemperatureComponent? temperature))
            return;

        if (_shutdown.IsMechanical(body) || GetLines(body) is not { } lines)
        {
            if (TryComp(body, out WolfmedBodyTemperatureComponent? stale))
                SetLevels(body, stale, 0f, 0f, 0f, 0f);
            return;
        }

        var comp = EnsureComp<WolfmedBodyTemperatureComponent>(body);
        var surface = temperature.CurrentTemperature;
        comp.Surface = surface;

        // A body arrives with a normal core. In space its surface has already lost tens of kelvin by the first tick.
        comp.Core = float.IsNaN(comp.Core)
            ? MathF.Max(surface, lines.Normal)
            : NextCore(comp.Core, surface, lines.Normal, seconds, temperature.ParentColdDamageThreshold != null);

        // A corpse's core still warms and cools, for the defibrillator's cold gate; it has no inputs.
        if (_mobState.IsDead(body))
        {
            SetLevels(body, comp, 0f, 0f, 0f, 0f);
            return;
        }

        var graced = UpdateGrace(body, comp, lines, seconds);
        var floor = Math.Clamp(_cfg.GetCVar(WolfmedCVars.TemperatureInputFloor), 0f, 1f);

        var coldDown = Level(lines.Normal - comp.Core, lines.Normal - lines.ColdDown, floor);
        var coldOut = Level(lines.Normal - comp.Core, lines.Normal - lines.ColdOut, floor);
        var heatDown = Level(comp.Core - lines.Normal, lines.HeatDown - lines.Normal, floor);
        var heatOut = Level(comp.Core - lines.Normal, lines.HeatOut - lines.Normal, floor);

        // The grace only delays entering a heat cause: a body already in one keeps it through a fire.
        if (graced && !comp.HeatHeld)
        {
            heatDown = 0f;
            heatOut = 0f;
        }
        else if (heatDown >= 1f)
        {
            comp.HeatHeld = true;
        }

        if (heatDown < 1f - _cfg.GetCVar(WolfmedCVars.ConsciousnessHysteresis))
            comp.HeatHeld = false;

        SetLevels(body, comp, coldDown, coldOut, heatDown, heatOut);
    }

    /// <summary>The next core temperature: it lags the surface away from normal and hurries back toward it.</summary>
    // Above normal it closes on a hotter surface over wolfmed.core_heating_seconds, so heat stroke takes time, and
    // falls back over wolfmed.core_recovery_seconds; warming from below normal is instant. Below normal it closes on
    // a colder surface over wolfmed.core_cooling_seconds. Held still in a container that protects from cold damage.
    private float NextCore(float core, float surface, float normal, float seconds, bool held)
    {
        if (core < normal && surface > core)
        {
            // Warming back toward normal is at once; anything past normal is chased over the heating time.
            core = MathF.Min(surface, normal);
            if (surface <= normal)
                return core;
        }

        // Over normal and the air is hotter still: the timed limit before overheating.
        if (surface > core)
            return core + (surface - core) * Fraction(seconds, WolfmedCVars.CoreHeatingSeconds);

        // Over normal and the air is cooler: back toward normal quickly, never below it on this branch.
        if (core > normal)
        {
            var target = MathF.Max(surface, normal);
            return core - (core - target) * Fraction(seconds, WolfmedCVars.CoreRecoverySeconds);
        }

        if (held || surface >= core)
            return core;

        return core - (core - surface) * Fraction(seconds, WolfmedCVars.CoreCoolingSeconds);
    }

    /// <summary>The share of a gap closed in <paramref name="seconds"/> with the time constant the cvar holds.</summary>
    private float Fraction(float seconds, CVarDef<float> tauCvar)
    {
        var tau = _cfg.GetCVar(tauCvar);
        return tau <= 0f ? 1f : 1f - MathF.Exp(-seconds / tau);
    }


    /// <summary>Updates the fire grace that delays entering a heat cause and returns whether it holds now.</summary>
    // Granted on catching fire unless already in a heat cause; lasts while burning and wolfmed.heat_fire_grace_seconds
    // after the fire first goes out, which re-ignition never moves. A new one comes only once the grace is over and
    // the core is back under the heat exhaustion line.
    private bool UpdateGrace(EntityUid body, WolfmedBodyTemperatureComponent comp, Lines lines, float seconds)
    {
        var onFire = TryComp(body, out FlammableComponent? flammable) && flammable.OnFire;

        if (!comp.GraceGranted && onFire && !comp.HeatHeld)
        {
            comp.GraceGranted = true;
            comp.GraceLeft = null;
        }

        if (comp.GraceGranted)
        {
            if (comp.GraceLeft is { } left)
                comp.GraceLeft = left - seconds;
            else if (!onFire)
                comp.GraceLeft = MathF.Max(0f, _cfg.GetCVar(WolfmedCVars.HeatFireGraceSeconds));
        }

        var holds = comp.GraceGranted && (comp.GraceLeft is not { } remaining || remaining > 0f);
        if (comp.GraceGranted && !holds && comp.Core < lines.HeatDown)
        {
            comp.GraceGranted = false;
            comp.GraceLeft = null;
        }

        return holds;
    }

    /// <summary>How far past normal toward a line the core is, 1 at the line; nothing under the floor share.</summary>
    private static float Level(float offset, float span, float floor)
    {
        if (span <= 0f || float.IsInfinity(span) || offset <= 0f)
            return 0f;

        var level = offset / span;
        return level <= floor ? 0f : level;
    }

    /// <summary>
    /// The hottest a fever may push this body (plan §3.10: a fever never Downs): the profile's fever temperature, or the
    /// point where this species' heat starts to count if that is lower. A species with a cold normal temperature would
    /// otherwise be pushed into heat stroke by a human fever.
    /// </summary>
    public float FeverCeiling(EntityUid body, float fever)
    {
        if (GetLines(body) is not { } lines || float.IsInfinity(lines.HeatDown))
            return fever;

        var floor = Math.Clamp(_cfg.GetCVar(WolfmedCVars.TemperatureInputFloor), 0f, 1f);
        return MathF.Min(fever, lines.Normal + floor * (lines.HeatDown - lines.Normal));
    }

    private void SetLevels(EntityUid body, WolfmedBodyTemperatureComponent comp, float coldDown, float coldOut,
        float heatDown, float heatOut)
    {
        if (MathF.Abs(comp.ColdDown - coldDown) < 0.001f && MathF.Abs(comp.ColdOut - coldOut) < 0.001f &&
            MathF.Abs(comp.HeatDown - heatDown) < 0.001f && MathF.Abs(comp.HeatOut - heatOut) < 0.001f)
            return;

        comp.ColdDown = coldDown;
        comp.ColdOut = coldOut;
        comp.HeatDown = heatDown;
        comp.HeatOut = heatOut;
        _consciousness.Refresh(body);
    }

    /// <summary>
    /// The body's lines: the damage thresholds (a protecting container's when it is inside one) moved by the plan's
    /// offsets. The offsets shrink for a species whose normal temperature sits close to a threshold, so no line comes
    /// nearer the normal temperature than wolfmed.temperature_line_max_share of the gap allows.
    /// </summary>
    public Lines? GetLines(EntityUid body)
    {
        if (!TryComp(body, out TemperatureComponent? temperature))
            return null;

        var cold = temperature.ParentColdDamageThreshold ?? temperature.ColdDamageThreshold;
        var heat = temperature.ParentHeatDamageThreshold ?? temperature.HeatDamageThreshold;
        var normal = TryComp(body, out ThermalRegulatorComponent? regulator) &&
                     regulator.NormalBodyTemperature > cold && regulator.NormalBodyTemperature < heat
            ? regulator.NormalBodyTemperature
            : (cold + heat) / 2f;

        var share = Math.Clamp(_cfg.GetCVar(WolfmedCVars.TemperatureLineMaxShare), 0f, 1f);
        var coldDown = float.NegativeInfinity;
        var coldOut = float.NegativeInfinity;
        var coldArrest = float.NegativeInfinity;
        var downOffset = MathF.Max(0f, _cfg.GetCVar(WolfmedCVars.HypothermiaDownOffset));
        if (normal > cold && downOffset > 0f)
        {
            var scale = MathF.Min(1f, share * (normal - cold) / downOffset);
            coldDown = cold + downOffset * scale;
            coldOut = cold + MathF.Max(0f, _cfg.GetCVar(WolfmedCVars.HypothermiaOutOffset)) * scale;
            coldArrest = cold + MathF.Max(0f, _cfg.GetCVar(WolfmedCVars.HypothermiaArrestOffset)) * scale;
        }

        var heatDown = float.PositiveInfinity;
        var heatOut = float.PositiveInfinity;
        var heatOffset = MathF.Max(0f, _cfg.GetCVar(WolfmedCVars.HyperthermiaDownOffset));
        if (heat > normal)
        {
            var scale = heatOffset > 0f ? MathF.Min(1f, share * (heat - normal) / heatOffset) : 0f;
            heatDown = heat - heatOffset * scale;
            heatOut = heat;
        }

        return new Lines(normal, coldDown, coldOut, coldArrest, heatDown, heatOut);
    }

    /// <summary>The inputs consciousness reads: past 1 is past the line. Nothing for a body with no reading yet.</summary>
    public (float ColdDown, float ColdOut, float HeatDown, float HeatOut) GetLevels(EntityUid body) =>
        TryComp(body, out WolfmedBodyTemperatureComponent? comp)
            ? (comp.ColdDown, comp.ColdOut, comp.HeatDown, comp.HeatOut)
            : (0f, 0f, 0f, 0f);

    /// <summary>The core temperature in kelvin, or null before the first reading and on a body with no route.</summary>
    public float? GetCore(EntityUid body) =>
        TryComp(body, out WolfmedBodyTemperatureComponent? comp) && !float.IsNaN(comp.Core) ? comp.Core : null;

    /// <summary>Sets the core outright and re-reads the inputs against the surface as it is. Test and admin seam.</summary>
    public void SetCore(EntityUid body, float kelvin)
    {
        EnsureComp<WolfmedBodyTemperatureComponent>(body).Core = kelvin;
        Tick(body, 0.0001f);
    }

    /// <summary>Heat stroke: the core past the heat line with no grace holding it off. Its brain drain runs.</summary>
    public bool InHeatStroke(EntityUid body) =>
        TryComp(body, out WolfmedBodyTemperatureComponent? comp) && comp.HeatOut >= 1f;

    /// <summary>M5: the heat stroke brain drain, oxygenation a second.</summary>
    public float DrainRate(EntityUid body)
    {
        var seconds = _cfg.GetCVar(WolfmedCVars.HyperthermiaBrainSeconds);
        return seconds > 0f && InHeatStroke(body) ? 1f / seconds : 0f;
    }

    /// <summary>The core under the cold arrest line: the heart stops, cause "cold", and a shock is refused.</summary>
    public bool IsColdArrest(EntityUid body) =>
        TryComp(body, out WolfmedBodyTemperatureComponent? comp) && !float.IsNaN(comp.Core) &&
        GetLines(body) is { } lines && comp.Core < lines.ColdArrest && !_shutdown.IsMechanical(body);

    /// <summary>Hypothermic, with the surface still colder than the core: getting worse.</summary>
    public bool StillCooling(EntityUid body) =>
        TryComp(body, out WolfmedBodyTemperatureComponent? comp) && comp.ColdDown >= 1f &&
        comp.Surface < comp.Core;

    /// <summary>A rejuvenated body is back at its normal temperature, with no grace spent.</summary>
    private void OnRejuvenate(Entity<WolfmedBodyTemperatureComponent> ent, ref RejuvenateEvent args)
    {
        ent.Comp.Core = GetLines(ent) is { } lines ? lines.Normal : float.NaN;
        ent.Comp.GraceGranted = false;
        ent.Comp.GraceLeft = null;
        ent.Comp.HeatHeld = false;
        ent.Comp.ColdDown = ent.Comp.ColdOut = ent.Comp.HeatDown = ent.Comp.HeatOut = 0f;
    }
}
