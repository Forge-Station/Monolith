using System.Linq;
using System.Numerics;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.NpcCrew.Components;
using Content.Server.Administration.Managers;
using Content.Server.Shuttles.Events;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Encounters;
using Content.Shared._WF.NpcCrew;
using Content.Shared.Ghost;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Random;

namespace Content.Server._WF.Encounters.Systems;

/// <summary>
/// Simulated encounter ships. With nobody near, a ship is a plain stand-in entity that flies its orders as a point; a
/// player coming within range, or plotting a jump near it, has its hull spawned, and a real ship nobody has touched
/// goes back to simulation once everyone has long left it. Spawning and despawning go one ship at a time.
/// </summary>
public sealed partial class WFEncounterSystem
{
    [Dependency] private IAdminManager _admins = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;

    /// <summary>How long a plotted jump keeps the ships near its exit spawned.</summary>
    private static readonly TimeSpan JumpNotice = TimeSpan.FromSeconds(90);

    /// <summary>How near two simulated ships come for a fight between them to be rolled.</summary>
    private const float DuelRange = 2000f;


    /// <summary>How many times a spawned hull is moved on to clear a grid it landed on.</summary>
    private const int ClearAttempts = 8;

    private bool _simulate;
    private float _materializeRange;
    private float _dematerializeRange;
    private TimeSpan _dematerializeDelay;
    private TimeSpan _materializeInterval;
    private float _duelMean;
    private TimeSpan _nextStep;

    /// <summary>Ships waiting to be spawned (up) or put back into simulation (down), nearest players first.</summary>
    private readonly List<(EntityUid Encounter, string Key, bool Up)> _pending = new();

    /// <summary>Jump exits plotted lately, which spawn the ships about them before the jumper arrives.</summary>
    private readonly List<(MapCoordinates At, TimeSpan Until)> _jumps = new();

    /// <summary>Where living players and admin ghosts are, as of the last poll: whoever can see a ship.</summary>
    private readonly List<MapCoordinates> _observers = new();

    private List<Entity<MapGridComponent>> _overlaps = new();

    private void InitializeSimulation()
    {
        Subs.CVar(_config, EncountersCVars.Simulate, value => _simulate = value, true);
        Subs.CVar(_config, EncountersCVars.MaterializeRange, value => _materializeRange = value, true);
        Subs.CVar(_config, EncountersCVars.DematerializeRange, value => _dematerializeRange = value, true);
        Subs.CVar(_config, EncountersCVars.DematerializeDelay, value => _dematerializeDelay = TimeSpan.FromSeconds(value), true);
        Subs.CVar(_config, EncountersCVars.MaterializeInterval, value => _materializeInterval = TimeSpan.FromSeconds(value), true);
        Subs.CVar(_config, EncountersCVars.DuelMeanSeconds, value => _duelMean = MathF.Max(value, 1f), true);
        SubscribeLocalEvent<FTLStartedEvent>(OnFtlStarted);
    }

    /// <summary>A player's ship plotting a jump spawns the simulated ships about its exit while it is in transit.</summary>
    private void OnFtlStarted(ref FTLStartedEvent ev)
    {
        if (HasComp<WFEncounterGridComponent>(ev.Entity))
            return;

        _jumps.Add((_transform.ToMapCoordinates(ev.TargetCoordinates), _timing.CurTime + JumpNotice));
    }

    /// <summary>Puts a planned ship on the map as a simulated stand-in.</summary>
    private void SpawnProxy(Entity<WFEncounterComponent> encounter, WFEncounterShip ship, WFEncounterShipState state, MapCoordinates at)
    {
        // Made off the map and only then placed, never picked up by a grid it stands over: the hull it replaces, or
        // one it passes, would take it wherever that grid goes, deletion included.
        var proxy = Spawn(null, MapCoordinates.Nullspace);
        Transform(proxy).GridTraversal = false;
        if (_mapSystem.TryGetMap(at.MapId, out var map))
            _transform.SetCoordinates(proxy, new EntityCoordinates(map.Value, at.Position));
        var marker = AddComp<WFEncounterGridComponent>(proxy);
        marker.Encounter = encounter;
        marker.Key = ship.Key;
        var simulated = AddComp<WFCrewVirtualShipComponent>(proxy);
        if (ship.Navigation is { } navigation && _prototypes.TryIndex(navigation, out var profile) && profile.Settings.CruiseSpeed > 0f)
            simulated.Speed = profile.Settings.CruiseSpeed;
        simulated.Crew = state.Crew > 0 ? state.Crew : ship.Deckhands + ship.Guards + (ship.Captain ? 2 : 1);
        _meta.SetEntityName(proxy, state.Name);
        state.Grid = proxy;
        state.Virtual = true;
    }

