using System.Linq;
using Content.Server.Body.Components;
using Content.Server._WF.Wolfmed.Consciousness;
using Content.Server.Body.Systems;
using Content.Shared.Chemistry.EntitySystems;
using Content.Server.Temperature.Components;
using Content.Server._WF.Wolfmed.Wounds;
using Content.Shared._Onyx.Body.Systems;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Body.Organ;
using Content.Shared._WF.Wolfmed.Body;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.Consciousness;
using Content.Shared._WF.Wolfmed.Life;
using Content.Shared._WF.Wolfmed.Reagents;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Body.Components;
using Content.Shared.Body.Events;
using Content.Shared.Body.Part;
using Content.Shared.Body.Prototypes;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Electrocution;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.Wolfmed.Life;

/// <summary>Heartbeat, brain oxygenation and death on a wound host, with no damage totals involved.</summary>
/// <remarks>
/// Brain-missing is event driven on purpose. A poll for "this body has no brain" kills every brainless
/// fixture the test suite spawns, which is what the interim system did.
/// </remarks>
// The heart either beats or it does not, and the brain either has oxygen or is running out of it. Death is the brain
// organ being destroyed, which is ordinary MobState.Dead with nothing permanent attached.
public sealed class WolfmedLifeSystem : EntitySystem
{
    /// <summary>Pressure key for a stopped heart. Full, so an arrested body is always unconscious.</summary>
    public const string ArrestPressure = "arrest";

    /// <summary>Pressure key for a brain running out of oxygen while the heart still beats.</summary>
    public const string HypoxiaPressure = "hypoxia";

    /// <summary>
    /// M3: pressure key for a brain (organic, cause Brain) or positronic core (machine, cause Core) under its
    /// Downed line (plan §3.6). Downs, never knocks out.
    /// </summary>
    public const string InjuryPressure = "injury";

    /// <summary>Blood still moving under a rescuer's hands, as a fraction, while CPR is in progress.</summary>
    private const float CprCirculation = 0.5f;

    /// <summary>Passive bleeding multiplier with no pulse behind it. Read by the Onyx bleeding system.</summary>
    public const float ArrestBleedFactor = 0.25f;

    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(1);

    [Dependency] private BloodstreamSystem _bloodstream = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private OrganHealthSystem _organs = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private WolfmedBreathingSystem _breathing = default!;
    [Dependency] private WolfmedConcussionSystem _concussion = default!;
    [Dependency] private WolfmedConsciousnessSystem _consciousness = default!;
    [Dependency] private WolfmedDyingActionsSystem _dyingActions = default!;
    [Dependency] private WolfmedInfectionSystem _infection = default!;
    [Dependency] private WolfmedPainReliefSystem _relief = default!;
    [Dependency] private WolfmedShutdownSystem _shutdown = default!;
    [Dependency] private WoundBleedingSystem _bleeding = default!;
    [Dependency] private WoundSystem _wounds = default!;
    [Dependency] private Wounds.WolfmedFluidLossSystem _fluidLoss = default!; // M1b
    [Dependency] private IComponentFactory _factory = default!; // M4
    [Dependency] private IPrototypeManager _prototypes = default!; // M4
    [Dependency] private WolfmedOverheatSystem _overheat = default!; // M4

    [Dependency] private WolfmedToxinSystem _toxin = default!; // M5
    [Dependency] private WolfmedRadiationSystem _radiation = default!;
    [Dependency] private WolfmedBodyTemperatureSystem _temperature = default!;
    [Dependency] private Content.Shared.Popups.SharedPopupSystem _popup = default!; // Playtest 5

    private readonly List<EntityUid> _due = new();
    private TimeSpan _nextTick;

    public override void Initialize()
    {
        base.Initialize();

        // The organ events are keyed on WolfmedOrganComponent: BrainComponent and HeartComponent already
        // have their pairs taken by BrainSystem and HeartSystem, and one health component covers both.
        SubscribeLocalEvent<WolfmedOrganComponent, OrganRemovedFromBodyEvent>(OnOrganRemoved);
        SubscribeLocalEvent<WolfmedOrganComponent, OrganAddedToBodyEvent>(OnOrganAdded);
        SubscribeLocalEvent<WolfmedPartAmputatedEvent>(OnAmputated);
        SubscribeLocalEvent<WolfmedRejuvenateEvent>(OnRejuvenate);
        SubscribeLocalEvent<WolfmedPainShockEvent>(OnPainShock);
        SubscribeLocalEvent<WolfmedConcussionSourcesEvent>(OnConcussionSources);
        SubscribeLocalEvent<ElectrocutedEvent>(OnElectrocuted);
    }

    #region Queries

    /// <summary>True while this package, rather than the damage thresholds, decides this body's death.</summary>
    public bool OwnsDeath(EntityUid body) => _consciousness.OwnsMobState(body);

    public bool InArrest(EntityUid body) => HasComp<WolfmedCardiacArrestComponent>(body);

    /// <summary>The brain organ's oxygenation clock, or null for a body that has no brain to run one.</summary>
    public Entity<WolfmedBrainComponent>? GetBrain(EntityUid body)
    {
        foreach (var (organ, _) in _body.GetBodyOrgans(body))
        {
            if (TryComp(organ, out WolfmedBrainComponent? brain))
                return (organ, brain);
        }

        return null;
    }

    /// <summary>The brain organ's health, whether or not it still runs a clock.</summary>
    public Entity<WolfmedOrganComponent>? GetBrainOrgan(EntityUid body)
    {
        foreach (var (organ, _) in _body.GetBodyOrgans(body))
        {
            if (HasComp<BrainComponent>(organ) && TryComp(organ, out WolfmedOrganComponent? health))
                return (organ, health);
        }

        return null;
    }

    /// <summary>
    /// M4 (OD16): the species is built without a heart: no slot of its body prototype holds one. Its arrest is
    /// circulatory collapse. A body that has lost its heart is not heartless; its arrest is cause "heart".
    /// </summary>
    public bool IsHeartless(EntityUid body)
    {
        if (!TryComp(body, out BodyComponent? comp) || comp.Prototype is not { } id ||
            !_prototypes.TryIndex<BodyPrototype>(id, out var prototype))
            return false;

        var heart = _factory.GetComponentName(typeof(HeartComponent));
        foreach (var slot in prototype.Slots.Values)
        {
            foreach (var organId in slot.Organs.Values)
            {
                if (_prototypes.TryIndex<EntityPrototype>(organId, out var organ) && organ.Components.ContainsKey(heart))
                    return false;
            }
        }

        return true;
    }

    /// <summary>Whether the body has a brain organ in it.</summary>
    public bool HasBrain(EntityUid body)
    {
        foreach (var (organ, _) in _body.GetBodyOrgans(body))
        {
            if (HasComp<BrainComponent>(organ))
                return true;
        }

        return false;
    }

