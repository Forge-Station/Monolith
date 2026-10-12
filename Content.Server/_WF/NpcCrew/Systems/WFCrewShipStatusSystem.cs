using Content.Server._Mono.FireControl;
using Content.Server._WF.NpcCrew.Components;
using Content.Server.Power.EntitySystems;
using Content.Server.Shuttles.Components;
using Content.Shared._WF.NpcCrew;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WF.NpcCrew.Systems;

/// <summary>
/// Decides when a ship is out of the fight, and by each crewed ship's own rule when its crew stop attacking it.
/// Every ship once asked about is watched from then on, so how often anyone asks makes no difference.
/// </summary>
public sealed partial class WFCrewShipStatusSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private PowerReceiverSystem _power = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private Content.Server.Shuttles.Systems.DockingSystem _docking = default!;

    private readonly List<EntityUid> _boarded = new();

    /// <summary>How long a condition must hold without a break before it counts.</summary>
    public static readonly TimeSpan DisabledDelay = TimeSpan.FromSeconds(10);

    /// <summary>A ship with less than this share of the most thrust it was seen with cannot manoeuvre.</summary>
    private const float CrippledThrust = 0.25f;

    /// <summary>A hull with less than this share of the most tiles it was seen with is a wreck: shot to pieces or split apart.</summary>
    private const float WreckedHull = 0.6f;

    private static readonly TimeSpan CacheTime = TimeSpan.FromSeconds(2);
    // A gap this long between readings means the spell was not watched continuously.
    private static readonly TimeSpan ReadingGap = TimeSpan.FromSeconds(5);

    /// <summary>How many times its disengage range a deterring crew still answers a ship that fires on it.</summary>
    public const float ShooterReach = 3f;

    /// <summary>How often the watched ships are read.</summary>
    private static readonly TimeSpan WatchInterval = TimeSpan.FromSeconds(1);

    private readonly Dictionary<EntityUid, (TimeSpan Until, Reading Reading)> _cache = new();
    private readonly Dictionary<EntityUid, float> _peakThrust = new();
    private readonly Dictionary<EntityUid, int> _peakTiles = new();
    private readonly HashSet<EntityUid> _armed = new();
    private readonly HashSet<EntityUid> _crewed = new();
    private readonly Dictionary<(EntityUid Grid, Condition Condition), (TimeSpan Since, TimeSpan Last)> _spells = new();
    private readonly Dictionary<EntityUid, (WFCrewDisengage Rule, float Range)> _policies = new();

    private TimeSpan _nextWatch;
    private readonly HashSet<EntityUid> _watch = new();
    private readonly HashSet<EntityUid> _armedNow = new();
    private readonly HashSet<EntityUid> _crewedNow = new();
    private readonly HashSet<EntityUid> _served = new();
    private readonly HashSet<EntityUid> _gunned = new();

    private enum Condition : byte { Abandoned, Disarmed, Crippled, Wrecked }

    private readonly record struct Reading(bool Abandoned, bool Disarmed, bool Crippled, bool Wrecked);

    /// <summary>Sets when the crew of a ship stop attacking other ships.</summary>
    public void SetPolicy(EntityUid grid, WFCrewDisengage rule, float range)
    {
        _policies[grid] = (rule, range);
    }

    /// <summary>
    /// Whether the crew of <paramref name="grid"/> should leave <paramref name="target"/> alone under their rule.
    /// <paramref name="range"/> lets a deterring crew also give up on a ship that is far enough away; an Attack task
    /// passes false, because it starts out of range. A <paramref name="shooter"/> that is firing on the ship is
    /// answered from farther out, up to <see cref="ShooterReach"/> times the range, and not chased past that.
    /// </summary>
    public bool ShouldDisengage(EntityUid grid, EntityUid target, bool range = true, bool shooter = false)
    {
        var (rule, distance) = _policies.TryGetValue(grid, out var policy) ? policy : (WFCrewDisengage.Disable, 0f);
        var reading = Read(target);
        if (reading.Abandoned)
            return true;
        if (rule == WFCrewDisengage.Destroy)
            return false;
        if (reading.Disarmed || reading.Crippled || reading.Wrecked)
            return true;
        if (rule != WFCrewDisengage.Deter || !range && !shooter || TerminatingOrDeleted(grid) || TerminatingOrDeleted(target))
            return false;

        var here = _transform.GetMapCoordinates(grid);
        var there = _transform.GetMapCoordinates(target);
        var reach = range ? distance : distance * ShooterReach;
        return here.MapId != there.MapId || (here.Position - there.Position).LengthSquared() > reach * reach;
    }

    /// <summary>
    /// Whether a ship is out of the fight: for <see cref="DisabledDelay"/> without a break, everyone who was aboard
    /// is dead or gone, or it had ship weapons and none can be fired, or it has no weapon that can and under a
    /// quarter of its thrust, or under 60% of its hull is left. Each is judged against what the ship was seen with
    /// earlier, so stations, wrecks, asteroids and drones are never disabled for lacking what they never had.
    /// </summary>
    public bool IsDisabled(EntityUid grid)
    {
        var reading = Read(grid);
        return reading.Abandoned || reading.Disarmed || reading.Crippled || reading.Wrecked;
    }

    /// <summary>Whether a ship has no working thruster at all right now.</summary>
    public bool IsAdrift(EntityUid grid)
    {
        if (TerminatingOrDeleted(grid) || !TryComp<ShuttleComponent>(grid, out var shuttle))
            return false;

        foreach (var thrust in shuttle.LinearThrust)
        {
            if (thrust > 0f)
                return false;
        }

        return true;
    }

    private Reading Read(EntityUid grid)
    {
        var now = _timing.CurTime;
        if (_cache.TryGetValue(grid, out var cached) && now < cached.Until)
            return cached.Reading;

        var reading = !TerminatingOrDeleted(grid) && TryComp<ShuttleComponent>(grid, out var shuttle)
            ? Measure(grid, shuttle, CanShoot(grid), IsCrewed(grid), now)
            : default;
        _cache[grid] = (now + CacheTime, reading);
        return reading;
    }

    /// <summary>Takes a fresh reading of a ship from whether it can shoot and has anyone aboard right now.</summary>
    private Reading Measure(EntityUid grid, ShuttleComponent shuttle, bool armed, bool crewed, TimeSpan now)
    {
        var thrust = 0f;
        foreach (var direction in shuttle.LinearThrust)
        {
            thrust += direction;
        }

        var peak = _peakThrust.GetValueOrDefault(grid);
        if (thrust > peak)
            _peakThrust[grid] = peak = thrust;

        var tiles = TryComp<MapGridComponent>(grid, out var hull) ? _maps.GetFilledTileCount((grid, hull)) : 0;
        var peakTiles = _peakTiles.GetValueOrDefault(grid);
        if (tiles > peakTiles)
            _peakTiles[grid] = peakTiles = tiles;

        if (armed)
            _armed.Add(grid);

        if (crewed)
            _crewed.Add(grid);

        return new Reading(
            Sustained(grid, Condition.Abandoned, !crewed && _crewed.Contains(grid), now),
            Sustained(grid, Condition.Disarmed, !armed && _armed.Contains(grid), now),
            Sustained(grid, Condition.Crippled, !armed && peak > 0f && thrust < peak * CrippledThrust, now),
            Sustained(grid, Condition.Wrecked, peakTiles > 0 && tiles < peakTiles * WreckedHull, now));
    }

    /// <summary>Whether a condition has held, watched without a gap, for the whole delay.</summary>
    private bool Sustained(EntityUid grid, Condition condition, bool holds, TimeSpan now)
    {
        var key = (grid, condition);
        if (!holds)
        {
            _spells.Remove(key);
            return false;
        }

        var since = _spells.TryGetValue(key, out var spell) && now - spell.Last <= ReadingGap ? spell.Since : now;
        _spells[key] = (since, now);
        return now - since >= DisabledDelay;
    }

    /// <summary>
    /// Whether any ship weapon aboard can be fired: it is powered and registered with a gunnery server, or the ship
    /// has a living NPC gunner, who fires without one, or no gunnery server at all, as on a drone.
    /// </summary>
    private bool CanShoot(EntityUid grid)
    {
        bool? serverless = null;
        var weapons = EntityQueryEnumerator<FireControllableComponent, TransformComponent>();
        while (weapons.MoveNext(out var uid, out var weapon, out var xform))
        {
            if (xform.GridUid != grid || !_power.IsPowered(uid))
                continue;

            if (weapon.ControllingServer != null)
                return true;

            serverless ??= !HasServer(grid) || HasGunner(grid);
            if (serverless.Value)
                return true;
        }

        return false;
    }

    private bool HasServer(EntityUid grid)
    {
        var servers = EntityQueryEnumerator<FireControlServerComponent, TransformComponent>();
        while (servers.MoveNext(out _, out _, out var xform))
        {
            if (xform.GridUid == grid)
                return true;
        }

        return false;
    }

    private bool HasGunner(EntityUid grid)
    {
        var gunners = EntityQueryEnumerator<WFGunnerDutyComponent, WFCrewComponent, TransformComponent>();
        while (gunners.MoveNext(out var uid, out _, out var crew, out var xform))
        {
            if (xform.GridUid == grid && IsGunner(uid, crew))
                return true;
        }

        return false;
    }

    /// <summary>A living NPC on gunnery duty, who lays the guns without a gunnery server.</summary>
    private bool IsGunner(EntityUid uid, WFCrewComponent crew)
    {
        return crew.Duty == WFCrewDuties.Gunnery && _mobs.IsAlive(uid) && !HasComp<ActorComponent>(uid);
    }

    /// <summary>Whether a living player body or crew NPC is aboard.</summary>
    private bool IsCrewed(EntityUid grid)
    {
        var crew = EntityQueryEnumerator<WFCrewComponent, TransformComponent>();
        while (crew.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid == grid && _mobs.IsAlive(uid))
                return true;
        }

        var players = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (players.MoveNext(out var uid, out _, out var xform))
        {
            if (!_mobs.IsAlive(uid) || xform.GridUid is not { } aboard)
                continue;
            if (aboard == grid || _docking.AreGridsDocked(aboard, grid))
                return true;
        }

        return false;
    }

    /// <summary>A ship whose people have stepped across a port to the ship alongside is not abandoned.</summary>
    private void AddDocked(EntityUid grid, HashSet<EntityUid> crewed)
    {
        foreach (var dock in _docking.GetDocks(grid))
        {
            if (dock.Comp.DockedWith is { } other && Transform(other).GridUid is { } alongside)
                crewed.Add(alongside);
        }
    }

    /// <summary>Once a second: reads every ship seen working, crewed or armed, and drops the entries of deleted grids.</summary>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        if (now < _nextWatch)
            return;

        _nextWatch = now + WatchInterval;
        _watch.Clear();
        _watch.UnionWith(_peakThrust.Keys);
        _watch.UnionWith(_peakTiles.Keys);
        _watch.UnionWith(_armed);
        _watch.UnionWith(_crewed);
        if (_watch.Count > 0)
            Survey();
        foreach (var grid in _watch)
        {
            if (TerminatingOrDeleted(grid))
            {
                Forget(grid);
                continue;
            }

            if (TryComp<ShuttleComponent>(grid, out var shuttle))
                _cache[grid] = (now + CacheTime, Measure(grid, shuttle, _armedNow.Contains(grid), _crewedNow.Contains(grid), now));
        }

        foreach (var grid in _cache.Keys)
        {
            if (TerminatingOrDeleted(grid))
                _cache.Remove(grid);
        }
        foreach (var grid in _policies.Keys)
        {
            if (TerminatingOrDeleted(grid))
                _policies.Remove(grid);
        }
    }

    /// <summary>Which grids can shoot and which have anyone alive aboard, in one pass over each kind.</summary>
    private void Survey()
    {
        _served.Clear();
        var servers = EntityQueryEnumerator<FireControlServerComponent, TransformComponent>();
        while (servers.MoveNext(out _, out _, out var xform))
        {
            if (xform.GridUid is { } grid)
                _served.Add(grid);
        }

        _gunned.Clear();
        var gunners = EntityQueryEnumerator<WFGunnerDutyComponent, WFCrewComponent, TransformComponent>();
        while (gunners.MoveNext(out var uid, out _, out var crew, out var xform))
        {
            if (xform.GridUid is { } grid && IsGunner(uid, crew))
                _gunned.Add(grid);
        }

        _armedNow.Clear();
        var weapons = EntityQueryEnumerator<FireControllableComponent, TransformComponent>();
        while (weapons.MoveNext(out var uid, out var weapon, out var xform))
        {
            if (xform.GridUid is { } grid && !_armedNow.Contains(grid) && _power.IsPowered(uid)
                && (weapon.ControllingServer != null || !_served.Contains(grid) || _gunned.Contains(grid)))
                _armedNow.Add(grid);
        }

        _crewedNow.Clear();
        var crews = EntityQueryEnumerator<WFCrewComponent, TransformComponent>();
        while (crews.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid is { } grid && _mobs.IsAlive(uid))
                _crewedNow.Add(grid);
        }

        var players = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (players.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid is { } grid && _mobs.IsAlive(uid) && _crewedNow.Add(grid))
                _boarded.Add(grid);
        }

        foreach (var grid in _boarded)
        {
            AddDocked(grid, _crewedNow);
        }
        _boarded.Clear();
    }

    private void Forget(EntityUid grid)
    {
        _cache.Remove(grid);
        _peakThrust.Remove(grid);
        _peakTiles.Remove(grid);
        _armed.Remove(grid);
        _crewed.Remove(grid);
        _spells.Remove((grid, Condition.Abandoned));
        _spells.Remove((grid, Condition.Disarmed));
        _spells.Remove((grid, Condition.Crippled));
        _spells.Remove((grid, Condition.Wrecked));
    }
}
