using Content.Server._WF.Wolfmed.Body;
using Content.Server._WF.Wolfmed.Consciousness;
using Content.Server.Temperature.Components;
using Content.Shared._Onyx.Body.Systems;
using Content.Shared._Shitmed.Body.Organ;
using Content.Shared._WF.Wolfmed.Body;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.Compat;
using Content.Shared._WF.Wolfmed.Consciousness;
using Content.Shared._WF.Wolfmed.Life;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Atmos;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.IdentityManagement;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.Wolfmed.Life;

/// <summary>Overheating on a mechanical wound host: Heat on its parts, and core heat that shuts it down.</summary>
// The pulse replaces upstream's instant overheat kill with Heat on the parts, through the wound pipeline, so a
// burning IPC goes down from pain. It never reaches the organs.
// The positronic core soaks up the chassis's heat and a working, powered coolant pump takes it off. Past
// wolfmed.ipc_core_heat_k the core loses health, faster the hotter it is, and the chassis is in thermal shutdown
// (Dying, cause CoreHeat, with Succumb and Last Words); under wolfmed.ipc_core_heat_wake_k it comes back online.
// A core at 0 is core failure, the ordinary organ death path.
public sealed class WolfmedOverheatSystem : EntitySystem
{
    /// <summary>Pressure key for thermal shutdown. Full, so the chassis is out while the core cooks.</summary>
    public const string CoreHeatPressure = "coreheat";

    private static readonly TimeSpan PulseInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan EpisodeGap = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan WarningInterval = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(1);
    private static readonly ProtoId<DamageTypePrototype> Heat = "Heat";

    /// <summary>The temperature a pump cools the core toward, and never below.</summary>
    private const float CoolingFloor = Atmospherics.T0C + Atmospherics.NormalBodyTemperature;

    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private OrganHealthSystem _organs = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private WolfmedConsciousnessSystem _consciousness = default!;
    [Dependency] private WolfmedDamageableSystem _damageable = default!;
    [Dependency] private WolfmedDyingActionsSystem _dyingActions = default!;
    [Dependency] private WolfmedLifeSystem _life = default!;
    [Dependency] private WolfmedOrganThresholdSystem _organReach = default!;
    [Dependency] private WolfmedShutdownSystem _shutdown = default!;

