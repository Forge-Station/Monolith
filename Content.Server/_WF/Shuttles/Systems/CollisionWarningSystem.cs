using Content.Shared._WF.ShipPa;
using System.Numerics;
using Content.Server._WF.ShipPa;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared.CCVar;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Shuttles;
using Content.Shared.Maps;
using Content.Shared.Shuttles.Components;
using Content.Shared.Popups;
using Robust.Shared.Audio;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.Shuttles.Systems;

/// <summary>
/// Ship collision warning. Sweeps every piloted ship's hull along its velocity, finds what it is going
/// to hit and how soon, and puts a <see cref="CollisionWarningComponent"/> on the grid plus an alarm on
/// the ship's PA. Only closing speeds hard enough to hurt count, so ordinary station approaches are quiet.
/// </summary>
public sealed partial class CollisionWarningSystem : EntitySystem
{
    private static readonly ProtoId<ContentTileDefinition> PlatingProto = "Plating";

    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedMapSystem _mapManager = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedTransformSystem _xform = default!;
    [Dependency] private ShuttleSystem _shuttle = default!;
    [Dependency] private ShipPaSystem _pa = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private IPrototypeManager _proto = default!;

    /// <summary>Mass the impact system gives a plating tile; the prototype is indexed once at startup.</summary>
    private static float PlatingMass = 1000f;

    /// <summary>Shuttle mass the impact system scales its inertia multiplier around.</summary>
    private const float BaseShuttleMass = 50f;

    /// <summary>Alarm loop key for the traffic advisory klaxon.</summary>
    public const string AdvisoryAlarm = "tcas-advisory";

    /// <summary>Alarm loop key for the imminent collision klaxon.</summary>
    public const string ImminentAlarm = "tcas-imminent";

    /// <summary>One-shot key for the periodic advisory voice callout.</summary>
    public const string AdvisoryCallout = "tcas-advisory-callout";

    public const int AdvisoryPriority = ShipPaPlaybackPolicy.AdvisoryPriority;
    public const int ImminentPriority = ShipPaPlaybackPolicy.ImminentPriority;

    private static readonly SoundSpecifier AdvisorySound =
        new SoundPathSpecifier("/Audio/_WF/Shuttles/Tcas/traffic_alarm.ogg");

    private static readonly SoundSpecifier ImminentSound =
        new SoundPathSpecifier("/Audio/_WF/Shuttles/Tcas/collision_warning.ogg");

    private static readonly SoundSpecifier VoiceSound =
        new SoundPathSpecifier("/Audio/_WF/Shuttles/Tcas/traffic.ogg");

    private bool _enabled;
    private float _lookahead;
    private float _imminentTime;
    private float _minimumClosingSpeed;
    private float _impactVelocity;
    private float _impactInertia;
    private float _dangerRadius;
    private float _inertiaScaling;
    private float _tileBreakEnergy;

    /// <summary>Mass the impact system is assumed to find around the contact, worked out from its own radius.</summary>
    private float _regionMass;
    private float _threatSpeedAllowance;
    private float _margin;
    private float _hysteresis;
    private float _calloutInterval;
    private float _updateInterval;

    private EntityQuery<MapGridComponent> _gridQuery;
    private EntityQuery<PhysicsComponent> _physicsQuery;
    private EntityQuery<TransformComponent> _xformQuery;

    /// <summary>Grids carrying a shuttle console, rebuilt each sweep; ships nobody flies are not warned.</summary>
    private readonly HashSet<EntityUid> _pilotedGrids = new();

    /// <summary>Reused per ship so a quiet sweep allocates nothing.</summary>
    private List<Entity<MapGridComponent>> _candidates = new();

    /// <summary>Union-find links between docked ships, rebuilt once per sweep from every docking port.</summary>
    private readonly Dictionary<EntityUid, EntityUid> _dockGroups = new();
    private readonly List<EntityUid> _stale = new();

    /// <summary>Hull bounds worked out this pass, so a grid seen by several ships is only measured once.</summary>
    private readonly Dictionary<EntityUid, Box2> _bounds = new();

    private TimeSpan _nextUpdate;

