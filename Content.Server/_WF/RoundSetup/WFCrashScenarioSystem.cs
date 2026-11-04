using System.Linq;
using System.Numerics;
using Content.Server._Mono.ShipRepair;
using Content.Server._NF.Shuttles.Components;
using Content.Server._WF.Planets;
using Content.Server._WF.Planets.Flight;
using Content.Server._WF.ShipPa;
using Content.Server.Chat.Managers;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Events;
using Content.Server.Shuttles.Systems;
using Content.Server.Station.Systems;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WF.Planets;
using Content.Shared._WF.RoundSetup;
using Content.Shared._WF.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Content.Shared.Popups;
using Content.Shared.Shuttles.Components;
using Content.Shared.Station;
using Content.Shared.UserInterface;
using Robust.Shared.Configuration;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.RoundSetup;

/// <summary>Prepares shared crash starts and follows their surviving wreck sections.</summary>
public sealed partial class WFCrashScenarioSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private MapLoaderSystem _loader = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private TurfSystem _turf = default!;
    [Dependency] private WFPlanetRegistrySystem _planets = default!;
    [Dependency] private WFPlanetNetworkSystem _networks = default!;
    [Dependency] private WFShipScenarioSystem _ships = default!;
    [Dependency] private WFOrbitEntrySystem _orbit = default!;
    [Dependency] private Content.Server._WF.Planets.Bounds.WFPlanetBoundsSystem _bounds = default!;
    [Dependency] private ShuttleSystem _shuttle = default!;
    [Dependency] private ShuttleConsoleSystem _consoles = default!;
    [Dependency] private ThrusterSystem _thrusters = default!;
    [Dependency] private StationSystem _stations = default!;
    [Dependency] private ShipPaSystem _pa = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private ShipRepairSystem _repair = default!;
    [Dependency] private WFSafeSpawnSystem _safeSpawn = default!;

    /// <summary>Time to prepare after the first survivor boards.</summary>
    public static readonly TimeSpan PreparationTime = TimeSpan.FromSeconds(WFCrashCountdownEvent.DurationSeconds);

    /// <summary>Civilian occupations available aboard a crash start.</summary>
    public static readonly string[] AllowedJobs = { "Contractor", "Pilot", "Mercenary", "StationEngineer", "MedicalDoctor" };

    /// <summary>Existing civilian hulls with enclosed crew spaces and room for wreck survivors.</summary>
    public static readonly string[] VesselIds =
    {
        "Guppy", "Peregrine", "Rosetta", "Pillbox",
        "Arribane", "Noble", "Sakuratsu", "Shuilian",
        "Spessbite", "Takeaway", "Tiger", "Zhuangzi",
    };

    private readonly Dictionary<int, Scenario> _scenarios = new();
    private TimeSpan _nextUpdate;

    private sealed class Scenario(EntityUid station, string planet)
    {
        public readonly EntityUid Station = station;
        public readonly string Planet = planet;
        public readonly HashSet<EntityUid> Grids = new();
        public bool Used;
        public int CosmeticBursts;
        public TimeSpan NextBurstRemaining;
    }

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GridSplitEvent>(OnGridSplit);
        SubscribeLocalEvent<ConsoleFTLAttemptEvent>(OnFTLAttempt);
        SubscribeLocalEvent<Content.Server._WF.Planets.Hop.WFPlanetHopAttemptEvent>(OnHopAttempt);
        SubscribeLocalEvent<WFCrashScenarioConsoleComponent, ActivatableUIOpenAttemptEvent>(OnConsoleOpen,
            before: new[] { typeof(ShuttleConsoleSystem) });
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
        InitializeCountdown();
    }

    /// <summary>Whether this server enables the planet simulation required by crash starts.</summary>
    public bool IsAvailable => _cfg.GetCVar(PlanetCVars.PlanetNetworks);

    /// <summary>Whether this lobby already has a prepared ship or wreck.</summary>
    public bool HasScenario(int lobbyId) => _scenarios.ContainsKey(lobbyId);

    /// <summary>The station used for this lobby's normal job and mind setup.</summary>
    public bool TryGetStation(int lobbyId, out EntityUid station)
    {
        station = EntityUid.Invalid;
        if (!_scenarios.TryGetValue(lobbyId, out var scenario) || TerminatingOrDeleted(scenario.Station))
            return false;
        station = scenario.Station;
        return true;
    }

    /// <summary>Loads one random civilian hull in a random planet's orbit without starting its timer.</summary>
    public bool TryPrepare(int lobbyId, out EntityUid station, out string reason)
    {
        station = EntityUid.Invalid;
        reason = string.Empty;
        if (TryGetStation(lobbyId, out station))
            return true;

        if (_scenarios.ContainsKey(lobbyId))
        {
            reason = "wf-round-setup-crash-no-wreck";
            return false;
        }

        if (!TryChoosePlanet(out var network, out var planetName))
        {
            reason = "wf-round-setup-crash-no-planet";
            return false;
        }

        var candidates = VesselIds.Where(id => _prototypes.HasIndex<VesselPrototype>(id)).ToList();
        _random.Shuffle(candidates);
        foreach (var id in candidates)
        {
            var vessel = _prototypes.Index<VesselPrototype>(id);
            // A bounded world only lets a hull down from inside its circle.
            var offset = _bounds.TryGetBounds(network.OrbitMap, out var centre, out var radius)
                ? centre + _random.NextVector2(radius * 0.2f, radius * 0.5f)
                : network.Centre + _random.NextVector2(500f, 1200f);
            var mapId = Comp<MapComponent>(network.OrbitMap).MapId;
            if (!_loader.TryLoadGrid(mapId, vessel.ShuttlePath, out var loaded, offset: offset))
                continue;

            var hull = loaded.Value.Owner;
            _bounds.ClampHull(hull);
            EntityManager.AddComponents(hull, vessel.AddComponents);
            if (!HasComp<ShuttleComponent>(hull) || !TryFindFloor(hull, out _))
            {
                QueueDel(hull);
                continue;
            }

            // Captured while the hull is still whole, so the survivors can repair the wreck back to it.
            _repair.GenerateRepairData(hull);
            station = _stations.InitializeNewStation(new StationConfig
            {
                StationPrototype = "WFCrashScenarioStation",
                StationComponentOverrides = new ComponentRegistry(),
            }, new[] { hull }, Loc.GetString("wf-round-setup-crash-station", ("ship", vessel.Name)));
            EnsureComp<WFCrashScenarioStationComponent>(station).LobbyId = lobbyId;
            _ships.AddVesselExpeditions(station, vessel);
            var scenario = new Scenario(station, planetName)
            {
                NextBurstRemaining = PreparationTime - TimeSpan.FromSeconds(_random.Next(40, 56)),
            };
            scenario.Grids.Add(hull);
            _scenarios[lobbyId] = scenario;
            var crash = EnsureComp<WFCrashScenarioComponent>(hull);
            crash.LobbyId = lobbyId;
            HoldInOrbit(hull);
            DisableControls(hull);
            return true;
        }

        reason = "wf-round-setup-crash-no-ship";
        return false;
    }

    /// <summary>Finds intact, unobstructed deck only on this lobby's ship or tracked wreck sections.</summary>
    public bool TryGetSpawn(int lobbyId, out EntityCoordinates coordinates)
    {
        coordinates = default;
        if (!_scenarios.TryGetValue(lobbyId, out var scenario))
            return false;

        var grids = scenario.Grids.Where(grid => !TerminatingOrDeleted(grid)).ToList();
        _random.Shuffle(grids);
        foreach (var grid in grids)
        {
            if (!TryComp<WFCrashScenarioComponent>(grid, out var crash) || crash.LobbyId != lobbyId
                || Transform(grid).MapUid is not { } map || !HasComp<MapComponent>(map))
                continue;
            if (TryFindFloor(grid, out coordinates))
                return true;
        }
        return false;
    }

    /// <summary>Arms the initial timer after a successful spawn and tells each newcomer what to expect.</summary>
    public void NotifyArrival(int lobbyId, ICommonSession session)
    {
        if (!_scenarios.TryGetValue(lobbyId, out var scenario))
            return;

        scenario.Used = true;
        WFCrashScenarioComponent? first = null;
        foreach (var grid in scenario.Grids)
        {
            if (!TryComp<WFCrashScenarioComponent>(grid, out var crash))
                continue;
            if (!crash.Started)
            {
                crash.Started = true;
                crash.DescendAt = _timing.CurTime + PreparationTime;
            }
            first ??= crash;
        }

        SendCountdown(session);
        if (first == null)
            return;
        var key = first.Crashed ? "wf-round-setup-crash-survivor"
            : first.Descending ? "wf-round-setup-crash-descending" : "wf-round-setup-crash-warning";
        _chat.DispatchServerMessage(session, Loc.GetString(key,
            ("planet", scenario.Planet), ("seconds", Math.Max(0, (int) Math.Ceiling((first.DescendAt - _timing.CurTime).TotalSeconds)))));
    }

    /// <summary>Returns the surviving scenario sections for crew identity and announcements.</summary>
    public IReadOnlyCollection<EntityUid> GetGrids(int lobbyId) => _scenarios.TryGetValue(lobbyId, out var scenario)
        ? scenario.Grids : Array.Empty<EntityUid>();

    /// <summary>Forgets a lobby; only never-used preparations are deleted.</summary>
    public void RemoveScenario(int lobbyId)
    {
        if (!_scenarios.Remove(lobbyId, out var scenario) || scenario.Used)
            return;
        foreach (var grid in scenario.Grids)
            QueueDel(grid);
        QueueDel(scenario.Station);
    }

    /// <summary>Finds or builds a random registered planet network.</summary>
    private bool TryChoosePlanet(out WFPlanetNetworkComponent network, out string planetName)
    {
        network = default!;
        planetName = string.Empty;
        var planets = _planets.GetPlanets();
        _random.Shuffle(planets);
        foreach (var planet in planets)
        {
            if (TerminatingOrDeleted(planet))
                continue;
            EntityUid networkUid;
            if (planet.Comp.Network is { } existing && TryGetEntity(existing, out var found)
                && TryComp<WFPlanetNetworkComponent>(found, out var built)
                && !TerminatingOrDeleted(built.OrbitMap))
            {
                network = built;
            }
            else if (_networks.TryBuildNetwork(planet, out networkUid)
                && TryComp<WFPlanetNetworkComponent>(networkUid, out var created))
            {
                network = created;
            }
            else
                continue;
            planetName = Name(planet);
            return true;
        }
        return false;
    }

    /// <summary>Picks a breathable interior deck tile, never exterior plating or machinery.</summary>
    private bool TryFindFloor(EntityUid grid, out EntityCoordinates coordinates) =>
        _safeSpawn.TryFindSafeFloor(grid, out coordinates);

    private void OnRoundRestart(RoundRestartCleanupEvent args)
    {
        _scenarios.Clear();
        ResetCountdown();
        _nextUpdate = TimeSpan.Zero;
    }
}