    private readonly List<EntityUid> _due = new();
    private TimeSpan _nextTick;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WolfmedRejuvenateEvent>(OnRejuvenate);
    }

    #region Pulse

    /// <summary>
    /// Takes over the kill for a body whose death Wolfmed owns, and reports whether it did. Called once a
    /// frame per overheating body, so the burn and the warning sit on their own pulse.
    /// </summary>
    public bool TryOverheat(EntityUid body, LocId popup)
    {
        if (TerminatingOrDeleted(body) || !_consciousness.OwnsMobState(body))
            return false;

        var comp = EnsureComp<WolfmedOverheatComponent>(body);
        if (comp.NextPulse > _timing.CurTime)
            return true;

        // A gap longer than a couple of pulses means the body cooled off in between: a new episode, warned
        // again straight away. Within one episode the warning repeats slowly rather than every pulse.
        var now = _timing.CurTime;
        var newEpisode = now - comp.NextPulse > EpisodeGap;
        comp.NextPulse = now + PulseInterval;

        // The upstream line says the circuits shut down, which is no longer what happens here.
        if (newEpisode || now >= comp.NextWarning)
        {
            comp.NextWarning = now + WarningInterval;
            _popup.PopupEntity(Loc.GetString("wolfmed-overheat-popup", ("name", Identity.Name(body, EntityManager))),
                body, PopupType.MediumCaution);
        }

        // M4: the parts burn and hurt; the core is the core-heat route's alone. M6: a tick, like fire, so it
        // interrupts no do-after (OD18).
        var damage = new DamageSpecifier(_prototypes.Index(Heat), FixedPoint2.New(comp.HeatPerPulse));
        _organReach.WithoutOrganReach(() => _damageable.ChangeDamage(body, damage, interruptsDoAfters: false));
        return true;
    }

    #endregion

    #region Core heat

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

    /// <summary>In thermal shutdown right now: alive, and the core has not cooled back under its wake line.</summary>
    public bool InThermalShutdown(EntityUid body) =>
        TryComp(body, out WolfmedCoreHeatComponent? heat) && heat.ThermalShutdown && !_mobState.IsDead(body);

    /// <summary>The core is past its line and losing health right now: the route the analyzer lists.</summary>
    public bool CoreCooking(EntityUid body) =>
        TryComp(body, out WolfmedCoreHeatComponent? heat) && !_mobState.IsDead(body) &&
        heat.CoreTemperature > _cfg.GetCVar(WolfmedCVars.IpcCoreHeatK) &&
        _life.GetBrainOrgan(body) is { } core && core.Comp.Health > FixedPoint2.Zero;

    /// <summary>
    /// Kelvin a second the pump takes off the core right now: the full rate from a working pump with power behind
    /// it, its organ's impaired factor under half health, and nothing from a destroyed, missing or unpowered one.
    /// </summary>
    public float PumpCooling(EntityUid body)
    {
        if (!_shutdown.HasPower(body))
            return 0f;

        foreach (var (organ, _) in _body.GetBodyOrgans(body))
        {
            if (!HasComp<HeartComponent>(organ))
                continue;

            var rate = MathF.Max(0f, _cfg.GetCVar(WolfmedCVars.IpcPumpCooling));
            if (!TryComp(organ, out WolfmedOrganComponent? health))
                return rate;

            return health.Band switch
            {
                WolfmedOrganBand.Failed => 0f,
                WolfmedOrganBand.Impaired => rate * Math.Clamp(health.ImpairedCoolingFactor, 0f, 1f),
                _ => rate,
            };
        }

        return 0f;
    }

    /// <summary>
    /// One body's share of the tick. Public so a test can run the clock forward. The core closes
    /// <c>wolfmed.ipc_core_heat_soak</c> of its gap to the chassis each second, the pump cools it, and the core
    /// then loses <c>wolfmed.ipc_core_heat_rate</c> health a second for every 100 K it is over the line.
    /// </summary>
    public void Tick(EntityUid body, float seconds)
    {
        if (seconds <= 0f || TerminatingOrDeleted(body))
            return;

        if (!_shutdown.IsMechanical(body) || !TryComp(body, out TemperatureComponent? temperature))
        {
            // Not a machine any more (or consciousness is off): nothing here owns it.
            if (TryComp(body, out WolfmedCoreHeatComponent? stale))
            {
                SetThermalShutdown((body, stale), false);
                RemComp<WolfmedCoreHeatComponent>(body);
            }

            return;
        }

        var chassis = temperature.CurrentTemperature;
        if (!TryComp(body, out WolfmedCoreHeatComponent? heat))
        {
            heat = AddComp<WolfmedCoreHeatComponent>(body);
            heat.CoreTemperature = chassis;
        }

        var core = heat.CoreTemperature;
        core += (chassis - core) * Math.Clamp(_cfg.GetCVar(WolfmedCVars.IpcCoreHeatSoak) * seconds, 0f, 1f);
        if (core > CoolingFloor)
            core = MathF.Max(CoolingFloor, core - PumpCooling(body) * seconds);

        SetTemperatures((body, heat), core, chassis);

        if (_mobState.IsDead(body))
        {
            SetThermalShutdown((body, heat), false);
            return;
        }

        var line = _cfg.GetCVar(WolfmedCVars.IpcCoreHeatK);
        if (core > line && _life.GetBrainOrgan(body) is { } organ && organ.Comp.Health > FixedPoint2.Zero)
        {
            var loss = MathF.Max(0f, _cfg.GetCVar(WolfmedCVars.IpcCoreHeatRate)) * (core - line) / 100f * seconds;
            if (loss > 0f)
                _organs.ChangeHealth(organ, FixedPoint2.New(-loss));
        }

        if (!heat.ThermalShutdown && core >= line)
            SetThermalShutdown((body, heat), true);
        else if (heat.ThermalShutdown && core < _cfg.GetCVar(WolfmedCVars.IpcCoreHeatWakeK))
            SetThermalShutdown((body, heat), false);
        else if (heat.ThermalShutdown)
            _consciousness.SetExternalPressure(body, CoreHeatPressure, 1f); // a rejuvenate may have cleared it
    }

    /// <summary>Sets the core temperature, for tests and admins; the next tick carries on from it.</summary>
    public void SetCoreTemperature(EntityUid body, float kelvin)
    {
        if (!TryComp(body, out WolfmedCoreHeatComponent? heat))
            return;

        SetTemperatures((body, heat), kelvin,
            CompOrNull<TemperatureComponent>(body)?.CurrentTemperature ?? heat.ChassisTemperature);
    }

    private void SetTemperatures(Entity<WolfmedCoreHeatComponent> heat, float core, float chassis)
    {
        heat.Comp.ChassisTemperature = chassis;
        var warn = _cfg.GetCVar(WolfmedCVars.IpcCoreHeatWarnK);
        var hot = core >= warn || chassis >= warn;
        var moved = MathF.Abs(heat.Comp.CoreTemperature - core) >= 1f || hot != heat.Comp.Hot;
        heat.Comp.CoreTemperature = core;
        heat.Comp.Hot = hot;
        if (moved)
            Dirty(heat);
    }

    /// <summary>
    /// Into or out of thermal shutdown: the flag, the pressure that holds the chassis out, and the Dying actions,
    /// by direct calls as arrest does (plan §5.4 item 3).
    /// </summary>
    public void SetThermalShutdown(Entity<WolfmedCoreHeatComponent> heat, bool on)
    {
        if (heat.Comp.ThermalShutdown == on)
            return;

        heat.Comp.ThermalShutdown = on;
        if (on)
            heat.Comp.ThermalShutdownStart = _timing.CurTime;

        Dirty(heat);
        _consciousness.SetExternalPressure(heat, CoreHeatPressure, on ? 1f : 0f);

        if (on)
            _dyingActions.Grant(heat);
        else
            _dyingActions.Revoke(heat);
    }

    /// <summary>Ends a thermal shutdown outright, whatever the core says: Succumb, on the corpse.</summary>
    public void EndThermalShutdown(EntityUid body)
    {
        if (TryComp(body, out WolfmedCoreHeatComponent? heat))
            SetThermalShutdown((body, heat), false);
    }

    /// <summary>A rejuvenate cools the core with the chassis, which the engine sets to 20 °C.</summary>
    private void OnRejuvenate(ref WolfmedRejuvenateEvent args)
    {
        if (TerminatingOrDeleted(args.Target) || !TryComp(args.Target, out WolfmedCoreHeatComponent? heat))
            return;

        SetThermalShutdown((args.Target, heat), false);
        SetTemperatures((args.Target, heat), Atmospherics.T20C, Atmospherics.T20C);
    }

    #endregion
}
