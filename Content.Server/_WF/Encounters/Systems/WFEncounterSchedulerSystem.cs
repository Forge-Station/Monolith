using System.Linq;
using System.Numerics;
using Content.Server._Mono.AlertLevel;
using Content.Server._NF.GameTicking.Events;
using Content.Server.GameTicking;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Encounters;
using Content.Shared.GameTicking;
using Content.Shared.Ghost;
using Content.Shared.Mobs.Systems;
using Content.Shared.Station.Components;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.Encounters.Systems;

/// <summary>
/// The storyteller. Works to the round's preset: places round-start encounters once the stations exist, then at a
/// random interval starts a weighted encounter that fits the budget, the player count and its cooldown, where its
/// placement says. It may bring back a replaceable round-start encounter that is gone.
/// </summary>
public sealed partial class WFEncounterSchedulerSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private IMapManager _maps = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private WFEncounterSystem _encounters = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WarLevelSystem _warLevel = default!;

    /// <summary>Space that has to be free of other grids around an open-space origin.</summary>
    private const float Clearance = 600f;
    private const int PlacementTries = 8;

    /// <summary>Room for a hull beyond its offset, kept free of other grids around a station-placed origin.</summary>
    private const float HullClearance = 150f;

    /// <summary>A station placement prefers a station with no encounter ship within this distance, so two don't share one.</summary>
    private const float StationSpacing = 2000f;

    /// <summary>How soon the storyteller tries again after an attempt that started nothing.</summary>
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(60);

    /// <summary>Round start is tried this often for its first few tries, then every <see cref="RetryDelay"/>.</summary>
    private static readonly TimeSpan RoundStartRetry = TimeSpan.FromSeconds(10);
    private const int RoundStartQuickTries = 6;

    private bool _enabled;
    private float _intervalMin;
    private float _intervalMax;
    private int _maxActive;
    private string _presetId = string.Empty;
    private TimeSpan? _next;
    private bool _roundStartDue;
    private int _roundStartTries;
    private int _roundStartPlaced;
    private TimeSpan _roundStartRetry;
    private readonly Dictionary<string, TimeSpan> _lastStarted = new();
    private readonly HashSet<string> _startedAtRoundStart = new();
    private List<Entity<MapGridComponent>> _nearby = new();

    /// <summary>Stops the scheduler until resumed, whatever the cvar says.</summary>
    public bool Paused;

    /// <summary>When the scheduler next tries, while it is running.</summary>
    public TimeSpan? Next => _next;

    /// <summary>The preset the storyteller works to, or null if the configured one does not exist.</summary>
    public WFEncounterPresetPrototype? Preset => _prototypes.TryIndex<WFEncounterPresetPrototype>(_presetId, out var preset) ? preset : null;

    public override void Initialize()
    {
        base.Initialize();
        Subs.CVar(_config, EncountersCVars.Enabled, value => _enabled = value, true);
        Subs.CVar(_config, EncountersCVars.IntervalMin, value => _intervalMin = value, true);
        Subs.CVar(_config, EncountersCVars.IntervalMax, value => _intervalMax = value, true);
        Subs.CVar(_config, EncountersCVars.MaxActive, value => _maxActive = value, true);
        Subs.CVar(_config, EncountersCVars.Preset, value => _presetId = value, true);
        Subs.CVar(_config, EncountersCVars.HiddenStations, value =>
        {
            _hidden.Clear();
            foreach (var fragment in value.Split(','))
            {
                if (fragment.Trim() is { Length: > 0 } name)
                    _hidden.Add(name);
            }
        }, true);
        Subs.CVar(_config, EncountersCVars.HiddenClearance, value => _hiddenClearance = value, true);
        SubscribeLocalEvent<StationsGeneratedEvent>(OnStationsGenerated);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundCleanup);
    }

    private void OnRoundCleanup(RoundRestartCleanupEvent args)
    {
        _lastStarted.Clear();
        _startedAtRoundStart.Clear();
        _next = null;
        _roundStartDue = false;
        _roundStartPlaced = 0;
    }

    /// <summary>
    /// Round-start encounters go in once the sector's stations exist and the round is running: while the map is
    /// still being set up its grids are paused and cannot be found.
    /// </summary>
    private void OnStationsGenerated(StationsGeneratedEvent args)
    {
        _roundStartDue = true;
        _roundStartTries = 0;
        _roundStartPlaced = 0;
        _roundStartRetry = TimeSpan.Zero;
    }

    /// <summary>Places the preset's share of round-start encounters. Returns how many started.</summary>
    public int StartRound()
    {
        return PlaceRoundStart(Preset?.RoundStart ?? 1, out _);
    }

    /// <summary>Places up to <paramref name="limit"/> round-start encounters; <paramref name="blocked"/> if one found no place.</summary>
    private int PlaceRoundStart(int limit, out bool blocked)
    {
        blocked = false;
        var started = 0;
        var unplaceable = new HashSet<string>();
        while (started < limit && Pick(WFEncounterStart.RoundStart, unplaceable) is { } prototype)
        {
            if (!TryStart(prototype, out _))
            {
                unplaceable.Add(prototype.ID);
                blocked = true;
                continue;
            }

            _lastStarted[prototype.ID] = _timing.CurTime;
            _startedAtRoundStart.Add(prototype.ID);
            started++;
        }

        return started;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (!_enabled || Paused || _ticker.RunLevel != GameRunLevel.InRound)
        {
            _next = null;
            return;
        }

        // Round start keeps trying, more slowly after the first few tries, until the preset's share is placed or
        // nothing is left to place.
        if (_roundStartDue && _timing.CurTime >= _roundStartRetry)
        {
            _roundStartTries++;
            _roundStartRetry = _timing.CurTime + (_roundStartTries < RoundStartQuickTries ? RoundStartRetry : RetryDelay);
            var limit = Preset?.RoundStart ?? 1;
            var started = PlaceRoundStart(limit - _roundStartPlaced, out var blocked);
            _roundStartPlaced += started;
            if (_roundStartPlaced >= limit || (started == 0 && !blocked && _roundStartPlaced > 0))
                _roundStartDue = false;
        }

        _next ??= _timing.CurTime + Interval();
        if (_timing.CurTime < _next)
            return;

        _next = _timing.CurTime + Interval();
        // An attempt that started nothing tries again soon, not a whole interval later.
        if (!TrySchedule(out _))
            _next = _timing.CurTime + RetryDelay;
    }

    private TimeSpan Interval()
    {
        var scale = MathF.Max(0.1f, Preset?.IntervalScale ?? 1f);
        return TimeSpan.FromSeconds(_random.NextFloat(_intervalMin, MathF.Max(_intervalMin, _intervalMax)) * scale);
    }

    /// <summary>The budget for running encounters: the preset's, grown by the player count.</summary>
    public int Budget()
    {
        return Preset is { } preset
            ? preset.Budget + (int) (preset.BudgetPerPlayer * _playerManager.PlayerCount)
            : int.MaxValue;
    }

    /// <summary>Picks and starts one mid-round encounter now, if the caps, a candidate and a place allow it.</summary>
    public bool TrySchedule(out EntityUid encounter)
    {
        encounter = default;
        if (_encounters.ActiveCount() >= _maxActive)
        {
            Log.Debug("No encounter scheduled: the cap is reached.");
            return false;
        }

        // A pick that has no place right now is passed over without using up its cooldown.
        var unplaceable = new HashSet<string>();
        while (Pick(WFEncounterStart.Scheduled, unplaceable) is { } prototype)
        {
            if (!TryStart(prototype, out encounter))
            {
                unplaceable.Add(prototype.ID);
                continue;
            }

            _lastStarted[prototype.ID] = _timing.CurTime;
            return true;
        }

        Log.Debug("No encounter scheduled: nothing fits the budget, the player count, the cooldowns and the space.");
        return false;
    }

    /// <summary>
    /// Starts an encounter for an admin. Open-space encounters go where asked; the others are placed beside
    /// stations on that map as usual, because their orders are aimed at those stations. Pinned, it stays until an
    /// admin ends it; otherwise it jumps out and is cleaned up like a scheduled one.
    /// </summary>
    public bool TryStartAt(WFEncounterPrototype prototype, MapCoordinates near, out EntityUid encounter, EntityUid? spawner, bool pinned = true)
    {
        encounter = default;
        if (prototype.Placement == WFEncounterPlacement.OpenSpace)
            return _encounters.TrySpawn(prototype, near, out encounter, spawner, pinned: pinned);

        return TryPlaceAtStations(prototype, near.MapId, out var origin, out var stops)
            && _encounters.TrySpawn(prototype, origin, out encounter, spawner, stops, pinned);
    }

    private bool TryStart(WFEncounterPrototype prototype, out EntityUid encounter)
    {
        encounter = default;
        if (!TryPlace(prototype, out var origin, out var stops))
        {
            Log.Info($"Encounter {prototype.ID} not started: no place for it.");
            return false;
        }

        return _encounters.TrySpawn(prototype, origin, out encounter, null, stops);
    }

    /// <summary>A weighted pick among the prototypes of a start kind that fit right now.</summary>
    private WFEncounterPrototype? Pick(WFEncounterStart start, HashSet<string> skip)
    {
        var players = _playerManager.PlayerCount;
        var preset = Preset;
        var room = Budget() - _encounters.ActiveCost();
        var atWar = _warLevel.GetWarLevel(EntityUid.Invalid);
        var candidates = new List<(WFEncounterPrototype Prototype, float Weight)>();
        var total = 0f;
        foreach (var prototype in _prototypes.EnumeratePrototypes<WFEncounterPrototype>())
        {
            // Mid-round the storyteller may also bring a replaceable round-start encounter that is gone, or that
            // round start never placed once it has stopped trying.
            var fits = prototype.Start == start
                || start == WFEncounterStart.Scheduled && prototype.Start == WFEncounterStart.RoundStart
                    && prototype.Replaceable && (!_roundStartDue || _startedAtRoundStart.Contains(prototype.ID));
            var weight = prototype.Weight * (preset != null && preset.Weights.TryGetValue(prototype.Category, out var scale) ? scale : 1f);
            // A round-start or round-long encounter takes nothing from the budget, so it needs no room in it.
            var cost = prototype.Lifetime == WFEncounterLifetime.Persistent || prototype.Start == WFEncounterStart.RoundStart
                ? 0
                : prototype.Cost;
            // One of a kind at a time: an encounter that is still running is not picked again.
            if (!fits || weight <= 0f || players < prototype.MinPlayers || cost > Math.Max(0, room) || skip.Contains(prototype.ID)
                || prototype.RequiresWar && !atWar
                || _encounters.IsRunning(prototype.ID)
                || _lastStarted.TryGetValue(prototype.ID, out var last)
                    && _timing.CurTime - last < TimeSpan.FromSeconds(prototype.Cooldown))
                continue;

            candidates.Add((prototype, weight));
            total += weight;
        }

        if (candidates.Count == 0)
            return null;

        var roll = _random.NextFloat(total);
        foreach (var (prototype, weight) in candidates)
        {
            roll -= weight;
            if (roll <= 0f)
                return prototype;
        }

        return candidates[^1].Prototype;
    }

    private bool TryPlace(WFEncounterPrototype prototype, out MapCoordinates origin, out List<EntityUid> stops)
    {
        stops = new List<EntityUid>();
        if (prototype.Placement == WFEncounterPlacement.OpenSpace)
            return TryPlaceInOpenSpace(prototype, out origin);

        return TryPlaceAtStations(prototype, _ticker.DefaultMap, out origin, out stops);
    }

    /// <summary>
    /// Chooses the stations an encounter is placed by and flies to, and its origin: beside the first for a
    /// station or a route, out at the approach distance from the first for a circuit.
    /// </summary>
    private bool TryPlaceAtStations(WFEncounterPrototype prototype, MapId map, out MapCoordinates origin, out List<EntityUid> stops)
    {
        stops = new List<EntityUid>();
        origin = MapCoordinates.Nullspace;
        var stations = Stations(map);
        // Never beside or bound for an enemy's home port.
        if (prototype.AvoidStations.Count > 0)
            stations.RemoveAll(station => Avoided(station, prototype.AvoidStations));

        var wanted = prototype.Placement switch
        {
            WFEncounterPlacement.Route => 2,
            WFEncounterPlacement.Circuit when prototype.Route is { } circuit =>
                Math.Clamp(_random.Next(circuit.MinStops, Math.Max(circuit.MinStops, circuit.MaxStops) + 1), 1, Math.Max(1, stations.Count)),
            _ => 1,
        };
        if (stations.Count < wanted || prototype.Placement == WFEncounterPlacement.Route && stations.Count < 2)
            return false;

        // The first stop is picked at random; each next one is the nearest station not yet on the route, so a
        // haul works its way across the sector and doesn't zigzag. A station placement keeps off a station that already has
        // an encounter ship about, unless every station has one.
        Entity<MapGridComponent> first;
        if (prototype.Placement == WFEncounterPlacement.Station && StationsWithoutShips(stations) is { Count: > 0 } free)
        {
            first = _random.Pick(free);
            stations.Remove(first);
        }
        else
        {
            first = _random.PickAndTake(stations);
        }

        stops.Add(first);
        var from = _transform.GetWorldPosition(first);
        while (stops.Count < wanted)
        {
            var next = stations.MinBy(station => (_transform.GetWorldPosition(station) - from).LengthSquared());
            stations.Remove(next);
            stops.Add(next);
            from = _transform.GetWorldPosition(next);
        }

        // The ships load where they are told, so their spot must be clear of anything but the first stop.
        var centre = _transform.GetMapCoordinates(first);
        var room = HullClearance;
        foreach (var ship in prototype.Ships)
        {
            room = MathF.Max(room, ship.Offset.Length() + HullClearance);
        }

        for (var i = 0; i < PlacementTries; i++)
        {
            var reach = prototype.Placement == WFEncounterPlacement.Circuit && prototype.Route is { } route
                ? _random.NextFloat(route.ApproachMin, MathF.Max(route.ApproachMin, route.ApproachMax))
                : first.Comp.LocalAABB.Size.Length() / 2f + prototype.Standoff;
            var point = centre.Position + _random.NextAngle().ToVec() * reach;
            _nearby.Clear();
            _maps.FindGridsIntersecting(centre.MapId, Box2.CenteredAround(point, new Vector2(room * 2f)), ref _nearby, approx: true, includeMap: false);
            if (_nearby.Any(grid => grid.Owner != first.Owner))
                continue;

            origin = new MapCoordinates(point, centre.MapId);
            return true;
        }

        return false;
    }

    /// <summary>
    /// The stations and outposts of a map: station grids with a docking port that nobody holds a deed to. A map with fewer than two, such as
    /// the development map, is topped up with its other unowned grids that have a port, so station encounters can still be tried.
    /// </summary>
    /// <summary>Stations kept secret: never a stop or a berth, and open space near them is never used.</summary>
    private readonly List<string> _hidden = new();
    private float _hiddenClearance;

    private List<Entity<MapGridComponent>> Stations(MapId map)
    {
        var stations = new List<Entity<MapGridComponent>>();
        var others = new List<Entity<MapGridComponent>>();
        var ported = new HashSet<EntityUid>();
        var docks = EntityQueryEnumerator<Content.Server.Shuttles.Components.DockingComponent, TransformComponent>();
        while (docks.MoveNext(out _, out _, out var dockXform))
        {
            if (dockXform.GridUid is { } dockGrid)
                ported.Add(dockGrid);
        }

        var query = EntityQueryEnumerator<MapGridComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var grid, out var xform))
        {
            // The map itself can be a grid on planet maps; an encounter never flies to it.
            if (xform.MapID != map || uid == xform.MapUid || HasComp<ShuttleDeedComponent>(uid)
                || HasComp<Components.WFEncounterGridComponent>(uid))
                continue;

            // Asteroid clusters and the like are stations too, but have no port to dock at; nor do debris and wrecks.
            if (!ported.Contains(uid))
                continue;

            if (_hidden.Count > 0 && Avoided(uid, _hidden))
                continue;

            if (HasComp<StationMemberComponent>(uid))
                stations.Add((uid, grid));
            else
                others.Add((uid, grid));
        }

        if (stations.Count < 2)
        {
            others.Sort((a, b) => b.Comp.LocalAABB.Size.LengthSquared().CompareTo(a.Comp.LocalAABB.Size.LengthSquared()));
            stations.AddRange(others.Take(2 - stations.Count));
        }

        return stations;
    }

    /// <summary>The stations with no encounter ship within <see cref="StationSpacing"/> of them.</summary>
    private List<Entity<MapGridComponent>> StationsWithoutShips(List<Entity<MapGridComponent>> stations)
    {
        var ships = new List<MapCoordinates>();
        var query = EntityQueryEnumerator<Components.WFEncounterGridComponent>();
        while (query.MoveNext(out var grid, out _))
        {
            ships.Add(_transform.GetMapCoordinates(grid));
        }

        return stations.Where(station =>
        {
            var at = _transform.GetMapCoordinates(station);
            return !ships.Any(other => other.MapId == at.MapId
                && (other.Position - at.Position).LengthSquared() < StationSpacing * StationSpacing);
        }).ToList();
    }

    /// <summary>Whether a station's grid name, station name or station id contains any of the given fragments.</summary>
    private bool Avoided(EntityUid station, List<string> fragments)
    {
        var name = Name(station);
        var id = CompOrNull<Content.Server.Station.Components.BecomesStationComponent>(station)?.Id ?? string.Empty;
        var owner = TryComp<StationMemberComponent>(station, out var member) && !TerminatingOrDeleted(member.Station)
            ? Name(member.Station)
            : string.Empty;
        foreach (var fragment in fragments)
        {
            if (name.Contains(fragment, StringComparison.OrdinalIgnoreCase)
                || id.Contains(fragment, StringComparison.OrdinalIgnoreCase)
                || owner.Contains(fragment, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>Whether a point is within the hidden clearance of a station kept secret.</summary>
    private bool NearHiddenStation(Vector2 point, MapId map)
    {
        if (_hidden.Count == 0 || _hiddenClearance <= 0f)
            return false;

        var query = EntityQueryEnumerator<MapGridComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.MapID == map && uid != xform.MapUid && !HasComp<ShuttleDeedComponent>(uid)
                && (HasComp<StationMemberComponent>(uid) || HasComp<Content.Server.Station.Components.BecomesStationComponent>(uid))
                && Avoided(uid, _hidden)
                && (_transform.GetWorldPosition(xform) - point).LengthSquared() < _hiddenClearance * _hiddenClearance)
                return true;
        }

        return false;
    }

    private bool NearStation(float clearance, Vector2 point, MapId map)
    {
        if (clearance <= 0f)
            return false;

        var query = EntityQueryEnumerator<StationMemberComponent, MapGridComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out _, out var xform))
        {
            if (xform.MapID == map && !HasComp<ShuttleDeedComponent>(uid)
                && (_transform.GetWorldPosition(xform) - point).LengthSquared() < clearance * clearance)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Whether a map is sector space that ships can be put in: not hyperspace, not the shipyard's holding map, not
    /// an expedition and not one of a planet's layers.
    /// </summary>
    private bool IsSector(MapId map, EntityUid? mapUid)
    {
        if (mapUid is not { } uid || HasComp<Content.Shared.Shuttles.Components.FTLMapComponent>(uid)
            || HasComp<Content.Server.Salvage.Expeditions.SalvageExpeditionComponent>(uid)
            || EntityManager.System<Content.Server._NF.Shipyard.Systems.ShipyardSystem>().ShipyardMap == map)
            return false;

        return !OnPlanet(uid);
    }

    /// <summary>Whether a map is one of a planet's layers: its orbit, its air, its ground or the caverns under it.</summary>
    private bool OnPlanet(EntityUid? map)
    {
        if (map is not { } uid)
            return false;

        var networks = EntityQueryEnumerator<Content.Server._WF.Planets.WFPlanetNetworkComponent>();
        while (networks.MoveNext(out var network))
        {
            if (network.OrbitMap == uid || network.Layers.Contains(uid) || network.LowerLayers.Contains(uid))
                return true;
        }

        return false;
    }

    /// <summary>How far outside a planet's pull, as a share of its reach, an encounter is kept.</summary>
    private const float WellMargin = 1.25f;

    /// <summary>
    /// Whether a point is inside a planet's gravity well, or near enough to drift in. A hull with no thrust there
    /// is pulled down into orbit, and encounters leave hulks, stranded ships and beaten ones adrift.
    /// </summary>
    private bool InGravityWell(Vector2 point, MapId map)
    {
        var planets = EntityQueryEnumerator<Content.Shared._WF.Planets.WFSectorPlanetComponent, TransformComponent>();
        while (planets.MoveNext(out _, out var planet, out var xform))
        {
            if (xform.MapID != map || !TryGetEntity(planet.OrbitMap, out var orbit)
                || !TryComp<Content.Shared._WF.Planets.WFOrbitLayerComponent>(orbit, out var layer))
                continue;

            var reach = layer.Range * WellMargin;
            if ((_transform.GetWorldPosition(xform) - point).LengthSquared() <= reach * reach)
                return true;
        }

        return false;
    }

    /// <summary>A point in clear space at the prototype's distance from a random living player.</summary>
    private bool TryPlaceInOpenSpace(WFEncounterPrototype prototype, out MapCoordinates origin)
    {
        origin = MapCoordinates.Nullspace;
        var anchors = new List<MapCoordinates>();
        var players = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (players.MoveNext(out var uid, out _, out var xform))
        {
            // A body without a mob state still counts; ghosts and the dead don't. Only the sector is open space:
            // a player on a planet, in its orbit or in a cavern is no place to put ships beside.
            if (xform.MapID != MapId.Nullspace && IsSector(xform.MapID, xform.MapUid) && !HasComp<GhostComponent>(uid) && !_mobs.IsDead(uid) && !_mobs.IsCritical(uid))
                anchors.Add(_transform.GetMapCoordinates(uid, xform));
        }

        if (anchors.Count == 0)
            return false;

        for (var i = 0; i < PlacementTries; i++)
        {
            var anchor = _random.Pick(anchors);
            var distance = _random.NextFloat(prototype.MinDistance, MathF.Max(prototype.MinDistance, prototype.MaxDistance));
            var point = anchor.Position + _random.NextAngle().ToVec() * distance;
            _nearby.Clear();
            _maps.FindGridsIntersecting(anchor.MapId, Box2.CenteredAround(point, new Vector2(Clearance * 2f)), ref _nearby, approx: true, includeMap: false);
            if (_nearby.Count > 0 || NearStation(prototype.StationClearance, point, anchor.MapId) || InGravityWell(point, anchor.MapId)
                || NearHiddenStation(point, anchor.MapId))
                continue;

            origin = new MapCoordinates(point, anchor.MapId);
            return true;
        }

        return false;
    }
}