    /// <summary>Whether two ships of encounters count as docked: hull to hull, or a simulated ship that reached the other.</summary>
    public bool AreDocked(WFEncounterShipState ship, WFEncounterShipState other)
    {
        if (TerminatingOrDeleted(ship.Grid) || TerminatingOrDeleted(other.Grid))
            return false;

        if (CompOrNull<WFCrewVirtualShipComponent>(ship.Grid)?.DockedTo == other.Grid
            || CompOrNull<WFCrewVirtualShipComponent>(other.Grid)?.DockedTo == ship.Grid
            || ship.RedockTo == other.Grid || other.RedockTo == ship.Grid)
            return true;

        return !ship.Virtual && !other.Virtual && _docking.AreGridsDocked(ship.Grid, other.Grid);
    }

    /// <summary>Spawns or despawns one queued ship, once the interval since the last has passed.</summary>
    private void StepSimulation()
    {
        // A ship being built takes a stage a tick, and holds the queue up until it is done.
        if (StepBuilding())
        {
            _nextStep = _timing.CurTime + _materializeInterval;
            return;
        }

        if (_pending.Count == 0 || _timing.CurTime < _nextStep)
            return;

        _nextStep = _timing.CurTime + _materializeInterval;
        var (uid, key, up) = _pending[0];
        _pending.RemoveAt(0);
        if (!TryComp<WFEncounterComponent>(uid, out var encounter) || !encounter.Ships.TryGetValue(key, out var state)
            || TerminatingOrDeleted(state.Grid) || state.Virtual != up)
            return;

        if (up)
            Materialize((uid, encounter), key, state);
        else
            Dematerialize((uid, encounter), key, state);
    }

    /// <summary>
    /// Each poll: finds who is about, queues the simulated ships near them to spawn and the untouched real ships long
    /// left alone to go back, and rolls the simulated fights.
    /// </summary>
    private void PlanSimulation()
    {
        var now = _timing.CurTime;
        _jumps.RemoveAll(jump => jump.Until <= now);
        FindObservers();
        var spawn = new List<(EntityUid Encounter, string Key, float Distance)>();
        var query = EntityQueryEnumerator<WFEncounterComponent>();
        while (query.MoveNext(out var uid, out var encounter))
        {
            foreach (var (key, ship) in encounter.Ships)
            {
                if (TerminatingOrDeleted(ship.Grid) || ship.Building)
                    continue;

                // Back alongside for real, or the ship it was to dock with gone: no longer counted docked meanwhile.
                if (ship.RedockTo is { } redock && (TerminatingOrDeleted(redock)
                    || !ship.Virtual && !HasComp<WFCrewVirtualShipComponent>(redock) && _docking.AreGridsDocked(ship.Grid, redock)))
                    ship.RedockTo = null;

                var at = _transform.GetMapCoordinates(ship.Grid);
                var nearest = Nearest(at);
                if (ship.Virtual)
                {
                    // Turned off, every simulated ship is spawned; a resolved encounter's stand-ins just wait for cleanup.
                    if (encounter.Resolution == null && (!_simulate || nearest <= _materializeRange || JumpingNear(at)))
                        spawn.Add((uid, key, _simulate ? nearest : 0f));
                    continue;
                }

                if (!CanSimulate(encounter, ship) || nearest < _dematerializeRange || JumpingNear(at))
                {
                    ship.AloneSince = null;
                    continue;
                }

                ship.AloneSince ??= now;
                if (now - ship.AloneSince >= _dematerializeDelay && !Queued(uid, key))
                    _pending.Add((uid, key, false));
            }
        }

        // The nearest first, ahead of anything going back.
        foreach (var (uid, key, _) in spawn.OrderByDescending(entry => entry.Distance))
        {
            if (Queued(uid, key))
                continue;

            _pending.Insert(0, (uid, key, true));
        }

        RollDuels();
    }

    private bool Queued(EntityUid encounter, string key)
    {
        return _pending.Any(entry => entry.Encounter == encounter && entry.Key == key);
    }

    /// <summary>Distance to the nearest observer on the same map, or infinity.</summary>
    private float Nearest(MapCoordinates at)
    {
        var best = float.PositiveInfinity;
        foreach (var observer in _observers)
        {
            if (observer.MapId == at.MapId)
                best = MathF.Min(best, (observer.Position - at.Position).Length());
        }

        return best;
    }

    private bool JumpingNear(MapCoordinates at)
    {
        return _jumps.Any(jump => jump.At.MapId == at.MapId
                                  && (jump.At.Position - at.Position).LengthSquared() <= _materializeRange * _materializeRange);
    }