    public override void Initialize()
    {
        base.Initialize();

        PlatingMass = _proto.Index<ContentTileDefinition>(PlatingProto).Mass;

        _gridQuery = GetEntityQuery<MapGridComponent>();
        _physicsQuery = GetEntityQuery<PhysicsComponent>();
        _xformQuery = GetEntityQuery<TransformComponent>();

        Subs.CVar(_cfg, CollisionWarningCVars.Enabled, value => _enabled = value, true);
        Subs.CVar(_cfg, CollisionWarningCVars.Lookahead, value => _lookahead = value, true);
        Subs.CVar(_cfg, CollisionWarningCVars.ImminentTime, value => _imminentTime = value, true);
        Subs.CVar(_cfg, CollisionWarningCVars.MinimumClosingSpeed, value => _minimumClosingSpeed = value, true);
        Subs.CVar(_cfg, CollisionWarningCVars.Margin, value => _margin = value, true);

        // The warning is only worth giving for a hit the impact system would actually act on, so it
        // reads that system's own thresholds rather than keeping its own idea of a dangerous speed.
        Subs.CVar(_cfg, CCVars.MinimumImpactVelocity, value => _impactVelocity = value, true);
        Subs.CVar(_cfg, CCVars.MinimumImpactInertia, value => _impactInertia = value, true);
        Subs.CVar(_cfg, CollisionWarningCVars.DangerRadius, value => _dangerRadius = value, true);
        Subs.CVar(_cfg, CCVars.ImpactInertiaScaling, value => _inertiaScaling = value, true);
        Subs.CVar(_cfg, CCVars.TileBreakEnergyMultiplier, value => _tileBreakEnergy = value, true);
        Subs.CVar(_cfg, CCVars.ImpactRadius, value => _regionMass = RegionMass(value), true);
        Subs.CVar(_cfg, CollisionWarningCVars.ThreatSpeedAllowance, value => _threatSpeedAllowance = value, true);
        Subs.CVar(_cfg, CollisionWarningCVars.Hysteresis, value => _hysteresis = value, true);
        Subs.CVar(_cfg, CollisionWarningCVars.AdvisoryCalloutInterval, value => _calloutInterval = value, true);
        Subs.CVar(_cfg, CollisionWarningCVars.UpdateInterval, value => _updateInterval = value, true);

        SubscribeLocalEvent<CollisionWarningComponent, ComponentShutdown>(OnWarningShutdown);

        // ShuttleConsoleSystem owns the open and close subscriptions for these consoles; only the
        // toggle belongs here.
        Subs.BuiEvents<ShuttleConsoleComponent>(ShuttleConsoleUiKey.Key, subs =>
        {
            subs.Event<CollisionWarningToggleMessage>(OnToggle);
        });
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextUpdate)
            return;

        _nextUpdate = _timing.CurTime + TimeSpan.FromSeconds(_updateInterval);

