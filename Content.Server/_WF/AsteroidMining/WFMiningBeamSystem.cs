using System.Numerics;
using Content.Server.Power.EntitySystems;
using Content.Shared._Mono.FireControl;
using Content.Shared._WF.AsteroidMining;
using Content.Shared.ActionBlocker;
using Content.Shared.Weapons.Hitscan.Events;
using Content.Shared.Weapons.Hitscan.Systems;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.AsteroidMining;

/// <summary>
/// Runs ship mining lasers. Each pulse that hits an asteroid keeps the gun's power on it; a rock that stays inside its
/// window long enough cracks cleanly, and one held over the overcharge line blows up and knocks its guns offline.
/// Gunnery consoles, and cockpit pilots linked to one, set the lasers' power and get a scan of the rock they are working.
/// </summary>
public sealed partial class WFMiningBeamSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private ActionBlockerSystem _blocker = default!;
    [Dependency] private WFAsteroidSystem _asteroids = default!;

    private static readonly TimeSpan UiInterval = TimeSpan.FromSeconds(0.25);
    private TimeSpan _nextUi;
    private readonly HashSet<EntityUid> _heated = new();
    private readonly List<EntityUid> _heatedList = new();
    private readonly HashSet<EntityUid> _openConsoles = new();
    private readonly List<EntityUid> _closedConsoles = new();
    /// <summary>Each ship's mining lasers, grouped once per gunnery refresh rather than once per console.</summary>
    private readonly Dictionary<EntityUid, List<(EntityUid Uid, WFMiningBeamEmitterComponent Beam)>> _shipLasers = new();
    private static readonly List<(EntityUid Uid, WFMiningBeamEmitterComponent Beam)> NoLasers = new();
    /// <summary>Emptied lists from earlier groupings, reused rather than reallocated four times a second.</summary>
    private readonly Stack<List<(EntityUid Uid, WFMiningBeamEmitterComponent Beam)>> _spareLasers = new();
    /// <summary>
    /// Each open gunnery console's last rock, kept until it breaks or the console closes, and what was last sent and to
    /// which viewers, so an unchanged scan isn't resent.
    /// </summary>
    private readonly Dictionary<EntityUid, (EntityUid? Rock, int Viewers, WFMiningBeamGunneryState Sent)> _gunnery = new();

    public override void Initialize()
    {
        base.Initialize();
        Subs.BuiEvents<FireControlConsoleComponent>(FireControlConsoleUiKey.Key, subs =>
        {
            subs.Event<WFMiningBeamPowerMessage>(OnPowerMessage);
        });
        InitializeCockpit();
        SubscribeLocalEvent<WFMiningBeamEmitterComponent, ComponentShutdown>(OnEmitterShutdown);
        SubscribeLocalEvent<WFMiningBeamEmitterComponent, AttemptShootEvent>(OnAttemptShoot);
        // After reflection, which may cancel the pulse, and before damage, so rocks are heated rather than chipped.
        SubscribeLocalEvent<WFMiningPulseComponent, HitscanRaycastFiredEvent>(OnPulse,
            after: new[] { typeof(HitscanReflectSystem) }, before: new[] { typeof(HitscanBasicDamageSystem) });
    }

    private void OnEmitterShutdown(Entity<WFMiningBeamEmitterComponent> ent, ref ComponentShutdown args)
    {
        Stop(ent, ent.Comp);
    }

    private void OnAttemptShoot(Entity<WFMiningBeamEmitterComponent> ent, ref AttemptShootEvent args)
    {
        if (_timing.CurTime < ent.Comp.CooldownUntil)
            args.Cancelled = true;
    }

    private void OnPowerMessage(Entity<FireControlConsoleComponent> ent, ref WFMiningBeamPowerMessage args)
    {
        if (_blocker.CanInteract(args.Actor, ent))
            SetPower(ent, args.Power);
    }

    /// <summary>Sets every mining laser on a working gunnery console's ship, then refreshes whoever is watching them.</summary>
    private void SetPower(EntityUid console, float power)
    {
        if (!float.IsFinite(power) || GunneryGrid(console) is not { } grid)
            return;

        power = MathF.Round(Math.Clamp(power, 0f, 1f), 2);
        GroupShipLasers();
        foreach (var (uid, beam) in ShipLasers(grid))
        {
            beam.Power = power;
            Dirty(uid, beam);
        }

        UpdateGunnery(console);
        UpdateCockpits(true);
    }

    /// <summary>The ship a working gunnery console speaks for.</summary>
    private EntityUid? GunneryGrid(EntityUid console)
    {
        var xform = Transform(console);
        return xform.Anchored && this.IsPowered(console, EntityManager) ? xform.GridUid : null;
    }

    /// <summary>Sorts every mining laser by the ship it sits on.</summary>
    private void GroupShipLasers()
    {
        foreach (var spare in _shipLasers.Values)
        {
            spare.Clear();
            _spareLasers.Push(spare);
        }

        _shipLasers.Clear();
        var query = EntityQueryEnumerator<WFMiningBeamEmitterComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var beam, out var xform))
        {
            if (xform.GridUid is not { } grid)
                continue;

            if (!_shipLasers.TryGetValue(grid, out var lasers))
                _shipLasers[grid] = lasers = _spareLasers.TryPop(out var spare) ? spare : new();
            lasers.Add((uid, beam));
        }
    }

    /// <summary>A ship's mining lasers as of the last <see cref="GroupShipLasers"/>.</summary>
    private List<(EntityUid Uid, WFMiningBeamEmitterComponent Beam)> ShipLasers(EntityUid grid)
    {
        return _shipLasers.TryGetValue(grid, out var lasers) ? lasers : NoLasers;
    }

    /// <summary>Points the beam at whatever the pulse hit, and takes asteroids out of the hit list so they heat instead.</summary>
    private void OnPulse(Entity<WFMiningPulseComponent> ent, ref HitscanRaycastFiredEvent args)
    {
        if (args.Canceled || !TryComp<WFMiningBeamEmitterComponent>(args.Gun, out var beam))
            return;

        EntityUid? rock = null;
        foreach (var hit in args.HitEntities)
        {
            if (TryComp<WFAsteroidComponent>(hit, out var asteroid) && !asteroid.Cracking)
            {
                rock = hit;
                break;
            }
        }

        if (rock is { } target)
            args.HitEntities.Remove(target);

        var direction = args.ShotDirection.LengthSquared() > 0f ? Vector2.Normalize(args.ShotDirection) : beam.BeamDirection;
        Pulse(args.Gun, beam, rock, args.DistanceTried, direction);
    }

    /// <summary>
    /// Records a pulse: lights the beam along the shot, which fire control aims at the cursor, and on an asteroid keeps
    /// the gun's power on it.
    /// </summary>
    public void Pulse(EntityUid gun, WFMiningBeamEmitterComponent beam, EntityUid? rock, float distance, Vector2 direction)
    {
        beam.LastPulse = _timing.CurTime;
        // Resend only on a visible change: about a degree of swing or a third of a metre of length.
        var changed = !beam.Active || beam.Target != rock ||
                      Vector2.Dot(beam.BeamDirection, direction) < 0.9998f ||
                      MathF.Abs(beam.BeamLength - distance) > 0.3f;
        beam.Active = true;
        beam.Target = rock;
        beam.BeamDirection = direction;
        beam.BeamLength = distance;
        if (rock is { } target)
            _heated.Add(target);

        if (changed)
            Dirty(gun, beam);
    }

    /// <summary>Keeps a rock's heat running though no beam is on it yet, such as a fragment born hot.</summary>
    public void Track(EntityUid rock)
    {
        _heated.Add(rock);
    }

    /// <summary>Puts a gun's beam out.</summary>
    public void Stop(EntityUid gun, WFMiningBeamEmitterComponent comp)
    {
        if (!comp.Active && comp.Target == null)
            return;

        comp.Active = false;
        comp.Target = null;
        comp.InWindow = false;
        comp.Hot = false;
        comp.Charge = 0f;
        if (!TerminatingOrDeleted(gun))
            Dirty(gun, comp);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var emitters = EntityQueryEnumerator<WFMiningBeamEmitterComponent>();
        while (emitters.MoveNext(out var uid, out var beam))
        {
            if (!beam.Active)
                continue;

            if (now - beam.LastPulse > TimeSpan.FromSeconds(beam.ActiveTime))
            {
                Stop(uid, beam);
                continue;
            }

            if (beam.Target is not { } rock || TerminatingOrDeleted(rock) ||
                !TryComp<WFAsteroidComponent>(rock, out var asteroid) || asteroid.Cracking)
            {
                if (beam.InWindow || beam.Hot)
                {
                    beam.InWindow = false;
                    beam.Hot = false;
                    Dirty(uid, beam);
                }
                continue;
            }

            asteroid.IncomingPower += beam.Power * beam.Strength;
            asteroid.CrackRate += beam.CrackRate;
            _heated.Add(rock);

            var (low, high) = Window(asteroid);
            var inWindow = asteroid.Charge >= low && asteroid.Charge <= high;
            var hot = asteroid.Charge > high && asteroid.Charge < WFAsteroidHeat.OverchargeLine;
            if (MathF.Abs(beam.Charge - asteroid.Charge) > 0.03f || beam.InWindow != inWindow || beam.Hot != hot)
            {
                beam.Charge = asteroid.Charge;
                beam.InWindow = inWindow;
                beam.Hot = hot;
                Dirty(uid, beam);
            }
        }

        _heatedList.Clear();
        _heatedList.AddRange(_heated);
        foreach (var rock in _heatedList)
        {
            if (TerminatingOrDeleted(rock) || !TryComp<WFAsteroidComponent>(rock, out var comp) || comp.Cracking ||
                !Heat(rock, comp, frameTime))
                _heated.Remove(rock);
        }

        if (now < _nextUi)
            return;

        _nextUi = now + UiInterval;
        _openConsoles.Clear();
        var consoles = EntityQueryEnumerator<FireControlConsoleComponent>();
        while (consoles.MoveNext(out var uid, out _))
        {
            if (_ui.IsUiOpen(uid, FireControlConsoleUiKey.Key))
                _openConsoles.Add(uid);
        }

        if (_openConsoles.Count > 0)
            GroupShipLasers();
        foreach (var uid in _openConsoles)
        {
            UpdateGunnery(uid);
        }

        UpdateCockpits(_openConsoles.Count > 0);

        _closedConsoles.Clear();
        foreach (var uid in _gunnery.Keys)
        {
            if (!_openConsoles.Contains(uid))
                _closedConsoles.Add(uid);
        }

        foreach (var uid in _closedConsoles)
        {
            _gunnery.Remove(uid);
        }
    }

    /// <summary>The heat band a rock cracks cleanly in.</summary>
    public static (float Low, float High) Window(WFAsteroidComponent comp)
    {
        return (comp.WindowCenter - comp.WindowHalfWidth, comp.WindowCenter + comp.WindowHalfWidth);
    }

    /// <summary>Advances a rock's heat by a tick. Returns false once it has cooled and no beam is on it.</summary>
    private bool Heat(EntityUid rock, WFAsteroidComponent comp, float frameTime)
    {
        // Take this tick's beam totals, so they start from zero next tick whether or not the rock stays tracked.
        var power = comp.IncomingPower;
        var crackRate = comp.CrackRate;
        comp.IncomingPower = 0f;
        comp.CrackRate = 0f;

        var heating = power > 0f;
        var equilibrium = power * (1f - comp.Resistance);
        // Heavier rocks heat and cool more slowly; with no beam they cool at half speed.
        var tau = (0.5f + comp.Mass * 0.004f) * (heating ? 1f : 2f);
        comp.BaseCharge += (equilibrium - comp.BaseCharge) * (1f - MathF.Exp(-frameTime / tau));

        // Unstable rocks drift slowly around the beam's heat, so the power has to follow them.
        var time = (float) _timing.CurTime.TotalSeconds;
        var wobble = heating
            ? comp.Instability * 0.08f * (0.6f * MathF.Sin(time * 0.9f + comp.NoiseSeed) +
                                          0.4f * MathF.Sin(time * 0.37f + comp.NoiseSeed * 1.7f))
            : 0f;
        var charge = MathF.Max(0f, comp.BaseCharge + wobble);
        var (low, high) = Window(comp);

        var rate = crackRate / WFAsteroidSystem.CrackTime(comp);
        if (heating && charge >= low && charge <= high)
        {
            comp.Progress += frameTime * rate;
        }
        else if (heating && charge > high && charge < WFAsteroidHeat.OverchargeLine)
        {
            // Pushing past the window cracks faster, but the hot share of the crack costs ore.
            var step = frameTime * rate * WFAsteroidHeat.HotCrackSpeed;
            comp.Progress += step;
            comp.HotProgress += step;
        }
        else if (comp.Progress > 0f)
        {
            var before = comp.Progress;
            comp.Progress = MathF.Max(0f, comp.Progress - frameTime * 0.04f);
            comp.HotProgress *= comp.Progress / before;
        }

        comp.OverchargeTime = charge >= WFAsteroidHeat.OverchargeLine
            ? comp.OverchargeTime + frameTime
            : MathF.Max(0f, comp.OverchargeTime - frameTime);

        var overcharging = comp.OverchargeTime > 0f;
        if (MathF.Abs(charge - comp.Charge) > 0.02f || overcharging != comp.Overcharging ||
            MathF.Abs(comp.Progress - comp.SentProgress) > 0.04f)
        {
            comp.Charge = charge;
            comp.Overcharging = overcharging;
            comp.SentProgress = comp.Progress;
            Dirty(rock, comp);
        }

        if (comp.OverchargeTime >= WFAsteroidHeat.OverchargeDuration)
        {
            Overload(rock);
            _asteroids.QueueCrack(rock, WFAsteroidCrack.Overcharge, comp);
            return false;
        }

        if (comp.Progress >= 1f)
        {
            _asteroids.QueueCrack(rock, WFAsteroidCrack.Clean, comp);
            return false;
        }

        if (heating || comp.BaseCharge > 0.01f || comp.Progress > 0f)
            return true;

        comp.BaseCharge = 0f;
        comp.Charge = 0f;
        comp.Overcharging = false;
        Dirty(rock, comp);
        return false;
    }

    /// <summary>Knocks every gun on a rock offline for its overload cooldown.</summary>
    private void Overload(EntityUid rock)
    {
        var emitters = EntityQueryEnumerator<WFMiningBeamEmitterComponent>();
        while (emitters.MoveNext(out var uid, out var beam))
        {
            if (beam.Target != rock)
                continue;

            beam.CooldownUntil = _timing.CurTime + TimeSpan.FromSeconds(beam.OverloadCooldown);
            Stop(uid, beam);
        }
    }

    /// <summary>
    /// Sends a gunnery console its ship's laser power and a scan of the rock its lasers are on, or were last on. Nothing
    /// is sent if neither the state nor its viewers changed. Lasers are read from the last <see cref="GroupShipLasers"/>.
    /// </summary>
    private void UpdateGunnery(EntityUid console)
    {
        var known = _gunnery.TryGetValue(console, out var last);
        var rock = last.Rock;
        var state = GunneryState(GunneryGrid(console), ref rock);

        // A viewer who opens an already open console has missed the last send; summing the actors catches that.
        var viewers = 0;
        foreach (var actor in _ui.GetActors(console, FireControlConsoleUiKey.Key))
        {
            viewers += actor.Id;
        }

        var unchanged = known && last.Viewers == viewers && SameState(last.Sent, state);
        _gunnery[console] = (rock, viewers, state);
        if (!unchanged)
            _ui.ServerSendUiMessage(console, FireControlConsoleUiKey.Key, new WFMiningBeamGunneryUpdateMessage(state));
    }

    /// <summary>
    /// A ship's laser power and a scan of the rock its lasers are on, or else of <paramref name="rock"/>, the rock they
    /// were last on, which is then set to the rock scanned or null. A ship without mining lasers gets an empty state,
    /// which hides the mining panels. Lasers are read from the last <see cref="GroupShipLasers"/>.
    /// </summary>
    private WFMiningBeamGunneryState GunneryState(EntityUid? grid, ref EntityUid? rock)
    {
        var lasers = grid is { } ship ? ShipLasers(ship) : NoLasers;
        var last = rock;
        rock = null;
        if (lasers.Count == 0)
            return new WFMiningBeamGunneryState(0, 0f, 0f, null);

        var cooldown = 0f;
        foreach (var (_, beam) in lasers)
        {
            rock ??= beam.Target;
            cooldown = MathF.Max(cooldown, (float) (beam.CooldownUntil - _timing.CurTime).TotalSeconds);
        }

        rock ??= last;
        WFMiningBeamTargetEntry? target = null;
        if (rock is { } uid && !TerminatingOrDeleted(uid) && TryComp<WFAsteroidComponent>(uid, out var comp) && !comp.Cracking)
        {
            var (low, high) = Window(comp);
            var ores = new WFMiningBeamOreEntry[comp.Ores.Count];
            for (var i = 0; i < ores.Length; i++)
            {
                var ore = comp.Ores[i];
                ores[i] = new WFMiningBeamOreEntry(WFAsteroidScan.OreName(_proto, ore.Ore), ore.Fraction, ore.Color);
            }

            target = new WFMiningBeamTargetEntry(Name(uid), comp.Mass, ores, comp.Resistance, low, high, comp.Charge,
                Math.Clamp(comp.Progress, 0f, 1f), comp.OverchargeTime / WFAsteroidHeat.OverchargeDuration,
                WFAsteroidHeat.Yield(comp.Progress, comp.HotProgress));
        }
        else
        {
            rock = null;
        }

        return new WFMiningBeamGunneryState(lasers.Count, lasers[0].Beam.Power, cooldown, target);
    }

    private static bool SameState(WFMiningBeamGunneryState a, WFMiningBeamGunneryState b)
    {
        if (a with { Target = null } != b with { Target = null })
            return false;

        if (a.Target is not { } x || b.Target is not { } y)
            return a.Target == null && b.Target == null;

        // The ore list is an array, which record equality compares by reference.
        return x with { Ores = y.Ores } == y && x.Ores.AsSpan().SequenceEqual(y.Ores);
    }
}
