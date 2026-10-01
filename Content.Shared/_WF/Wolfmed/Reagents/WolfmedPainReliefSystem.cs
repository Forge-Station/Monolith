using System.Linq;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.Consciousness;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Content.Shared.Movement.Systems;
using Content.Shared.Rejuvenate;
using Robust.Shared.Configuration;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Shared._WF.Wolfmed.Reagents;

/// <summary>
/// Painkillers: what is in a body, how much pain it discounts, which states its tier may lift, and what
/// taking too much of it costs. None of it treats a wound, which is the whole point of the tiers.
/// </summary>
public sealed class WolfmedPainReliefSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private MovementSpeedModifierSystem _movement = default!;
    [Dependency] private PainSystem _pain = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private SharedWolfmedConsciousnessSystem _consciousness = default!;

    /// <summary>Pressure key for an overdose that has stopped the patient breathing.</summary>
    public const string SedationPressure = "sedation";

    private readonly List<EntityUid> _finished = new();
    private float _accumulator;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WolfmedPainReliefComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshSpeed);
        SubscribeLocalEvent<WolfmedPainReliefComponent, RejuvenateEvent>(OnRejuvenate);
    }

    /// <summary>Strongest tier in the body right now, including an open emergency window.</summary>
    public WolfmedPainReliefTier GetTier(Entity<WolfmedPainReliefComponent?> body)
    {
        return Resolve(body, ref body.Comp, false) ? body.Comp.Tier : WolfmedPainReliefTier.None;
    }

    /// <summary>Pain units discounted from the Downed test. Every tier counts here.</summary>
    public float GetRelief(Entity<WolfmedPainReliefComponent?> body)
    {
        return Resolve(body, ref body.Comp, false) ? body.Comp.Relief : 0f;
    }

    /// <summary>Pain units discounted from the Unconscious test. A weak painkiller never reaches this.</summary>
    public float GetStrongRelief(Entity<WolfmedPainReliefComponent?> body)
    {
        return Resolve(body, ref body.Comp, false) ? body.Comp.StrongRelief : 0f;
    }

    /// <summary>A stimulant or an emergency pen: pain decides nothing about staying on your feet.</summary>
    public bool LiftsDowned(Entity<WolfmedPainReliefComponent?> body)
    {
        return GetTier(body) >= WolfmedPainReliefTier.Stimulant;
    }

    /// <summary>Inside an emergency window: pain decides nothing at all. Blood and airloss still do.</summary>
    public bool LiftsUnconscious(Entity<WolfmedPainReliefComponent?> body)
    {
        return Resolve(body, ref body.Comp, false) && body.Comp.EmergencyEnds > _timing.CurTime;
    }

    /// <summary>
    /// M1a: a strong or emergency painkiller is in the body. It ends a pain faint at once and stops a new one
    /// starting, whatever tier a stimulant on top reports (plan §3.1).
    /// </summary>
    public bool EndsFaint(Entity<WolfmedPainReliefComponent?> body)
    {
        if (!Resolve(body, ref body.Comp, false))
            return false;

        if (body.Comp.EmergencyEnds > _timing.CurTime)
            return true;

        foreach (var dose in body.Comp.Doses.Values)
        {
            if (dose.Tier is WolfmedPainReliefTier.Strong or WolfmedPainReliefTier.Emergency)
                return true;
        }

        return false;
    }

    /// <summary>The crash after an emergency window. Downed on its own, whatever the other inputs say.</summary>
    public bool InCrash(Entity<WolfmedPainReliefComponent?> body)
    {
        return Resolve(body, ref body.Comp, false) && body.Comp.CrashEnds > _timing.CurTime;
    }

    /// <summary>
    /// Strong painkillers hide the wound slowdowns, so a player can walk on a broken leg. The fracture keeps
    /// worsening underneath, which is the trade.
    /// </summary>
    /// <remarks>
    /// M2 (P30): read off the doses, like <see cref="EndsFaint"/>. The strongest tier alone let a stimulant on top of
    /// a strong painkiller bring the slowdowns back, because Stimulant outranks Strong.
    /// </remarks>
    public bool MasksSlowdown(EntityUid body)
    {
        if (!TryComp(body, out WolfmedPainReliefComponent? relief))
            return false;

        if (relief.Tier is WolfmedPainReliefTier.Strong or WolfmedPainReliefTier.Emergency)
            return true;

        foreach (var dose in relief.Doses.Values)
        {
            if (dose.Tier is WolfmedPainReliefTier.Strong or WolfmedPainReliefTier.Emergency)
                return true;
        }

        return false;
    }

    public float GetSedation(Entity<WolfmedPainReliefComponent?> body)
    {
        return Resolve(body, ref body.Comp, false) ? body.Comp.Sedation : 0f;
    }

    /// <summary>
    /// BRAIN: how far past the threshold the sedation has pushed breathing, 0 to 1. The oxygenation clock
    /// reads this exactly as it reads suffocation damage, so an overdose is still lethal without the
    /// Asphyxiation stand-in that used to sit here.
    /// </summary>
    public float GetRespiratoryDepression(Entity<WolfmedPainReliefComponent?> body)
    {
        if (!Resolve(body, ref body.Comp, false) || body.Comp.SedationAirlossThreshold >= 1f ||
            body.Comp.Sedation <= body.Comp.SedationAirlossThreshold)
            return 0f;

        return Math.Clamp(
            (body.Comp.Sedation - body.Comp.SedationAirlossThreshold) /
            (1f - body.Comp.SedationAirlossThreshold), 0f, 1f);
    }

    /// <summary>
    /// M2 (plan §3.4): the sedation the body is moving toward, 0 to 1. Every sedating dose's units in the blood
    /// times its sedation per unit; zero while an antagonist is in the blood.
    /// </summary>
    public float GetSedationTarget(Entity<WolfmedPainReliefComponent?> body)
    {
        if (!Resolve(body, ref body.Comp, false) || body.Comp.ReversalEnds > _timing.CurTime)
            return 0f;

        var target = 0f;
        foreach (var dose in body.Comp.Doses.Values)
            target += dose.SedationTarget;

        return Math.Clamp(target, 0f, 1f);
    }

    /// <summary>
    /// M2: the sedation a reagent aims for per unit in the blood, from its <see cref="WolfmedPainRelief"/> effect.
    /// 0 for anything that does not sedate. The autodoc doses against it.
    /// </summary>
    public float SedationPerUnit(string reagent)
    {
        if (!_prototypes.TryIndex<ReagentPrototype>(reagent, out var prototype) || prototype.Metabolisms == null)
            return 0f;

        var perUnit = 0f;
        foreach (var entry in prototype.Metabolisms.Values)
        {
            foreach (var effect in entry.Effects)
            {
                if (effect is WolfmedPainRelief relief)
                    perUnit = MathF.Max(perUnit, relief.SedationPerUnit);
            }
        }

        return perUnit;
    }

    /// <summary>
    /// M2 (OD14): an antagonist takes <paramref name="amount"/> off the sedation at once and holds the target at zero
    /// for <paramref name="duration"/>, so the painkiller still in the blood cannot pull it back up meanwhile.
    /// </summary>
    public void ReverseSedation(EntityUid body, float amount, TimeSpan duration)
    {
        if (!_net.IsServer || !TryComp(body, out WolfmedPainReliefComponent? comp))
            return;

        var ends = _timing.CurTime + duration;
        if (comp.ReversalEnds == null || comp.ReversalEnds < ends)
            comp.ReversalEnds = ends;

        comp.Sedation = Math.Clamp(comp.Sedation - MathF.Max(0f, amount), 0f, 1f);
        _consciousness.SetExternalPressure(body, SedationPressure, GetRespiratoryDepression((body, comp)));
        CheckWarnings((body, comp));
        _movement.RefreshMovementSpeedModifiers(body);
        Dirty(body, comp);
    }

    /// <summary>
    /// Adds or refreshes one reagent's dose. Doses are keyed by reagent, so two painkillers stack while one
    /// painkiller metabolising every tick does not. M2: <paramref name="sedationTarget"/> is the sedation this dose
    /// asks for right now (its units in the blood times the reagent's sedation per unit).
    /// </summary>
    public void AddDose(EntityUid body, string key, WolfmedPainReliefTier tier, float strength,
        TimeSpan duration, float sedationTarget)
    {
        if (!_net.IsServer || tier == WolfmedPainReliefTier.None || duration <= TimeSpan.Zero)
            return;

        var comp = EnsureComp<WolfmedPainReliefComponent>(body);
        if (tier == WolfmedPainReliefTier.Emergency)
        {
            // One window per pen, and never during the crash the last one left behind.
            if (comp.EmergencyEnds > _timing.CurTime || comp.CrashEnds > _timing.CurTime)
                return;

            comp.EmergencyEnds = _timing.CurTime + duration;
        }

        comp.Doses[key] = new WolfmedPainReliefDose(tier, strength, MathF.Max(0f, sedationTarget),
            _timing.CurTime + duration);
        Recalculate((body, comp));
        _consciousness.Refresh(body);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (!_net.IsServer)
            return;

        _accumulator += frameTime;
        if (_accumulator < 1f)
            return;

        var elapsed = _accumulator;
        _accumulator = 0f;

        _finished.Clear();
        var query = EntityQueryEnumerator<WolfmedPainReliefComponent>();
        while (query.MoveNext(out var uid, out var comp))
            Tick((uid, comp), elapsed);

        foreach (var uid in _finished)
            RemComp<WolfmedPainReliefComponent>(uid);
    }

    private void Tick(Entity<WolfmedPainReliefComponent> body, float elapsed)
    {
        var now = _timing.CurTime;
        var changed = false;
        foreach (var (key, dose) in body.Comp.Doses.ToArray())
        {
            if (dose.Ends > now)
                continue;

            body.Comp.Doses.Remove(key);
            changed = true;
        }

        if (body.Comp.EmergencyEnds is { } emergency && now >= emergency)
        {
            body.Comp.EmergencyEnds = null;
            body.Comp.CrashEnds = now + body.Comp.CrashDuration;
            Crash(body);
            changed = true;
        }

        if (body.Comp.CrashEnds is { } crash && now >= crash)
        {
            body.Comp.CrashEnds = null;
            changed = true;
        }

        if (body.Comp.ReversalEnds is { } reversal && now >= reversal)
            body.Comp.ReversalEnds = null;

        if (UpdateSedation(body, elapsed) || changed)
            Recalculate(body);

        if (changed)
            _consciousness.Refresh(body);

        if (body.Comp.Doses.Count == 0 && body.Comp.Sedation <= 0f &&
            body.Comp.EmergencyEnds == null && body.Comp.CrashEnds == null && body.Comp.ReversalEnds == null)
            _finished.Add(body);
    }

    /// <summary>
    /// M2 (plan §3.4): sedation moves toward its target, rising at <c>wolfmed.sedation_rise</c> and falling at
    /// <see cref="WolfmedPainReliefComponent.SedationDecayPerSecond"/>, so a steady dose levels off. Past the
    /// threshold it depresses breathing, which is the lethal end of an overdose.
    /// </summary>
    private bool UpdateSedation(Entity<WolfmedPainReliefComponent> body, float elapsed)
    {
        var target = GetSedationTarget(body.AsNullable());
        var old = body.Comp.Sedation;
        var rise = MathF.Max(0f, _cfg.GetCVar(WolfmedCVars.SedationRise));
        body.Comp.Sedation = Math.Clamp(old < target
            ? MathF.Min(target, old + rise * elapsed)
            : MathF.Max(target, old - body.Comp.SedationDecayPerSecond * elapsed), 0f, 1f);

        // BRAIN: respiratory depression is a pressure and an oxygenation input, never damage. The pressure
        // is what puts the patient out; GetRespiratoryDepression is what starves the brain.
        _consciousness.SetExternalPressure(body.Owner, SedationPressure, GetRespiratoryDepression(body.AsNullable()));
        CheckWarnings(body);

        if (Math.Abs(old - body.Comp.Sedation) < 0.0005f)
            return false;

        _movement.RefreshMovementSpeedModifiers(body);
        Dirty(body);
        return true;
    }

    /// <summary>
    /// M2 (plan §3.4): one warning each time sedation climbs past <c>wolfmed.sedation_warn</c>, the breathing line
    /// and <c>wolfmed.sedation_warn_heavy</c>. A line re-arms once sedation falls back under it.
    /// </summary>
    private void CheckWarnings(Entity<WolfmedPainReliefComponent> body)
    {
        if (!_net.IsServer)
            return;

        var level = SedationWarningLevel(body.Comp.Sedation, body.Comp.SedationAirlossThreshold);
        if (level < body.Comp.WarnedLevel)
            body.Comp.WarnedLevel = level;

        while (body.Comp.WarnedLevel < level)
        {
            body.Comp.WarnedLevel++;
            var ev = new WolfmedSedationWarningEvent(body, body.Comp.WarnedLevel);
            RaiseLocalEvent(ref ev);
        }
    }

    /// <summary>How many of the three warning lines a sedation is at or past, in order: drowsy, breathing, barely awake.</summary>
    public int SedationWarningLevel(float sedation, float breathingLine)
    {
        if (sedation < _cfg.GetCVar(WolfmedCVars.SedationWarn))
            return 0;

        if (sedation < breathingLine)
            return 1;

        return sedation < _cfg.GetCVar(WolfmedCVars.SedationWarnHeavy) ? 2 : 3;
    }

    /// <summary>The bill for an emergency pen: every part's pain goes up by a data multiplier.</summary>
    private void Crash(Entity<WolfmedPainReliefComponent> body)
    {
        if (body.Comp.CrashPainMultiplier <= 1f)
            return;

        foreach (var (part, _) in _body.GetBodyChildren(body.Owner))
        {
            if (!TryComp(part, out PainComponent? pain) || pain.Value <= FixedPoint2.Zero)
                continue;

            _pain.SetPain((part, pain), pain.Value * body.Comp.CrashPainMultiplier);
        }
    }

    private void Recalculate(Entity<WolfmedPainReliefComponent> body)
    {
        var oldTier = body.Comp.Tier;
        var tier = WolfmedPainReliefTier.None;
        var relief = 0f;
        var strong = 0f;
        var bestRelief = 0f;
        var bestStrong = 0f;
        TimeSpan? ends = null;

        foreach (var dose in body.Comp.Doses.Values)
        {
            if (dose.Tier > tier)
                tier = dose.Tier;

            relief += dose.Strength;
            bestRelief = MathF.Max(bestRelief, dose.Strength);
            if (dose.Tier >= WolfmedPainReliefTier.Strong)
            {
                strong += dose.Strength;
                bestStrong = MathF.Max(bestStrong, dose.Strength);
            }

            if (ends == null || dose.Ends > ends)
                ends = dose.Ends;
        }

        // Diminishing returns: the strongest dose counts whole, everything else only for its share. Chugging
        // one of each painkiller must not discount enough pain to stay on your feet through anything.
        relief = Cap(bestRelief, relief, body.Comp.StackShare);
        strong = Cap(bestStrong, strong, body.Comp.StackShare);

        if (body.Comp.EmergencyEnds is { } emergency)
        {
            tier = WolfmedPainReliefTier.Emergency;
            if (ends == null || emergency > ends)
                ends = emergency;
        }

        body.Comp.Tier = tier;
        body.Comp.Relief = relief;
        body.Comp.StrongRelief = strong;
        body.Comp.Ends = ends;
        Dirty(body);
        _movement.RefreshMovementSpeedModifiers(body);

        // Playtest 1: the patient is told when a painkiller takes hold and when it wears off.
        if (tier != oldTier && _net.IsServer)
        {
            var ev = new WolfmedPainReliefTierChangedEvent(body, oldTier, tier);
            RaiseLocalEvent(ref ev);
        }
    }

    /// <summary>The strongest single dose, plus <paramref name="share"/> of everything else.</summary>
    private static float Cap(float best, float total, float share) =>
        best + MathF.Max(total - best, 0f) * Math.Clamp(share, 0f, 1f);

    private void OnRefreshSpeed(EntityUid uid, WolfmedPainReliefComponent component,
        RefreshMovementSpeedModifiersEvent args)
    {
        if (component.Sedation <= 0f)
            return;

        args.ModifySpeed(1f - (1f - component.SedationSlowdown) * component.Sedation);
    }

    private void OnRejuvenate(EntityUid uid, WolfmedPainReliefComponent component, RejuvenateEvent args)
    {
        if (_net.IsServer)
            RemComp<WolfmedPainReliefComponent>(uid);
    }
}

/// <summary>M2: broadcast when sedation climbs past a warning line (1 drowsy, 2 breathing slows, 3 barely awake). Server only.</summary>
[ByRefEvent]
public readonly record struct WolfmedSedationWarningEvent(EntityUid Body, int Level);

/// <summary>Broadcast when a body's strongest active painkiller tier changes. Server only.</summary>
[ByRefEvent]
public readonly record struct WolfmedPainReliefTierChangedEvent(
    EntityUid Body,
    WolfmedPainReliefTier Old,
    WolfmedPainReliefTier New);