        Sweep();
    }

    /// <summary>
    /// One pass over every piloted ship. Exposed so tests can drive a sweep without waiting on the tick.
    /// </summary>
    public void Sweep()
    {
        if (!_enabled)
        {
            ClearAll();
            return;
        }

        _bounds.Clear();
        CollectPilotedGrids();
        CollectDockGroups();

        // Every warning starts the pass unrenewed, and only a threat found below renews it. A ship that
        // drops out of the sweep entirely - console destroyed, system switched off - therefore cannot
        // leave a banner and an alarm running with nothing to stop them.
        var running = EntityQueryEnumerator<CollisionWarningComponent>();

        while (running.MoveNext(out _, out var warning))
        {
            warning.Threat = null;
        }

        var shuttles = EntityQueryEnumerator<ShuttleComponent, TransformComponent, PhysicsComponent>();

        while (shuttles.MoveNext(out var uid, out _, out var xform, out var physics))
        {
            if (!_pilotedGrids.Contains(uid) || !_gridQuery.TryComp(uid, out var grid))
                continue;

            // A ship with the warning switched off is left alone, warning and alarms included.
            if (HasComp<CollisionWarningDisabledComponent>(uid))
            {
                RemComp<CollisionWarningComponent>(uid);
                continue;
            }

            if (FindThreat((uid, grid, xform, physics)) is { } threat)
                Warn(uid, threat);
        }

        ExpireHeldWarnings();
    }

    /// <summary>Links every pair of ships joined by a docked port, so a sweep checks docked chains without walking them.</summary>
    private void CollectDockGroups()
    {
        _dockGroups.Clear();
        var docks = EntityQueryEnumerator<DockingComponent, TransformComponent>();
        while (docks.MoveNext(out var dock, out var xform))
        {
            if (!dock.Docked || dock.DockedWith is not { } with || !_xformQuery.TryComp(with, out var withXform))
                continue;

            var a = xform.ParentUid;
            var b = withXform.ParentUid;
            if (a == b || !HasComp<ShuttleComponent>(a) || !HasComp<ShuttleComponent>(b))
                continue;

            var rootA = DockGroup(a);
            var rootB = DockGroup(b);
            if (rootA != rootB)
                _dockGroups[rootB] = rootA;
        }
    }

    /// <summary>The ship a docked chain is filed under; a ship docked to nothing is its own.</summary>
    private EntityUid DockGroup(EntityUid ship)
    {
        while (_dockGroups.TryGetValue(ship, out var next))
            ship = next;
        return ship;
    }

    /// <summary>
    /// The first thing this ship is going to hit inside the lookahead window, if any.
    /// </summary>
    private Threat? FindThreat(Entity<MapGridComponent, TransformComponent, PhysicsComponent> ship)
    {
        var (uid, _, xform, physics) = ship;

        // Ships in FTL cannot hit anything, and the hyperspace map holds no traffic.
        if (HasComp<FTLComponent>(uid) || HasComp<Content.Server._WF.Planets.Hop.WFPlanetHopComponent>(uid) || xform.MapUid == null || HasComp<FTLMapComponent>(xform.MapUid))
            return null;

        var velocity = physics.LinearVelocity;
        var speed = velocity.Length();

        // A hull is taken as its bounding box. Rotation is left out of the prediction, so a ship that
        // only spins into another is not warned about.
        var ourBounds = GetBounds(uid, xform).Enlarged(_margin);

        // The reach has to cover traffic closing on us as well as our own run, or a parked ship never
        // sees the one bearing down on it. The allowance is what a threat is assumed to manage.
        _candidates.Clear();
        _mapManager.FindGridsIntersecting(xform.MapID,
            ourBounds.Enlarged((speed + _threatSpeedAllowance) * _lookahead),
            ref _candidates,
            approx: true,
            includeMap: false);

        if (_candidates.Count == 0)
            return null;

        // Docking is not a collision, and neither is a ship already tied to ours.
        var ourGroup = DockGroup(uid);

        var ourCentre = ourBounds.Center;
        Threat? best = null;

        foreach (var candidate in _candidates)
        {
            var other = candidate.Owner;

            if (other == uid || DockGroup(other) == ourGroup)
                continue;

            if (!_xformQuery.TryComp(other, out var otherXform))
                continue;

            _physicsQuery.TryComp(other, out var otherPhysics);
            var otherVelocity = otherPhysics?.LinearVelocity ?? Vector2.Zero;

            // How the other hull moves as this ship sees it.
            var approach = otherVelocity - velocity;
            var closingSpeed = approach.Length();

            if (closingSpeed < _minimumClosingSpeed || !WouldHurt(closingSpeed, physics, otherPhysics))
                continue;

            var otherBounds = GetBounds(other, otherXform);

            if (TimeToContact(ourBounds, otherBounds, approach) is not { } time)
                continue;

            if (best != null && time >= best.Value.Time)
                continue;

            best = new Threat(other, time, closingSpeed, otherBounds.Center - ourCentre);
        }

        return best;
    }

    /// <summary>
    /// Whether contact at this speed would be worth warning about. The impact system's own gate decides
    /// whether anything happens at all; how badly it goes is its collision energy, which is what sizes
    /// the hull it tears out. Predicting that energy and comparing it against the damage radius the
    /// crew care about discriminates a scrape from a crash far better than speed alone, because energy
    /// runs with the square of the closing speed.
    /// </summary>
    private bool WouldHurt(float closingSpeed, PhysicsComponent ours, PhysicsComponent? other)
    {
        var ourMass = ours.FixturesMass;
        var otherMass = other?.FixturesMass ?? 0f;

        // Reduced mass: what the collision actually has to work with. A hull with no body of its own is
        // immovable, which is the worst case for us.
        var effectiveMass = otherMass > 0f
            ? ourMass * otherMass / (ourMass + otherMass)
            : ourMass;

        // Nothing the impact system would ignore outright is ever worth a warning.
        if (closingSpeed < _impactVelocity && closingSpeed * effectiveMass < _impactInertia)
            return false;

        // The impact system's own energy, less the mass reductions it only knows at the contact point.
        var energy = _regionMass
            * (closingSpeed * closingSpeed / 2f)
            * MathF.Pow(effectiveMass / BaseShuttleMass, _inertiaScaling);

        // It spends that energy as a damage radius of sqrt(energy / tileBreak / platingMass), so this is
        // that relation turned around: the energy needed to reach the radius worth warning about.
        return energy >= _dangerRadius * _dangerRadius * _tileBreakEnergy * PlatingMass;
    }

    /// <summary>
    /// Mass of a solid plating disc the width of the impact system's radius. Real hulls are lighter than
    /// this, so the prediction errs towards warning.
    /// </summary>
    private static float RegionMass(float impactRadius)
    {
        return MathF.PI * impactRadius * impactRadius * PlatingMass;
    }

    /// <summary>
    /// Seconds until two boxes overlap, given how the second moves relative to the first, or null if
    /// they never do inside the lookahead window. Each axis gives the window it overlaps in, and
    /// contact is where those windows meet.
    /// </summary>
    private float? TimeToContact(Box2 ours, Box2 other, Vector2 approach)
    {
        var entry = float.NegativeInfinity;
        var exit = float.PositiveInfinity;

        for (var axis = 0; axis < 2; axis++)
        {
            var velocity = axis == 0 ? approach.X : approach.Y;
            var ourMin = axis == 0 ? ours.Left : ours.Bottom;
            var ourMax = axis == 0 ? ours.Right : ours.Top;
            var otherMin = axis == 0 ? other.Left : other.Bottom;
            var otherMax = axis == 0 ? other.Right : other.Top;

            float axisEntry;
            float axisExit;

            if (MathF.Abs(velocity) <= float.Epsilon)
            {
                // Nothing closes on this axis, so the boxes either already share it or never will.
                if (otherMax < ourMin || otherMin > ourMax)
                    return null;

                axisEntry = float.NegativeInfinity;
                axisExit = float.PositiveInfinity;
            }
            else if (velocity > 0f)
            {
                axisEntry = (ourMin - otherMax) / velocity;
                axisExit = (ourMax - otherMin) / velocity;
            }
            else
            {
                axisEntry = (ourMax - otherMin) / velocity;
                axisExit = (ourMin - otherMax) / velocity;
            }

            entry = MathF.Max(entry, axisEntry);
            exit = MathF.Min(exit, axisExit);
        }

        // Passing clear, already past, or further out than anyone needs warning of.
        if (entry > exit || exit < 0f || entry > _lookahead)
            return null;

        return MathF.Max(entry, 0f);
    }

    /// <summary>
    /// Raises or refreshes the warning on a ship, and matches the PA alarm to its stage.
    /// </summary>
    private void Warn(EntityUid uid, Threat threat)
    {
        var level = threat.Time <= _imminentTime ? CollisionWarningLevel.Imminent : CollisionWarningLevel.Advisory;
        var warning = EnsureComp<CollisionWarningComponent>(uid);
        var escalated = warning.Level != level;

        warning.Level = level;
        warning.ImpactTime = _timing.CurTime + TimeSpan.FromSeconds(threat.Time);
        warning.ThreatName = _shuttle.GetIFFLabel(threat.Grid);
        warning.Bearing = GetBearing(uid, threat.Offset);
        warning.ClosingSpeed = threat.ClosingSpeed;
        warning.ClearTime = _timing.CurTime + TimeSpan.FromSeconds(_hysteresis);
        warning.Threat = threat.Grid;
        Dirty(uid, warning);

        // The PA renders one foreground broadcast. The imminent asset contains both the
        // klaxon and spoken callout, so neither competes with (and silences) the other.
        if (level == CollisionWarningLevel.Advisory)
        {
            // An advisory is not an emergency, so the callout only comes round every few seconds.
            if (_timing.CurTime >= warning.NextCallout)
            {
                _pa.Broadcast(uid, VoiceSound, priority: ShipPaPlaybackPolicy.AdvisoryCalloutPriority, key: AdvisoryCallout);
                warning.NextCallout = _timing.CurTime + TimeSpan.FromSeconds(_calloutInterval);
            }
        }

        // Klaxons loop until stopped, so only a change of stage touches them.
        if (!escalated && (_pa.IsAlarmActive(uid, AdvisoryAlarm) || _pa.IsAlarmActive(uid, ImminentAlarm)))
            return;

        if (level == CollisionWarningLevel.Imminent)
        {
            _pa.StopAlarm(uid, AdvisoryAlarm);
            _pa.StopAlarm(uid, AdvisoryCallout);
            _pa.StartAlarm(uid, ImminentAlarm, ImminentSound,
                message: Loc.GetString("collision-warning-pa-imminent"), color: Color.Red,
                priority: ImminentPriority);
        }
        else
        {
            _pa.StopAlarm(uid, ImminentAlarm);
            _pa.StartAlarm(uid, AdvisoryAlarm, AdvisorySound,
                message: Loc.GetString("collision-warning-pa-advisory"), color: Color.Orange,
                priority: AdvisoryPriority);
        }
    }

    /// <summary>
    /// Drops warnings nothing renewed this pass. The hold is there to stop the banner strobing while a
    /// ship yaws on the edge of the cone; a klaxon has no such problem, so the noise stops at once and
    /// only the banner waits out the hold.
    /// </summary>
    private void ExpireHeldWarnings()
    {
        _stale.Clear();

        var warnings = EntityQueryEnumerator<CollisionWarningComponent>();

        while (warnings.MoveNext(out var uid, out var warning))
        {
            if (warning.Threat != null)
                continue;

            StopAlarms(uid);

            if (_timing.CurTime >= warning.ClearTime)
                _stale.Add(uid);
        }

        foreach (var uid in _stale)
        {
            RemComp<CollisionWarningComponent>(uid);
        }
    }

    private void ClearAll()
    {
        _stale.Clear();

        var warnings = EntityQueryEnumerator<CollisionWarningComponent>();

        while (warnings.MoveNext(out var uid, out _))
        {
            _stale.Add(uid);
        }

        foreach (var uid in _stale)
        {
            RemComp<CollisionWarningComponent>(uid);
        }
    }

    /// <summary>
    /// The pilot switched the warning on or off for the whole ship. Switching it off drops any warning
    /// already running, which takes the alarms with it.
    /// </summary>
    private void OnToggle(Entity<ShuttleConsoleComponent> ent, ref CollisionWarningToggleMessage args)
    {
        if (Transform(ent).GridUid is not { } grid)
            return;

        if (args.Enabled)
        {
            RemComp<CollisionWarningDisabledComponent>(grid);
        }
        else
        {
            EnsureComp<CollisionWarningDisabledComponent>(grid);
            RemComp<CollisionWarningComponent>(grid);
        }

        _popup.PopupEntity(
            Loc.GetString(args.Enabled ? "collision-warning-popup-on" : "collision-warning-popup-off"),
            ent,
            args.Actor);
    }

    /// <summary>
    /// The alarm belongs to the warning, so it stops with it however the warning ends.
    /// </summary>
    private void OnWarningShutdown(Entity<CollisionWarningComponent> ent, ref ComponentShutdown args)
    {
        StopAlarms(ent);
    }

    /// <summary>
    /// Silences everything this system runs on a ship's PA.
    /// </summary>
    private void StopAlarms(EntityUid uid)
    {
        _pa.StopAlarm(uid, AdvisoryAlarm);
        _pa.StopAlarm(uid, ImminentAlarm);
        _pa.StopAlarm(uid, AdvisoryCallout);
    }

    /// <summary>
    /// World bounds of a hull, worked out once per pass. One grid is usually a candidate for several
    /// ships, and the lookup behind this raises an event every time.
    /// </summary>
    private Box2 GetBounds(EntityUid uid, TransformComponent xform)
    {
        if (_bounds.TryGetValue(uid, out var bounds))
            return bounds;

        bounds = _lookup.GetWorldAABB(uid, xform);
        _bounds[uid] = bounds;

        return bounds;
    }

    private void CollectPilotedGrids()
    {
        _pilotedGrids.Clear();

        var consoles = EntityQueryEnumerator<ShuttleConsoleComponent, TransformComponent>();

        while (consoles.MoveNext(out _, out _, out var xform))
        {
            if (xform.GridUid is { } grid)
                _pilotedGrids.Add(grid);
        }
    }

    /// <summary>
    /// Bearing to the threat in degrees clockwise from the ship's nose.
    /// </summary>
    private float GetBearing(EntityUid uid, Vector2 offset)
    {
        var bearing = (new Angle(offset) - _xform.GetWorldRotation(uid)).Degrees;

        return (float) ((bearing % 360d + 360d) % 360d);
    }

    private readonly record struct Threat(EntityUid Grid, float Time, float ClosingSpeed, Vector2 Offset);
}