    /// <summary>The heart's health, or null when there is no heart in the body at all.</summary>
    public FixedPoint2? GetHeartHealth(EntityUid body)
    {
        foreach (var (organ, _) in _body.GetBodyOrgans(body))
        {
            if (HasComp<HeartComponent>(organ))
                return CompOrNull<WolfmedOrganComponent>(organ)?.Health ?? FixedPoint2.New(1);
        }

        return null;
    }

    public float GetOxygenation(EntityUid body) => GetBrain(body)?.Comp.Oxygenation ?? 1f;

    /// <summary>Oxygenation a successful shock or a brain repair leaves at the least (M1a, plan §7.1).</summary>
    public float PostShockOxygenation => Math.Clamp(_cfg.GetCVar(WolfmedCVars.PostShockOxygenation), 0f, 1f);

    /// <summary>A successful shock is holding off the blood and oxygen arrest triggers right now.</summary>
    public bool InPostShockGrace(EntityUid body) => GetPostShockGraceSeconds(body) > 0f;

    /// <summary>Seconds left on the post-shock grace, or 0 when there is none.</summary>
    public float GetPostShockGraceSeconds(EntityUid body) =>
        TryComp(body, out WolfmedPostShockComponent? post) ? MathF.Max(0f, post.GraceSeconds) : 0f;

    /// <summary>
    /// The time a successful shock restores anything again: another success inside it only restarts the
    /// heart (<see cref="WolfmedCVars.PostShockRepeatSeconds"/>).
    /// </summary>
    public float PostShockRepeatSeconds => MathF.Max(0f, _cfg.GetCVar(WolfmedCVars.PostShockRepeatSeconds));

    /// <summary>
    /// The brain is gone: either the organ is destroyed or the body has died some other way. What separates
    /// this from cardiac arrest is that no shock will do anything about it until the brain is rebuilt.
    /// </summary>
    public bool IsBrainDead(EntityUid body)
    {
        if (TerminatingOrDeleted(body))
            return false;

        if (_mobState.IsDead(body))
            return true;

        return GetBrainOrgan(body) is { } organ && organ.Comp.Health <= FixedPoint2.Zero;
    }

    /// <summary>Brain activity, 0 to 1: the brain organ's health as a share of its maximum.</summary>
    public float GetBrainActivity(EntityUid body)
    {
        if (GetBrainOrgan(body) is not { } organ || organ.Comp.MaxHealth <= FixedPoint2.Zero)
            return 1f;

        return Math.Clamp(organ.Comp.Health.Float() / organ.Comp.MaxHealth.Float(), 0f, 1f);
    }

    /// <summary>Passive bleeding multiplier. A stopped heart is not pushing anything out of a wound.</summary>
    public float BleedFactor(EntityUid body) => InArrest(body) ? ArrestBleedFactor : 1f;

    // The solution check comes first: a body being torn down (a spawn-all test deletes its blood before its heart)
    // still raises organ events, and the bloodstream asserts on a cached solution that is gone.
    public float GetBlood(EntityUid body) =>
        TryComp(body, out BloodstreamComponent? blood) && _solutions.TryGetSolution(body, blood.BloodSolutionName, out _)
            ? _bloodstream.GetBloodLevelPercentage(body, blood)
            : 1f;

    /// <summary>Somebody's hands are on the chest right now.</summary>
    public bool InCpr(EntityUid body) =>
        TryComp(body, out WolfmedCprComponent? cpr) && cpr.Ends > _timing.CurTime;

    #endregion