    /// <summary>Living players, then admins' ghosts: a dead player's ghost spawns nothing, an admin watching does.</summary>
    private void FindObservers()
    {
        _observers.Clear();
        _observers.AddRange(_players);
        var ghosts = EntityQueryEnumerator<ActorComponent, GhostComponent, TransformComponent>();
        while (ghosts.MoveNext(out var uid, out var actor, out _, out var xform))
        {
            if (_admins.IsAdmin(actor.PlayerSession))
                _observers.Add(_transform.GetMapCoordinates(uid, xform));
        }
    }

    /// <summary>
    /// Whether a real ship may go back to simulation: its encounter running and not an admin's, nobody having boarded,
    /// docked with or fought it, its crew all alive and not alerted, and docked with nothing but other encounter ships.
    /// </summary>
    private bool CanSimulate(WFEncounterComponent encounter, WFEncounterShipState ship)
    {
        if (!_simulate || !encounter.Simulated || encounter.Resolution != null || encounter.Pinned || ship.Touched || ship.Derelict
            || ship.Building)
            return false;

        if (PlayersAboard(ship.Grid) || DockedWithPlayerShip(ship.Grid)
            || encounter.ShipHits.Keys.Any(hit => hit.Side == ship.Side) || encounter.Kills.Count > 0)
        {
            ship.Touched = true;
            return false;
        }

        if (_alerts.IsAlerted(ship.Grid, ship.Group) || LivingCrew(ship) < ship.Crew)
            return false;

        foreach (var dock in _docking.GetDocks(ship.Grid))
        {
            if (dock.Comp.DockedWith is { } other && Transform(other).GridUid is { } docked && !HasComp<WFEncounterGridComponent>(docked))
                return false;
        }

        return true;
    }

    private int LivingCrew(WFEncounterShipState ship)
    {
        var count = 0;
        var crew = EntityQueryEnumerator<WFCrewComponent>();
        while (crew.MoveNext(out var uid, out var member))
        {
            if (member.Group == ship.Group && _mobs.IsAlive(uid))
                count++;
        }

        return count;
    }

    /// <summary>
    /// Starts building a simulated ship's hull where its stand-in is, over the ticks that follow: see
    /// <c>WFEncounterSystem.Build.cs</c>.
    /// </summary>
    private void Materialize(Entity<WFEncounterComponent> encounter, string key, WFEncounterShipState state)
    {
        if (!_prototypes.TryIndex(encounter.Comp.Prototype, out var prototype)
            || prototype.Ships.FirstOrDefault(ship => ship.Key == key) is not { } ship)
            return;

        var proxy = state.Grid;
        WFEncounterManifestPrototype? manifest = null;
        if (encounter.Comp.Manifest is { } manifestId)
            _prototypes.TryIndex(manifestId, out manifest);

        _building = new ShipBuild
        {
            Encounter = encounter,
            Ship = ship,
            State = state,
            At = _transform.GetMapCoordinates(proxy),
            Manifest = manifest,
            Proxy = proxy,
            Docked = CompOrNull<WFCrewVirtualShipComponent>(proxy)?.DockedTo,
        };
        // Left alone while it is built: not judged, despawned or fought.
        state.Building = true;
        Log.Info($"Encounter {prototype.ID} spawning simulated ship '{key}'.");
        StepBuilding();
    }

    /// <summary>Puts an untouched real ship back into simulation: a stand-in takes its place, orders and docking, and the hull and crew go.</summary>
    private void Dematerialize(Entity<WFEncounterComponent> encounter, string key, WFEncounterShipState state)
    {
        if (!_prototypes.TryIndex(encounter.Comp.Prototype, out var prototype)
            || prototype.Ships.FirstOrDefault(ship => ship.Key == key) is not { } ship || !CanSimulate(encounter.Comp, state))
            return;

        var grid = state.Grid;
        EntityUid? partner = null;
        foreach (var dock in _docking.GetDocks(grid))
        {
            if (dock.Comp.DockedWith is { } other && Transform(other).GridUid is { } docked && docked != grid)
                partner = docked;
        }

        var at = new MapCoordinates(_objectives.ShipCentre(grid), Transform(grid).MapID);
        SpawnProxy(encounter, ship, state, at);
        var proxy = state.Grid;
        Comp<WFCrewVirtualShipComponent>(proxy).DockedTo = partner ?? state.RedockTo;
        state.RedockTo = null;
        state.AloneSince = null;
        _objectives.MoveQueue(grid, proxy, state.Group);
        _objectives.Retarget(grid, proxy);
        RepointDocks(grid, proxy);
        _setup.ClearCrew(grid, state.Group, keepCorpses: false);
        _lifecycle.UnparentPlayersFromGrid(grid, true);
        Log.Info($"Encounter {prototype.ID} ship '{key}' went back to simulation.");
    }

