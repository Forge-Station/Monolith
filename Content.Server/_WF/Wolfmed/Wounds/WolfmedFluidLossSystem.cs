using Content.Server.Body.Components;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Medical.Surgery;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Body.Part;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Wolfmed.Wounds;

/// <summary>Drains blood volume once a second from every wound with <see cref="WolfmedFluidLossBehavior"/>.</summary>
// A badly burned body dies of fluid loss through the blood route medics already know. The volume is discarded, never
// spilled: burns weep, they do not bleed. A dressing cuts it to wolfmed.burn_dressed_fluid_factor, a graft stops it.
// Dead bodies lose nothing, and arrest does not slow it.
public sealed class WolfmedFluidLossSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private WoundSystem _wounds = default!;

    private const float TickSeconds = 1f;
    private float _accumulator;
    private readonly Dictionary<EntityUid, float> _rates = new();

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WolfmedWoundLifecycleEvent>(OnWoundLifecycle);
        SubscribeLocalEvent<WolfmedSurgeryGraftBurnsEffectComponent, SurgeryStepEvent>(OnGraftStep);
    }

    private void OnWoundLifecycle(ref WolfmedWoundLifecycleEvent args)
    {
        if (args.Kind == WolfmedWoundLifecycle.Removed || TerminatingOrDeleted(args.Wound) ||
            !_prototypes.TryIndex(args.Prototype, out var prototype) ||
            !prototype.TryGetBehavior(args.Severity, out WolfmedFluidLossBehavior _))
            return;

        EnsureComp<WolfmedFluidLossComponent>(args.Wound);
    }

    private void OnGraftStep(Entity<WolfmedSurgeryGraftBurnsEffectComponent> ent, ref SurgeryStepEvent args)
    {
        Treat(args.Part, grafted: true);
    }

    /// <summary>A burn dressing went on this part: every weeping wound on it is dressed.</summary>
    public bool Dress(EntityUid part) => Treat(part, grafted: false);

    /// <summary>A graft closed this part: its weeping wounds stop losing fluid. The surgery step calls this.</summary>
    public bool Graft(EntityUid part) => Treat(part, grafted: true);

    private bool Treat(EntityUid part, bool grafted)
    {
        var any = false;
        foreach (var wound in _wounds.GetWounds(part))
        {
            if (!HasComp<WolfmedFluidLossComponent>(wound))
                continue;

            var dressed = EnsureComp<WolfmedDressedComponent>(wound);
            dressed.Grafted |= grafted;
            dressed.TreatedSeverity = wound.Comp.Severity;
            Dirty(wound, dressed);
            any = true;
        }

        return any;
    }

    /// <summary>The body's burn fluid loss in units a second, as of the last tick.</summary>
    public float GetRate(EntityUid body) => CompOrNull<WolfmedBurnFluidLossComponent>(body)?.Rate ?? 0f;

    /// <summary>What one weeping wound loses a second right now, before the global multiplier.</summary>
    public float WoundRate(Entity<WoundComponent> wound)
    {
        if (wound.Comp.State is WoundState.Healed or WoundState.Scarred ||
            !_prototypes.TryIndex(wound.Comp.Prototype, out var prototype) ||
            !prototype.TryGetBehavior(wound.Comp.Severity, out WolfmedFluidLossBehavior behavior) ||
            wound.Comp.Severity < behavior.From)
            return 0f;

        var factor = 1f;
        if (TryComp(wound, out WolfmedDressedComponent? dressed))
        {
            // Burned again past where it was treated: the dressing or graft is gone.
            if (wound.Comp.Severity - dressed.TreatedSeverity >=
                FixedPoint2.New(_config.GetCVar(WolfmedCVars.BurnTreatmentLostSeverity)))
                RemComp<WolfmedDressedComponent>(wound);
            else
                factor = dressed.Grafted ? 0f : _config.GetCVar(WolfmedCVars.BurnDressedFluidFactor);
        }

        return wound.Comp.Severity.Float() * behavior.PerSeverity * factor;
    }

    /// <inheritdoc/>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        _accumulator += frameTime;
        if (_accumulator < TickSeconds)
            return;

        var elapsed = _accumulator;
        _accumulator = 0f;
        Tick(elapsed);
    }

    /// <summary>
    /// One fluid-loss step for every body with weeping wounds. Public as the scenario tests' seam (plan §12.0).
    /// </summary>
    public void Tick(float seconds)
    {
        var multiplier = _config.GetCVar(WolfmedCVars.BurnFluidRate);
        _rates.Clear();

        var query = EntityQueryEnumerator<WolfmedFluidLossComponent, WoundComponent>();
        while (query.MoveNext(out var uid, out _, out var wound))
        {
            if (CompOrNull<BodyPartComponent>(wound.HoldingPart)?.Body is not { } body)
                continue;

            var rate = WoundRate((uid, wound)) * multiplier;
            _rates[body] = _rates.GetValueOrDefault(body) + rate;
        }

        // Bodies that stopped weeping since the last tick lose their readout.
        var stale = new List<EntityUid>();
        var shown = EntityQueryEnumerator<WolfmedBurnFluidLossComponent>();
        while (shown.MoveNext(out var uid, out _))
        {
            if (_rates.GetValueOrDefault(uid) <= 0f)
                stale.Add(uid);
        }

        foreach (var uid in stale)
            RemComp<WolfmedBurnFluidLossComponent>(uid);

        foreach (var (body, rate) in _rates)
        {
            if (rate <= 0f || TerminatingOrDeleted(body))
                continue;

            if (!HasComp<WolfmedBurnFluidLossComponent>(body) && !_mobState.IsDead(body))
                _popup.PopupEntity(Loc.GetString("wolfmed-burn-fluid-start"), body, body, PopupType.MediumCaution);

            var shownRate = EnsureComp<WolfmedBurnFluidLossComponent>(body);
            if (MathF.Abs(shownRate.Rate - rate) > 0.001f)
            {
                shownRate.Rate = rate;
                Dirty(body, shownRate);
            }

            if (!_mobState.IsDead(body))
                Drain(body, shownRate, rate * seconds);
        }
    }

    /// <summary>Takes the volume straight out of the blood solution; nothing reaches the floor.</summary>
    private void Drain(EntityUid body, WolfmedBurnFluidLossComponent owed, float units)
    {
        if (!TryComp(body, out BloodstreamComponent? bloodstream) ||
            !_solutions.ResolveSolution(body, bloodstream.BloodSolutionName, ref bloodstream.BloodSolution, out _))
            return;

        // FixedPoint2 moves in hundredths; small burns would round to nothing a tick.
        owed.Owed += units;
        var take = FixedPoint2.New(owed.Owed);
        if (take <= FixedPoint2.Zero)
            return;

        owed.Owed -= take.Float();
        _solutions.SplitSolution(bloodstream.BloodSolution.Value, take);
    }
}
