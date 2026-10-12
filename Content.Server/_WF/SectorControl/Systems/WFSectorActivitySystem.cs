using System.Linq;
using System.Numerics;
using Content.Server._WF.Encounters.Components;
using Content.Server.GameTicking;
using Content.Server.Station.Components;
using Content.Shared._Mono.Company;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._WF.SectorControl;
using Content.Shared.GameTicking;
using Content.Shared.Ghost;
using Content.Shared.Mobs.Systems;
using Content.Shared.Station.Components;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.SectorControl.Systems;

/// <summary>Which cells players fly: each minute, crewed player ships that moved 100 m since the last scan mark their cell active.</summary>
public sealed partial class WFSectorActivitySystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WFSectorTerritorySystem _territory = default!;

    private static readonly TimeSpan ScanInterval = TimeSpan.FromSeconds(60);

    /// <summary>Activity older than this is forgotten; no driver asks about a longer quiet.</summary>
    private static readonly TimeSpan Horizon = TimeSpan.FromHours(2);

    /// <summary>How far a ship must move between scans to count.</summary>
    private const float MoveThreshold = 100f;

    /// <summary>How long a ship may be off the sector map, in FTL, before its track is dropped.</summary>
    private static readonly TimeSpan JumpGrace = TimeSpan.FromMinutes(3);

    private readonly Dictionary<EntityUid, ShipTrack> _ships = new();
    private TimeSpan _nextScan;
    private TimeSpan _lastScan;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundCleanup);
    }

    private void OnRoundCleanup(RoundRestartCleanupEvent args)
    {
        _ships.Clear();
        _nextScan = TimeSpan.Zero;
        _lastScan = TimeSpan.Zero;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (!_territory.Enabled || _ticker.RunLevel != GameRunLevel.InRound || _timing.CurTime < _nextScan)
            return;

        _nextScan = _timing.CurTime + ScanInterval;
        Scan();
    }

    /// <summary>One activity scan of the sector map, as the minute timer runs it.</summary>
    public void Scan()
    {
        var now = _timing.CurTime;
        _lastScan = now;
        var map = _territory.SectorMap;
        var ships = CrewedShips(map);
        var territory = ships.Count > 0 ? _territory.Territory(map, true) : _territory.Territory(map);

        foreach (var grid in ships)
        {
            var position = _transform.GetWorldPosition(grid);
            var cell = WFSectorHex.CellOf(position, _territory.CellSize);
            if (_ships.TryGetValue(grid, out var track) && track.Map == map
                && Vector2.Distance(track.Position, position) >= MoveThreshold && territory != null)
            {
                territory.Activity[cell] = now;
                if (WFSectorHex.Distance(track.Cell, cell) > 1)
                    territory.Activity[track.Cell] = now;

                track.Counted = now;
            }

            if (track == null || track.Map != map)
            {
                track = new ShipTrack { Map = map };
                _ships[grid] = track;
            }

            track.Position = position;
            track.Cell = cell;
            track.Seen = now;
        }

        // A ship in FTL is off the map for a scan or two; its track stays so the jump counts when it lands.
        var gone = new List<EntityUid>();
        foreach (var (grid, track) in _ships)
        {
            if (ships.Contains(grid))
                continue;

            if (TerminatingOrDeleted(grid) || Transform(grid).MapID == map || now - track.Seen > JumpGrace)
                gone.Add(grid);
        }

        foreach (var grid in gone)
        {
            _ships.Remove(grid);
        }

        if (territory == null)
            return;

        var stale = new List<WFSectorCell>();
        foreach (var (cell, last) in territory.Activity)
        {
            if (now - last > Horizon)
                stale.Add(cell);
        }

        foreach (var cell in stale)
        {
            territory.Activity.Remove(cell);
        }
    }

    /// <summary>Marks a cell active now, as a counted ship would.</summary>
    public void Stamp(MapId map, WFSectorCell cell)
    {
        if (_territory.Territory(map, true) is { } territory)
            territory.Activity[cell] = _timing.CurTime;
    }

    /// <summary>When a moving player ship was last counted in a cell, or null if not within the last two hours.</summary>
    public TimeSpan? LastActivity(MapId map, WFSectorCell cell)
    {
        return _territory.Territory(map)?.Activity.TryGetValue(cell, out var last) == true ? last : null;
    }

    /// <summary>Whether no moving player ship was counted in a cell at or after a time.</summary>
    public bool IsQuiet(MapId map, WFSectorCell cell, TimeSpan since)
    {
        return LastActivity(map, cell) is not { } last || last < since;
    }

    /// <summary>The ships counted in a cell at the last scan.</summary>
    public List<EntityUid> ActiveShips(MapId map, WFSectorCell cell)
    {
        var ships = new List<EntityUid>();
        foreach (var (grid, track) in _ships)
        {
            if (track.Map == map && track.Cell == cell && track.Counted == _lastScan && _lastScan != TimeSpan.Zero)
                ships.Add(grid);
        }

        return ships;
    }

    /// <summary>The grids on a map with a living, non-ghost player aboard, leaving out encounter ships and stations without a deed.</summary>
    public HashSet<EntityUid> CrewedShips(MapId map)
    {
        var ships = new HashSet<EntityUid>();
        if (map == MapId.Nullspace)
            return ships;

        var players = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (players.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.MapID != map || xform.GridUid is not { } grid || grid == xform.MapUid
                || HasComp<GhostComponent>(uid) || _mobs.IsDead(uid) || HasComp<WFEncounterGridComponent>(grid)
                || HasComp<StationMemberComponent>(grid) && !HasComp<ShuttleDeedComponent>(grid))
                continue;

            ships.Add(grid);
        }

        return ships;
    }

    /// <summary><see cref="CrewedShips(MapId)"/> without the ships neutral to a faction.</summary>
    public HashSet<EntityUid> CrewedShips(MapId map, ProtoId<WFSectorFactionPrototype> faction)
    {
        var ships = CrewedShips(map);
        if (_prototypes.TryIndex(faction, out var proto) && Neutrals(proto))
            ships.RemoveWhere(ship => IsNeutralTo(proto, ship));

        return ships;
    }

    /// <summary>The crewed player ships in each cell on a map right now, by position with no moved rule.</summary>
    public Dictionary<WFSectorCell, List<EntityUid>> CellsWithCrewedShips(MapId map)
    {
        return CellsOf(CrewedShips(map));
    }

    /// <summary><see cref="CellsWithCrewedShips(MapId)"/> without the ships neutral to a faction.</summary>
    public Dictionary<WFSectorCell, List<EntityUid>> CellsWithCrewedShips(MapId map, ProtoId<WFSectorFactionPrototype> faction)
    {
        return CellsOf(CrewedShips(map, faction));
    }

    private Dictionary<WFSectorCell, List<EntityUid>> CellsOf(HashSet<EntityUid> grids)
    {
        var cells = new Dictionary<WFSectorCell, List<EntityUid>>();
        foreach (var grid in grids)
        {
            var cell = WFSectorHex.CellOf(_transform.GetWorldPosition(grid), _territory.CellSize);
            if (!cells.TryGetValue(cell, out var ships))
                cells[cell] = ships = new List<EntityUid>();

            ships.Add(grid);
        }

        return cells;
    }

    private static bool Neutrals(WFSectorFactionPrototype faction)
    {
        return faction.NeutralCompanies.Count > 0 || faction.NeutralStations.Count > 0;
    }

    /// <summary>Whether a grid is a station a faction leaves alone: of one of its neutral companies, or with one of its neutral station ids.</summary>
    public bool IsNeutralStation(ProtoId<WFSectorFactionPrototype> faction, EntityUid grid)
    {
        return _prototypes.TryIndex(faction, out var proto) && IsNeutralStation(proto, grid);
    }

    private bool IsNeutralStation(WFSectorFactionPrototype faction, EntityUid grid)
    {
        if (!Neutrals(faction) || TerminatingOrDeleted(grid))
            return false;

        if (TryComp<CompanyComponent>(grid, out var company) && faction.NeutralCompanies.Contains(company.CompanyName))
            return true;

        return TryComp<BecomesStationComponent>(grid, out var station)
            && faction.NeutralStations.Any(id => string.Equals(id, station.Id, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Whether a grid is a station: a station member without a deed, or a grid that becomes one.</summary>
    private bool IsStation(EntityUid grid)
    {
        return HasComp<StationMemberComponent>(grid) && !HasComp<ShuttleDeedComponent>(grid) || HasComp<BecomesStationComponent>(grid);
    }

    /// <summary>
    /// Whether a faction leaves a grid alone: a neutral station, docked to one, flying a neutral company's flag or,
    /// with no flag of its own, crewed only by people of neutral companies.
    /// </summary>
    public bool IsNeutralTo(ProtoId<WFSectorFactionPrototype> faction, EntityUid grid)
    {
        return _prototypes.TryIndex(faction, out var proto) && IsNeutralTo(proto, grid);
    }

    private bool IsNeutralTo(WFSectorFactionPrototype faction, EntityUid grid)
    {
        if (!Neutrals(faction) || TerminatingOrDeleted(grid))
            return false;

        if (IsNeutralStation(faction, grid))
            return true;

        var docks = EntityQueryEnumerator<Content.Server.Shuttles.Components.DockingComponent, TransformComponent>();
        while (docks.MoveNext(out _, out var dock, out var dockXform))
        {
            if (dockXform.GridUid == grid && dock.DockedWith is { } other
                && Transform(other).GridUid is { } alongside && IsStation(alongside) && IsNeutralStation(faction, alongside))
                return true;
        }

        if (TryComp<CompanyComponent>(grid, out var flag) && flag.CompanyName.Id != "None")
            return false;

        var aboard = false;
        var players = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (players.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid != grid || HasComp<GhostComponent>(uid) || _mobs.IsDead(uid))
                continue;

            if (!TryComp<CompanyComponent>(uid, out var own) || !faction.NeutralCompanies.Contains(own.CompanyName))
                return false;

            aboard = true;
        }

        return aboard;
    }

    /// <summary>Where a ship was at the last scan, and when it was last counted.</summary>
    private sealed class ShipTrack
    {
        public MapId Map;
        public Vector2 Position;
        public WFSectorCell Cell;
        public TimeSpan Counted;
        public TimeSpan Seen;
    }
}