    /// <summary>Simulated ships counted as docked with one ship are counted as docked with what replaced it.</summary>
    private void RepointDocks(EntityUid from, EntityUid to)
    {
        var query = EntityQueryEnumerator<WFCrewVirtualShipComponent>();
        while (query.MoveNext(out _, out var simulated))
        {
            if (simulated.DockedTo == from)
                simulated.DockedTo = to;
        }

        var encounters = EntityQueryEnumerator<WFEncounterComponent>();
        while (encounters.MoveNext(out _, out var encounter))
        {
            foreach (var ship in encounter.Ships.Values)
            {
                if (ship.RedockTo == from)
                    ship.RedockTo = to;
            }
        }
    }

    /// <summary>
    /// Moves a freshly spawned hull off any grid it landed on: away from the ship it was docked with, else away from
    /// what it overlaps, a hull's width at a time.
    /// </summary>
    private void Clear(EntityUid grid, EntityUid? partner)
    {
        var xform = Transform(grid);
        for (var i = 0; i < ClearAttempts; i++)
        {
            var bounds = _lookup.GetWorldAABB(grid);
            _overlaps.Clear();
            _mapSystem.FindGridsIntersecting(xform.MapID, bounds.Enlarged(10f), ref _overlaps, includeMap: false);
            var other = _overlaps.FirstOrDefault(found => found.Owner != grid);
            if (other.Owner == EntityUid.Invalid)
                return;

            var from = partner is { } docked && !TerminatingOrDeleted(docked) ? _objectives.ShipCentre(docked) : _lookup.GetWorldAABB(other.Owner).Center;
            var away = bounds.Center - from;
            var direction = away.LengthSquared() > 0.01f ? Vector2.Normalize(away) : Vector2.UnitY;
            var step = MathF.Max(bounds.Width, bounds.Height) + 20f;
            _transform.SetWorldPosition(grid, _transform.GetWorldPosition(xform) + direction * step);
        }
    }

    /// <summary>
    /// Rolls the fights between simulated ships nobody is watching: a ship whose order is to attack a simulated ship
    /// within range fights it out, and each poll one of the pair may be lost, the weaker the likelier.
    /// </summary>
    private void RollDuels()
    {
        var seen = new HashSet<(EntityUid, EntityUid)>();
        var crews = new Dictionary<EntityUid, WFEncounterShipState>();
        var query = EntityQueryEnumerator<WFEncounterComponent>();
        while (query.MoveNext(out _, out var encounter))
        {
            foreach (var ship in encounter.Ships.Values)
            {
                if (ship.Virtual && !ship.Building && !TerminatingOrDeleted(ship.Grid))
                    crews[ship.Grid] = ship;
            }
        }

        foreach (var (proxy, ship) in crews)
        {
            if (_objectives.AttackTarget(proxy, ship.Group, simulated: true) is not { } target
                || !crews.TryGetValue(target, out var enemy))
                continue;

            var pair = proxy.Id < target.Id ? (proxy, target) : (target, proxy);
            if (!seen.Add(pair)
                || (_objectives.ShipCentre(proxy) - _objectives.ShipCentre(target)).LengthSquared() > DuelRange * DuelRange)
                continue;

            // Each round of the fight lands on both, as hits would: a stronghold under fire answers as it does to real ones.
            var struck = new WFEncounterSimulatedHitEvent(target, proxy);
            RaiseLocalEvent(ref struck);
            var answered = new WFEncounterSimulatedHitEvent(proxy, target);
            RaiseLocalEvent(ref answered);
            if (!_random.Prob(MathF.Min(1f, (float) PollInterval.TotalSeconds / _duelMean)))
                continue;

            var mine = Strength(ship);
            var theirs = Strength(enemy);
            var loser = _random.Prob(theirs / (mine + theirs)) ? ship : enemy;
            Log.Info($"Simulated fight: {MetaData(ship.Grid).EntityName} against {MetaData(enemy.Grid).EntityName}; {MetaData(loser.Grid).EntityName} is lost.");
            RemoveShip(loser);
        }
    }

    /// <summary>A simulated ship's weight in a fight: its hull's worth, by its crew's skill, less for a civilian hull the raiders armed.</summary>
    private float Strength(WFEncounterShipState ship)
    {
        var worth = ship.Vessel is { } id && _prototypes.TryIndex(id, out var vessel) ? MathF.Max(vessel.Price, 20000) / 1000f : 50f;
        var skill = ship.Skill switch
        {
            WFCrewSkill.Green => 0.6f,
            WFCrewSkill.Veteran => 1.4f,
            _ => 1f,
        };
        var refit = ship.Retrofit is { } refitId && _prototypes.TryIndex(refitId, out var retrofit) && retrofit.Guns.Count > 0 ? 0.8f : 1f;
        return worth * skill * refit;
    }
}