    #region Tick

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_timing.CurTime < _nextTick)
            return;

        _nextTick = _timing.CurTime + TickInterval;

        // Buffered: a tick can destroy an organ, which removes entities while the query is open.
        _due.Clear();
        var query = EntityQueryEnumerator<WolfmedConsciousnessComponent>();
        while (query.MoveNext(out var uid, out _))
            _due.Add(uid);

        foreach (var body in _due)
        {
            if (!TerminatingOrDeleted(body))
                Tick(body, (float) TickInterval.TotalSeconds);
        }
    }

    /// <summary>
    /// One body's share of a tick. Public so a test can fast-forward the clock instead of waiting out the
    /// real seconds; the tick is the only thing that moves oxygenation.
    /// </summary>
    public void Tick(EntityUid body, float seconds)
    {
        if (seconds <= 0f || TerminatingOrDeleted(body) || !OwnsDeath(body))
            return;

        if (TryComp(body, out WolfmedCprComponent? cpr) && cpr.Ends <= _timing.CurTime)
            RemComp<WolfmedCprComponent>(body);

        AdvancePostShock(body, seconds);
        ExpireArrestMemory(body); // M2

        // M5 (plan §3.8-3.9): the liver clears Poison and a failing marrow costs blood, brain or no brain.
        _toxin.Clear(body, seconds);
        _radiation.Tick(body, seconds);

        if (GetBrain(body) is not { } brain)
        {
            // No clock to run. Mechanical bodies live here, and so does every brainless test fixture.
            // The heart-restored exit still runs: it is the only way out of arrest that needs no clock, and
            // without it a chassis with its pump back would stay arrested until a defibrillator found it.
            TryEndHeartArrest(body);
            _consciousness.SetExternalPressure(body, HypoxiaPressure, 0f);
            // M3: a chassis's positronic core under its line holds it Downed, cause Core (plan §3.6).
            UpdateInjury(body, WolfmedCVars.ConsciousnessCoreDown);
            UpdateVitalSigns(body);
            UpdateCoreRestored(body); // M2
            return;
        }

        var dead = _mobState.IsDead(body);
        UpdateOxygenation(body, brain, seconds);

        if (!dead)
        {
            UpdateBrainDamage(body, brain, seconds);
            UpdateArrest(body, brain, seconds);
        }

        UpdateTrauma(body);
        UpdatePressures(body, brain, dead);
        UpdateVitalSigns(body);
    }

    /// <summary>
    /// Runs the post-shock clocks on the life tick. The record goes, and the next shock is a fresh episode,
    /// once the grace is spent and either the repeat window has passed or the patient has recovered.
    /// </summary>
    private void AdvancePostShock(EntityUid body, float seconds)
    {
        if (!TryComp(body, out WolfmedPostShockComponent? post))
            return;

        post.GraceSeconds = MathF.Max(0f, post.GraceSeconds - seconds);
        post.SinceRestore += seconds;
        if (post.GraceSeconds <= 0f && (post.SinceRestore >= PostShockRepeatSeconds || RecoveredFromArrest(body)))
            RemComp<WolfmedPostShockComponent>(body);
    }

    /// <summary>M2: the "After a restart" line lasts <c>wolfmed.arrest_cause_memory_seconds</c>.</summary>
    private void ExpireArrestMemory(EntityUid body)
    {
        if (TryComp(body, out WolfmedArrestMemoryComponent? memory) &&
            _timing.CurTime - memory.RestartedAt >= TimeSpan.FromSeconds(MathF.Max(0f, _cfg.GetCVar(WolfmedCVars.ArrestCauseMemorySeconds))))
            RemComp<WolfmedArrestMemoryComponent>(body);
    }

    /// <summary>
    /// M2 (plan §5.5): why the heart last stopped, as the arrest cause's sub-source, and whether that cause is still
    /// there: blood still under the line where it starves the brain, still suffocating, overdosed or on damaged lungs,
    /// the heart still failed, sepsis still past its line. Null outside the memory window, while arrested again, or dead.
    /// </summary>
    public (WolfmedCauseSource Cause, bool Present)? GetRestartMemory(EntityUid body)
    {
        if (!TryComp(body, out WolfmedArrestMemoryComponent? memory) || InArrest(body) || _mobState.IsDead(body) ||
            _timing.CurTime - memory.RestartedAt >= TimeSpan.FromSeconds(MathF.Max(0f, _cfg.GetCVar(WolfmedCVars.ArrestCauseMemorySeconds))))
            return null;

        var present = memory.Cause switch
        {
            "blood" => HasComp<BloodstreamComponent>(body) && GetBlood(body) < _cfg.GetCVar(WolfmedCVars.BrainBloodStart),
            // Every breath input DrainRate folds together, M3's damaged lungs included.
            "oxygen" => BreathingLevel(body) > 0f || _relief.GetRespiratoryDepression(body) > 0f || LungDamageLevel(body) > 0f,
            "heart" => GetHeartHealth(body) is not { } heart || heart <= FixedPoint2.Zero,
            "sepsis" => _infection.GetSepsis(body) >= _cfg.GetCVar(WolfmedCVars.ArrestSepsis),
            "cold" => _temperature.IsColdArrest(body), // M5
            "toxin" => _toxin.InComa(body),
            "heat" => _temperature.InHeatStroke(body),
            _ => false,
        };

        return (ArrestSource(memory.Cause), present);
    }

    /// <summary>An arrest cause string as the sub-source the texts name.</summary>
    public static WolfmedCauseSource ArrestSource(string cause) => cause switch
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
    };

    /// <summary>
    /// The arrest episode is over: the patient is standing, the heart is going and the blood is back above the
    /// line where it drains the brain. A later arrest is a new emergency and earns its own restore and grace.
    /// </summary>
    private bool RecoveredFromArrest(EntityUid body) =>
        !InArrest(body) && !_mobState.IsDead(body) &&
        TryComp(body, out WolfmedConsciousnessComponent? consciousness) &&
        consciousness.State == WolfmedConsciousness.Up &&
        GetTransfusionGuidance(body).ToBrainSafe <= 0f;

    /// <summary>The worst of the four inputs drains; nothing draining at all refills, slowly.</summary>
    private void UpdateOxygenation(EntityUid body, Entity<WolfmedBrainComponent> brain, float seconds)
    {
        var rate = DrainRate(body, brain);

        // AUTODOC5: chest compressions circulate. On a corpse there is nothing else moving blood at all, so
        // CPR is the only thing that can raise the number a defibrillator's odds used to be read off. An
        // arrested body still on the clock is untouched: CPR buys it time, it does not undo the arrest.
        if (_mobState.IsDead(body) && InCpr(body))
        {
            SetOxygenation(brain, brain.Comp.Oxygenation + CprDeadRefill * seconds);
            return;
        }

        // Playtest 5: compressions on a living arrested body hold the brain at wolfmed.brain_cpr_floor, the damage
        // line: the drain stops there, and a brain already under it comes back up at the corpse rate. A rescuer who
        // keeps going keeps the brain alive for as long as they keep going.
        var floor = _cfg.GetCVar(WolfmedCVars.BrainCprFloor);
        if (floor > 0f && InCpr(body) && InArrest(body))
        {
            var held = brain.Comp.Oxygenation < floor
                ? MathF.Min(floor, brain.Comp.Oxygenation + CprDeadRefill * seconds)
                : MathF.Max(floor, brain.Comp.Oxygenation - rate * seconds);
            SetOxygenation(brain, held);
            return;
        }

        var value = rate > 0f
            ? brain.Comp.Oxygenation - rate * seconds
            : brain.Comp.Oxygenation + RefillRate() * seconds;

        SetOxygenation(brain, value);
    }

    /// <summary>Oxygenation a minute of CPR puts back into a corpse's brain, per second.</summary>
    private const float CprDeadRefill = 1f / 90f;

    private float RefillRate()
    {
        var arrestSeconds = _cfg.GetCVar(WolfmedCVars.BrainArrestSeconds);
        return arrestSeconds <= 0f ? 0f : _cfg.GetCVar(WolfmedCVars.BrainRefillFactor) / arrestSeconds;
    }

    /// <summary>Oxygenation lost per second right now, with every multiplier already applied.</summary>
    public float DrainRate(EntityUid body, Entity<WolfmedBrainComponent> brain) => DrainRate(body, brain, out _);

    /// <summary>
    /// <see cref="DrainRate(EntityUid, Entity{WolfmedBrainComponent})"/>, and which drain is the largest: the
    /// hypoxia cause's sub-source (M1a, plan §5.1).
    /// </summary>
    public float DrainRate(EntityUid body, Entity<WolfmedBrainComponent> brain, out WolfmedCauseSource source)
    {
        var cpr = InCpr(body);
        var worst = 0f;
        source = WolfmedCauseSource.None;

        if (InArrest(body))
            worst = MathF.Max(worst, Per(_cfg.GetCVar(WolfmedCVars.BrainArrestSeconds)));

        // CPR is rescue breaths as well as compressions, so it answers for the airway while it lasts.
        if (!cpr)
        {
            var suffocation = BreathingLevel(body);
            var depression = _relief.GetRespiratoryDepression(body);
            var lungs = LungDamageLevel(body); // M3: damaged lungs (plan §3.3)
            var breath = Math.Clamp(MathF.Max(MathF.Max(suffocation, depression), lungs), 0f, 1f);
            var rate = breath * Per(_cfg.GetCVar(WolfmedCVars.BrainAirlossSeconds));
            if (rate > worst)
            {
                worst = rate;
                source = lungs > suffocation && lungs >= depression ? WolfmedCauseSource.Lungs
                    : depression > suffocation ? WolfmedCauseSource.Sedation
                    : _breathing.Assess(body).Source == WolfmedBreathingSource.Lungs ? WolfmedCauseSource.Lungs
                    : WolfmedCauseSource.Airway;
            }
        }

        var start = _cfg.GetCVar(WolfmedCVars.BrainBloodStart);
        var full = _cfg.GetCVar(WolfmedCVars.BrainBloodFull);
        var blood = GetBlood(body);
        if (cpr && blood > full)
            blood = MathF.Max(blood, CprCirculation);

        if (blood < start && start > full)
        {
            var level = Math.Clamp((start - blood) / (start - full), 0f, 1f);
            var rate = level * Per(_cfg.GetCVar(WolfmedCVars.BrainBloodSeconds));
            if (rate > worst)
            {
                worst = rate;
                source = WolfmedCauseSource.Circulation;
            }
        }

        if (_infection.GetSepsis(body) >= _cfg.GetCVar(WolfmedCVars.ArrestSepsis))
        {
            var rate = Per(_cfg.GetCVar(WolfmedCVars.BrainSepsisSeconds));
            if (rate > worst)
            {
                worst = rate;
                source = WolfmedCauseSource.Sepsis;
            }
        }

        // M5 (plan §3.8, §3.10): a toxic coma and heat stroke drain the brain on clocks of their own.
        var toxin = _toxin.DrainRate(body);
        if (toxin > worst)
        {
            worst = toxin;
            source = WolfmedCauseSource.Toxin;
        }

        var heat = _temperature.DrainRate(body);
        if (heat > worst)
        {
            worst = heat;
            source = WolfmedCauseSource.Heat;
        }

        if (worst <= 0f)
            return 0f;

        worst *= ColdFactor(brain, body);

        if (cpr)
            worst *= _cfg.GetCVar(WolfmedCVars.BrainCprFactor);

        if (_relief.GetTier(body) == WolfmedPainReliefTier.Stimulant)
            worst *= _cfg.GetCVar(WolfmedCVars.BrainStimulantFactor);

        return worst;
    }

    private static float Per(float seconds) => seconds > 0f ? 1f / seconds : 0f;

    /// <summary>A cold brain keeps for longer. The curve is the organ's own data; the CVar scales it.</summary>
    private float ColdFactor(Entity<WolfmedBrainComponent> brain, EntityUid body)
    {
        if (!TryComp(body, out TemperatureComponent? temperature))
            return 1f;

        var factor = 1f;
        foreach (var step in brain.Comp.ColdSteps)
        {
            if (temperature.CurrentTemperature < step.Below)
            {
                factor = step.Factor;
                break;
            }
        }

        return Math.Clamp(factor * _cfg.GetCVar(WolfmedCVars.BrainColdFactor), 0f, 1f);
    }

    /// <summary>
    /// Not breathing, 0 to 1 (plan §4.3): real suffocation only. Leftover Asphyxiation on a body that is
    /// breathing again, and Bloodloss at any time, never count; blood reaches the brain through the blood
    /// input alone.
    /// </summary>
    public float BreathingLevel(EntityUid body) => _breathing.SuffocationLevel(body);

    /// <summary>Below the threshold the organ itself starts dying, and organ damage is not reversible.</summary>
    private void UpdateBrainDamage(EntityUid body, Entity<WolfmedBrainComponent> brain, float seconds)
    {
        if (!TryComp(brain, out WolfmedOrganComponent? organ) || organ.Health <= FixedPoint2.Zero)
            return;

        var threshold = _cfg.GetCVar(WolfmedCVars.BrainDamageOxygenation);
        if (threshold > 0f && brain.Comp.Oxygenation < threshold)
        {
            // M2 (P24): a cold brain loses tissue as slowly as it loses oxygen, on the same curve.
            var rate = _cfg.GetCVar(WolfmedCVars.BrainDamageRate) *
                       Math.Clamp((threshold - brain.Comp.Oxygenation) / threshold, 0f, 1f) *
                       ColdFactor(brain, body);
            if (rate > 0f)
                _organs.ChangeHealth((brain.Owner, organ), FixedPoint2.New(-rate * seconds));
        }

        RefreshConcussion(body, brain, organ);
    }

    /// <summary>A damaged brain blurs, slurs and, far enough gone, drops things. W3's effects, no new system.</summary>
    private void RefreshConcussion(EntityUid body, Entity<WolfmedBrainComponent> brain, WolfmedOrganComponent organ)
    {
        var share = organ.MaxHealth > FixedPoint2.Zero
            ? organ.Health.Float() / organ.MaxHealth.Float()
            : 1f;

        var concussed = share < brain.Comp.ConcussionAt;
        if (concussed == brain.Comp.Concussed)
            return;

        brain.Comp.Concussed = concussed;
        _concussion.Refresh(body);
    }

    /// <summary>M2 (OD10): the repaired core's "CORE RESTORED" line goes when the time a brain's trauma would last is up.</summary>
    private void UpdateCoreRestored(EntityUid body)
    {
        if (TryComp(body, out WolfmedCoreRestoredComponent? restored) && restored.Ends <= _timing.CurTime)
            RemComp<WolfmedCoreRestoredComponent>(body);
    }

    /// <summary>The trauma a repaired brain carries, and the moment it wears off.</summary>
    private void UpdateTrauma(EntityUid body)
    {
        if (!TryComp(body, out WolfmedBrainTraumaComponent? trauma) || trauma.Ends > _timing.CurTime)
            return;

        RemComp<WolfmedBrainTraumaComponent>(body);
        _concussion.Refresh(body);
        if (!_mobState.IsDead(body))
            _popup.PopupEntity(Loc.GetString("wolfmed-brain-trauma-end"), body, body); // Playtest 5
    }

    private void UpdateArrest(EntityUid body, Entity<WolfmedBrainComponent> brain, float seconds)
    {
        var heart = GetHeartHealth(body);
        var blood = GetBlood(body);

        if (InArrest(body))
        {
            TryEndHeartArrest(body);
            return;
        }

        // M1a: a successful shock buys the medic its grace. The drains still run and still show; only the
        // blood and oxygen triggers wait. A heart that is itself destroyed stops whatever the grace says.
        var grace = InPostShockGrace(body);

        // A heart that has stopped working, not a body that never had one: a species with no heart in its
        // prototype at all is not in arrest, and a heart pulled out arrests on its own removal event.
        if (heart is { } beating && beating <= FixedPoint2.Zero)
        {
            StartArrest(body, "heart");
            return;
        }

        if (!grace && blood <= _cfg.GetCVar(WolfmedCVars.ArrestBlood))
        {
            StartArrest(body, "blood");
            return;
        }

        if (!grace && brain.Comp.Oxygenation <= _cfg.GetCVar(WolfmedCVars.ArrestOxygenation))
        {
            // M2 (OD15): late sepsis stops the heart through this trigger, on its own drain, and is named for it.
            // M5: so do a toxic coma and heat stroke.
            DrainRate(body, brain, out var drain);
            StartArrest(body, drain switch
            {
                WolfmedCauseSource.Sepsis => "sepsis",
                WolfmedCauseSource.Toxin => "toxin",
                WolfmedCauseSource.Heat => "heat",
                _ => "oxygen",
            });
            return;
        }

        // M5 (plan §3.10): a core under the cold arrest line stops the heart; the brain's cold protection applies.
        if (_temperature.IsColdArrest(body))
        {
            StartArrest(body, "cold");
            return;
        }

        if (_infection.GetSepsis(body) >= _cfg.GetCVar(WolfmedCVars.ArrestSepsis) &&
            _random.Prob(Math.Clamp(_cfg.GetCVar(WolfmedCVars.ArrestSepsisChance) * seconds, 0f, 1f)))
            StartArrest(body, "sepsis");
    }

    /// <summary>
    /// The one route out of arrest that is not a defibrillator: the heart is back and there is blood for it
    /// to move. Kept off the oxygenation clock so it also reaches a body that runs no clock at all.
    /// </summary>
    private bool TryEndHeartArrest(EntityUid body)
    {
        if (!TryComp(body, out WolfmedCardiacArrestComponent? arrest) || arrest.Cause != "heart" ||
            GetHeartHealth(body) is not { } health || health <= FixedPoint2.Zero ||
            GetBlood(body) <= _cfg.GetCVar(WolfmedCVars.ArrestBlood))
            return false;

        return EndArrest(body);
    }

    /// <summary>Arrest is a full pressure; hypoxia short of it ramps between the two CVars.</summary>
    private void UpdatePressures(EntityUid body, Entity<WolfmedBrainComponent> brain, bool dead)
    {
        _consciousness.SetExternalPressure(body, ArrestPressure, !dead && InArrest(body) ? 1f : 0f);

        var start = _cfg.GetCVar(WolfmedCVars.BrainPressureStart);
        var outAt = _cfg.GetCVar(WolfmedCVars.BrainPressureOut);
        var level = 0f;
        if (!dead && start > outAt && brain.Comp.Oxygenation < start)
            level = Math.Clamp((start - brain.Comp.Oxygenation) / (start - outAt), 0f, 1f);

        // M1a: the hypoxia cause names its largest drain; recorded before the pressure re-evaluates.
        if (TryComp(body, out WolfmedConsciousnessComponent? sourced))
        {
            DrainRate(body, brain, out var source);
            if (source != WolfmedCauseSource.None || level <= 0f)
                sourced.HypoxiaSource = source;
        }

        _consciousness.SetExternalPressure(body, HypoxiaPressure, level);
        UpdateInjury(body, WolfmedCVars.ConsciousnessBrainDown); // M3: brain injury (plan §3.6)

        if (TryComp(body, out WolfmedConsciousnessComponent? consciousness) &&
            MathF.Abs(consciousness.Oxygenation - brain.Comp.Oxygenation) >= 0.005f)
        {
            consciousness.Oxygenation = brain.Comp.Oxygenation;
            Dirty(body, consciousness);
        }
    }

    /// <summary>
    /// M3 (plan §3.6): a brain, or a chassis's positronic core, under its Downed line writes the injury pressure.
    /// It Downs and never knocks out, and nothing heals an organ on its own, so it holds until surgery.
    /// </summary>
    private void UpdateInjury(EntityUid body, CVarDef<float> line)
    {
        var level = 0f;
        if (!_mobState.IsDead(body) && GetBrainOrgan(body) is { } organ && organ.Comp.Health > FixedPoint2.Zero &&
            organ.Comp.Fraction < _cfg.GetCVar(line))
            level = Math.Clamp(_cfg.GetCVar(WolfmedCVars.InjuryDownPressure), 0f, 1f);

        _consciousness.SetExternalPressure(body, InjuryPressure, level);
    }

    /// <summary>M3 (plan §3.3): damaged lungs as a breathing input, 0 to 1. See <see cref="WolfmedBreathingSystem.LungDamageLevel"/>.</summary>
    public float LungDamageLevel(EntityUid body) => _breathing.LungDamageLevel(body);

    /// <summary>
    /// M3 (plan §8): blood regeneration multiplier from the organs, the heart's impaired band (× its
    /// <c>impairedRegenFactor</c>). The bloodstream's one marked line asks it every regeneration step.
    /// </summary>
    public float BloodRegenFactor(EntityUid body)
    {
        if (!OwnsDeath(body))
            return 1f;

        var factor = 1f;
        foreach (var (organ, _) in _body.GetBodyOrgans(body))
        {
            if (TryComp(organ, out WolfmedOrganComponent? health) && health.Band == WolfmedOrganBand.Impaired)
                factor *= Math.Clamp(health.ImpairedRegenFactor, 0f, 1f);
        }

        // M5 (plan §3.9): past wolfmed.rad_marrow_stop the marrow makes nothing.
        return factor * _radiation.RegenFactor(body);
    }

    /// <summary>
    /// The networked breathing and circulation words examine and the analyzer read (plan §4.5, §5.5). Only
    /// dirtied when one of them changes.
    /// </summary>
    public void UpdateVitalSigns(EntityUid body)
    {
        if (!TryComp(body, out WolfmedConsciousnessComponent? consciousness))
            return;

        var (breathing, source) = _breathing.Assess(body);
        var band = GetBloodBand(body);
        var irregular = band != WolfmedBloodBand.None && HeartImpaired(body); // M3
        if (consciousness.Breathing == breathing && consciousness.BreathingSource == source &&
            consciousness.BloodBand == band && consciousness.PulseIrregular == irregular)
            return;

        consciousness.Breathing = breathing;
        consciousness.BreathingSource = source;
        consciousness.BloodBand = band;
        consciousness.PulseIrregular = irregular;
        Dirty(body, consciousness);
    }

    /// <summary>M3 (plan §8): a heart under its impaired line, still beating.</summary>
    public bool HeartImpaired(EntityUid body)
    {
        foreach (var (organ, _) in _body.GetBodyOrgans(body))
        {
            if (HasComp<HeartComponent>(organ) && TryComp(organ, out WolfmedOrganComponent? health) &&
                health.Band == WolfmedOrganBand.Impaired)
                return true;
        }

        return false;
    }

    /// <summary>Blood volume in a medic's words. The lines are consciousness's own Downed and Unconscious ones.</summary>
    public WolfmedBloodBand GetBloodBand(EntityUid body)
    {
        if (_mobState.IsDead(body) || InArrest(body))
            return WolfmedBloodBand.None;

        if (!HasComp<BloodstreamComponent>(body))
            return WolfmedBloodBand.Normal;

        var blood = GetBlood(body);
        if (blood <= _cfg.GetCVar(WolfmedCVars.ConsciousnessBloodOut))
            return WolfmedBloodBand.Critical;

        if (blood <= _cfg.GetCVar(WolfmedCVars.ConsciousnessBloodDown))
            return WolfmedBloodBand.Weak;

        return blood <= _cfg.GetCVar(WolfmedCVars.BloodBandPale) ? WolfmedBloodBand.Low : WolfmedBloodBand.Normal;
    }

    /// <summary>
    /// Blood leaving the body right now, in units a second: the wounds' stream rate (the bloodstream removes
    /// it once per update interval) plus every open internal bleed.
    /// </summary>
    public float GetBleedRate(EntityUid body)
    {
        var rate = GetInternalBleedRate(body);
        if (TryComp(body, out BloodstreamComponent? bloodstream) && bloodstream.UpdateInterval > TimeSpan.Zero)
            rate += bloodstream.BleedAmount / (float) bloodstream.UpdateInterval.TotalSeconds;

        return rate;
    }

    /// <summary>Units a second lost to every open internal bleed. M2: its own route on the analyzer.</summary>
    public float GetInternalBleedRate(EntityUid body)
    {
        var rate = 0f;
        foreach (var (part, _) in _body.GetBodyChildren(body))
        {
            if (!TryComp(part, out WoundableComponent? woundable))
                continue;

            foreach (var wound in _wounds.GetWounds((part, woundable)))
            {
                if (wound.Comp.State == WoundState.Open &&
                    TryComp(wound, out WoundInternalBleedingComponent? internalBleeding) &&
                    internalBleeding.Severity > FixedPoint2.Zero)
                    rate += internalBleeding.Rate * internalBleeding.Severity.Float();
            }
        }

        return rate;
    }

    /// <summary>
    /// M2 (plan §5.5): every process making this body worse right now. Every drain on the brain counts, not only the
    /// largest one <see cref="DrainRate(EntityUid, Entity{WolfmedBrainComponent}, out WolfmedCauseSource)"/> names, and
    /// so do the blood routes. Nothing for the dead.
    /// </summary>
    public WolfmedRoutes GetActiveRoutes(EntityUid body)
    {
        if (TerminatingOrDeleted(body) || _mobState.IsDead(body))
            return WolfmedRoutes.None;

        var routes = WolfmedRoutes.None;
        if (TryComp(body, out BloodstreamComponent? bloodstream) && bloodstream.BleedAmount > 0f)
            routes |= WolfmedRoutes.Bleeding;

        if (GetInternalBleedRate(body) > 0f)
            routes |= WolfmedRoutes.InternalBleeding;

        if (_fluidLoss.GetRate(body) > 0f)
            routes |= WolfmedRoutes.BurnFluid;

        // M4 (plan §3.11): a machine's core past its heat line.
        if (_overheat.CoreCooking(body))
            routes |= WolfmedRoutes.CoreHeat;

        // M5 (plan §5.4): the failing marrow and a hypothermic core still cooling.
        if (_radiation.GetMarrowLossRate(body) > 0f)
            routes |= WolfmedRoutes.Marrow;

        if (_temperature.StillCooling(body))
            routes |= WolfmedRoutes.Hypothermia;

        // Playtest 4 (SEPSIS): sepsis eating the organs, brain or no brain.
        if (_infection.DamagingOrgans(body))
            routes |= WolfmedRoutes.SepsisOrgans;

        if (GetBrain(body) is not { } brain)
            return routes;

        if (_toxin.InComa(body))
            routes |= WolfmedRoutes.Toxin;

        if (_temperature.InHeatStroke(body))
            routes |= WolfmedRoutes.HeatStroke;

        if (InArrest(body))
            routes |= WolfmedRoutes.Arrest;

        if (!InCpr(body))
        {
            var suffocation = BreathingLevel(body);
            if (suffocation > 0f)
                routes |= _breathing.Assess(body).Source == WolfmedBreathingSource.Lungs
                    ? WolfmedRoutes.Lungs
                    : WolfmedRoutes.Airway;

            // M3: damaged lungs drain the brain with the airway clear, the same input DrainRate reads.
            if (LungDamageLevel(body) > 0f)
                routes |= WolfmedRoutes.Lungs;

            if (_relief.GetRespiratoryDepression(body) > 0f)
                routes |= WolfmedRoutes.Sedation;
        }

        var start = _cfg.GetCVar(WolfmedCVars.BrainBloodStart);
        if (HasComp<BloodstreamComponent>(body) && GetBlood(body) < start && start > _cfg.GetCVar(WolfmedCVars.BrainBloodFull))
            routes |= WolfmedRoutes.Circulation;

        if (_infection.GetSepsis(body) >= _cfg.GetCVar(WolfmedCVars.ArrestSepsis))
            routes |= WolfmedRoutes.Sepsis;

        if (brain.Comp.Oxygenation < _cfg.GetCVar(WolfmedCVars.BrainDamageOxygenation) &&
            TryComp(brain, out WolfmedOrganComponent? organ) && organ.Health > FixedPoint2.Zero)
            routes |= WolfmedRoutes.TissueLoss;

        return routes;
    }

    /// <summary>
    /// M1b: all blood volume leaving the body, in units a second: the bleeding plus the burns' fluid loss
    /// (<see cref="Wounds.WolfmedFluidLossSystem"/>), which weeps rather than bleeds but empties the same pool.
    /// </summary>
    public float GetVolumeLossRate(EntityUid body) =>
        GetBleedRate(body) + _fluidLoss.GetRate(body) + _radiation.GetMarrowLossRate(body); // M5: the marrow too

    /// <summary>
    /// The two transfusion numbers (plan §7.1): units to <see cref="WolfmedCVars.PostShockBloodTarget"/> plus
    /// what the current bleed takes over the grace, which keeps the heart going; and units to the line where
    /// the blood stops draining the brain. Zero when the body has no bloodstream or is already past the line.
    /// </summary>
    public (float ToTarget, float ToBrainSafe) GetTransfusionGuidance(EntityUid body)
    {
        if (!TryComp(body, out BloodstreamComponent? bloodstream))
            return (0f, 0f);

        FixedPoint2 max = bloodstream.BloodMaxVolume;
        var pool = max.Float();
        if (pool <= 0f)
            return (0f, 0f);

        var volume = GetBlood(body) * pool;
        var target = _cfg.GetCVar(WolfmedCVars.PostShockBloodTarget) * pool;
        var safe = _cfg.GetCVar(WolfmedCVars.BrainBloodStart) * pool;
        var bleed = GetVolumeLossRate(body) * MathF.Max(0f, _cfg.GetCVar(WolfmedCVars.PostShockGraceSeconds)); // M1b: burns too

        return (MathF.Max(0f, target - volume) + bleed, MathF.Max(0f, safe - volume));
    }

    /// <summary>
    /// What the analyzer says after a successful shock: shown while that shock is inside its repeat window,
    /// the heart is still going and the blood is still under the brain-safe line. Null otherwise.
    /// </summary>
    public WolfmedPostShockAdvice? GetPostShockAdvice(EntityUid body)
    {
        if (!TryComp(body, out WolfmedPostShockComponent? post) || InArrest(body) || _mobState.IsDead(body) ||
            post.SinceRestore >= PostShockRepeatSeconds)
            return null;

        var (units, safe) = GetTransfusionGuidance(body);
        if (safe <= 0f)
            return null;

        return new WolfmedPostShockAdvice(units, safe, GetPostShockGraceSeconds(body),
            _cfg.GetCVar(WolfmedCVars.BrainBloodStart) * 100f);
    }

    #endregion

    #region State changes

    /// <summary>Sets the brain's oxygenation, clamped to 0..1.</summary>
    public void SetOxygenation(Entity<WolfmedBrainComponent> brain, float value)
    {
        var clamped = Math.Clamp(value, 0f, 1f);
        if (MathF.Abs(brain.Comp.Oxygenation - clamped) < 0.0005f)
            return;

        brain.Comp.Oxygenation = clamped;
        Dirty(brain);
    }

    /// <summary>Sets the oxygenation of the body's brain, if it has one.</summary>
    public void SetOxygenation(EntityUid body, float value)
    {
        if (GetBrain(body) is { } brain)
            SetOxygenation(brain, value);
    }

    /// <summary>Stops the heart. Only a defibrillator, a restored heart or a rejuvenate starts it again.</summary>
    public bool StartArrest(EntityUid body, string cause)
    {
        if (TerminatingOrDeleted(body) || !OwnsDeath(body) || InArrest(body) || _mobState.IsDead(body) ||
            GetBrain(body) == null)
            return false;

        var arrest = AddComp<WolfmedCardiacArrestComponent>(body);
        arrest.StartTime = _timing.CurTime;
        arrest.Cause = cause;
        Dirty(body, arrest);

        // M4 (OD16): a species with no heart collapses instead; the texts read this before the pressure lands.
        if (TryComp(body, out WolfmedConsciousnessComponent? consciousness) &&
            consciousness.Heartless != IsHeartless(body))
        {
            consciousness.Heartless = !consciousness.Heartless;
            Dirty(body, consciousness);
        }

        _consciousness.SetExternalPressure(body, ArrestPressure, 1f);
        _bleeding.RefreshBody(body);
        UpdateVitalSigns(body);

        // M1a: only the Dying may let go (plan §5.4).
        _dyingActions.Grant(body);
        return true;
    }

    /// <summary>Ends a cardiac arrest and lifts its pressure; false when the body was not in arrest.</summary>
    public bool EndArrest(EntityUid body)
    {
        if (!TryComp(body, out WolfmedCardiacArrestComponent? stopped))
            return false;

        // M2 (plan §5.5): the analyzer's "After a restart" line remembers why the heart stopped. A corpse's arrest
        // ending (Succumb) is not a restart.
        if (!_mobState.IsDead(body))
        {
            var memory = EnsureComp<WolfmedArrestMemoryComponent>(body);
            memory.Cause = stopped.Cause;
            memory.RestartedAt = _timing.CurTime;
        }

        RemComp<WolfmedCardiacArrestComponent>(body);
        _dyingActions.Revoke(body);
        _consciousness.SetExternalPressure(body, ArrestPressure, 0f);
        _bleeding.RefreshBody(body);
        UpdateVitalSigns(body);
        return true;
    }

    /// <summary>
    /// Brain death. Ordinary <see cref="MobState.Dead"/>: the ghost, the corpse and the death screen, with
    /// nothing marking the body unrevivable.
    /// </summary>
    public bool Kill(EntityUid body)
    {
        if (TerminatingOrDeleted(body) || _mobState.IsDead(body) || !_mobState.HasState(body, MobState.Dead))
            return false;

        _mobState.ChangeMobState(body, MobState.Dead);
        return true;
    }

    /// <summary>
    /// What brain surgery leaves behind: a whole organ and the concussion effects for a while. M2 (OD10): a
    /// positronic core carries no trauma; the chassis shows "CORE RESTORED" for the same time instead.
    /// </summary>
    public void RepairBrain(EntityUid body)
    {
        if (GetBrainOrgan(body) is not { } organ)
            return;

        _organs.SetHealth(organ, organ.Comp.MaxHealth);
        SetOxygenation(body, MathF.Max(GetOxygenation(body), PostShockOxygenation));

        if (_shutdown.IsMechanical(body))
        {
            var restored = EnsureComp<WolfmedCoreRestoredComponent>(body);
            restored.Ends = _timing.CurTime + TimeSpan.FromMinutes(_cfg.GetCVar(WolfmedCVars.BrainTraumaMinutes));
            Dirty(body, restored);
            return;
        }

        var minutes = _cfg.GetCVar(WolfmedCVars.BrainTraumaMinutes);
        var trauma = EnsureComp<WolfmedBrainTraumaComponent>(body);
        trauma.Ends = _timing.CurTime + TimeSpan.FromMinutes(minutes);
        Dirty(body, trauma);

        // Playtest 5: the patient is told what the stutter and the blur are, and for how long.
        if (!_mobState.IsDead(body))
            _popup.PopupEntity(Loc.GetString("wolfmed-brain-trauma-start", ("minutes", (int) MathF.Ceiling(minutes))), body, body);

        if (GetBrain(body) is { } brain)
            brain.Comp.Concussed = false;

        _concussion.Refresh(body);
    }

    #endregion

    #region Events

    /// <summary>A brain leaving the body is death; a heart leaving it is arrest.</summary>
    private void OnOrganRemoved(Entity<WolfmedOrganComponent> organ, ref OrganRemovedFromBodyEvent args)
    {
        var body = args.OldBody;
        if (TerminatingOrDeleted(body) || !OwnsDeath(body))
            return;

        if (HasComp<BrainComponent>(organ) && !HasBrain(body))
        {
            Kill(body);
            return;
        }

        if (!HasComp<HeartComponent>(organ))
            return;

        StartArrest(body, "heart");
        _shutdown.Refresh(body);
    }

    /// <summary>A heart back in its slot gets one tick straight away, which is what ends the arrest.</summary>
    private void OnOrganAdded(Entity<WolfmedOrganComponent> organ, ref OrganAddedToBodyEvent args)
    {
        if (TerminatingOrDeleted(args.Body) || !OwnsDeath(args.Body))
            return;

        // M1a D: any other organ refreshes the vital signs at once. A body being assembled gets its heart
        // before its lungs, and without this read "not breathing: no lungs" until the next life tick; a lung
        // transplant reads as breathing the moment it is in.
        if (!HasComp<HeartComponent>(organ))
        {
            UpdateVitalSigns(args.Body);
            return;
        }

        Tick(args.Body, 0.0001f);
        _shutdown.Refresh(args.Body);
    }

    /// <summary>
    /// A head torn off takes the brain with it. Event driven, and it asks what was on the part that came
    /// off: a body that never had a brain in the first place has lost nothing.
    /// </summary>
    private void OnAmputated(ref WolfmedPartAmputatedEvent args) => OnPartDetached(args.Body, args.Part);

    /// <summary>
    /// Any part leaving the body, however it left: amputation, surgery, or a gib that deletes the part in
    /// place. The part lifecycle system owns the body's part-removed event and forwards it here.
    /// </summary>
    public void OnPartDetached(EntityUid body, EntityUid part)
    {
        if (TerminatingOrDeleted(body) || !OwnsDeath(body))
            return;

        if (CarriedBrain(part) && !HasBrain(body) || LostVitalPart(body, part))
            Kill(body);
    }

    /// <summary>Whether the body just lost its last part of a type it cannot do without.</summary>
    // A chassis keeps its positronic brain in the torso, so decapitating an IPC carries no brain off. The prototype
    // already calls a head vital, but upstream only turned that into bloodloss damage, which an inorganic damage
    // container does not carry; death on a wound host is ours, so the flag is read here.
    private bool LostVitalPart(EntityUid body, EntityUid part)
    {
        return TryComp(part, out BodyPartComponent? bodyPart) && bodyPart.IsVital &&
               !_body.GetBodyChildrenOfType(body, bodyPart.PartType).Any();
    }

    /// <summary>Whether a detached part, or anything still hanging off it, was holding a brain.</summary>
    private bool CarriedBrain(EntityUid part)
    {
        if (TerminatingOrDeleted(part))
            return false;

        foreach (var (child, _) in _body.GetBodyPartChildren(part))
        {
            foreach (var (organ, _) in _body.GetPartOrgans(child))
            {
                if (HasComp<BrainComponent>(organ))
                    return true;
            }
        }

        return false;
    }

    private void OnRejuvenate(ref WolfmedRejuvenateEvent args)
    {
        var body = args.Target;
        if (TerminatingOrDeleted(body))
            return;

        EndArrest(body);
        RemComp<WolfmedBrainTraumaComponent>(body);
        RemComp<WolfmedCprComponent>(body);
        RemComp<WolfmedPostShockComponent>(body);
        RemComp<WolfmedArrestMemoryComponent>(body); // M2
        RemComp<WolfmedCoreRestoredComponent>(body); // M2
        _consciousness.SetExternalPressure(body, HypoxiaPressure, 0f);

        if (GetBrainOrgan(body) is { } organ)
            _organs.SetHealth(organ, organ.Comp.MaxHealth);

        if (GetBrain(body) is { } brain)
        {
            SetOxygenation(brain, 1f);
            brain.Comp.Concussed = false;
        }

        _concussion.Refresh(body);
    }

    /// <summary>
    /// A pain shock on a body that has already bled out stops the heart instead. Off by default since M1a
    /// (<see cref="WolfmedCVars.ArrestShockBlood"/> 0): pain is never a route to death.
    /// </summary>
    private void OnPainShock(ref WolfmedPainShockEvent args)
    {
        var threshold = _cfg.GetCVar(WolfmedCVars.ArrestShockBlood);
        if (threshold <= 0f || !OwnsDeath(args.Body) || GetBlood(args.Body) > threshold)
            return;

        StartArrest(args.Body, "shock");
    }

    /// <summary>
    /// A big enough jolt stops the heart outright. A defibrillator's own zap is far below this. M2 (P28): the shock
    /// that counts is the one that got through the insulation, the same figure the electrocution deals as damage.
    /// </summary>
    private void OnElectrocuted(ElectrocutedEvent args)
    {
        var threshold = _cfg.GetCVar(WolfmedCVars.ArrestShockDamage);
        if (threshold <= 0f || args.ShockDamage is not { } raw || !OwnsDeath(args.TargetUid))
            return;

        var damage = raw * MathF.Max(0f, args.SiemensCoefficient);
        if (damage < threshold)
            return;

        StartArrest(args.TargetUid, "shock");
    }

    /// <summary>A damaged or freshly repaired brain contributes the same effects a concussion does.</summary>
    private void OnConcussionSources(ref WolfmedConcussionSourcesEvent args)
    {
        if (TryComp(args.Body, out WolfmedBrainTraumaComponent? trauma) && trauma.Ends > _timing.CurTime)
        {
            args.Blur = MathF.Max(args.Blur, trauma.Blur);
            args.Stutter = true;
        }

        if (GetBrain(args.Body) is not { } brain || !TryComp(brain, out WolfmedOrganComponent? organ) ||
            organ.MaxHealth <= FixedPoint2.Zero)
            return;

        var share = Math.Clamp(organ.Health.Float() / organ.MaxHealth.Float(), 0f, 1f);
        if (share >= brain.Comp.ConcussionAt)
            return;

        var depth = brain.Comp.ConcussionAt > 0f
            ? Math.Clamp((brain.Comp.ConcussionAt - share) / brain.Comp.ConcussionAt, 0f, 1f)
            : 1f;

        args.Blur = MathF.Max(args.Blur, brain.Comp.MaxBlur * depth);
        args.Stutter = true;
        args.Drop |= share < brain.Comp.SevereAt;
    }

    #endregion

    /// <summary>
    /// Seconds until the brain organ is destroyed at the current drain, or null when nothing is draining.
    /// Stepped rather than solved: the damage rate depends on oxygenation, which is itself moving.
    /// </summary>
    public float? GetBrainDeathSeconds(EntityUid body)
    {
        if (GetBrain(body) is not { } brain || !TryComp(brain, out WolfmedOrganComponent? organ) ||
            organ.Health <= FixedPoint2.Zero || _mobState.IsDead(body))
            return null;

        var rate = DrainRate(body, brain);
        var threshold = _cfg.GetCVar(WolfmedCVars.BrainDamageOxygenation);
        var damageRate = _cfg.GetCVar(WolfmedCVars.BrainDamageRate) * ColdFactor(brain, body); // M2 (P24)
        if (rate <= 0f || threshold <= 0f || damageRate <= 0f)
            return null;

        // Playtest 5: under compressions the brain holds at wolfmed.brain_cpr_floor; at or over the damage line there
        // is no death coming, and the analyzer's estimate says so by saying nothing.
        var floor = InCpr(body) && InArrest(body) ? MathF.Max(0f, _cfg.GetCVar(WolfmedCVars.BrainCprFloor)) : 0f;
        if (floor >= threshold)
            return null;

        var oxygen = brain.Comp.Oxygenation;
        var health = organ.Health.Float();
        for (var second = 0; second < 3600; second++)
        {
            oxygen = MathF.Max(floor, oxygen - rate);
            if (oxygen < threshold)
                health -= damageRate * Math.Clamp((threshold - oxygen) / threshold, 0f, 1f);

            if (health <= 0f)
                return second + 1;
        }

        return null;
    }
}

/// <summary>The analyzer's post-shock numbers: units inside the grace, units to the brain-safe line (a %).</summary>
public readonly record struct WolfmedPostShockAdvice(float Units, float SafeUnits, float GraceSeconds, float SafeLine);
