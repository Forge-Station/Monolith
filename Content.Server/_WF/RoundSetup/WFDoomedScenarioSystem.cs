using System.Linq;
using System.Numerics;
using Content.Server._Mono.ShipRepair;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.Encounters.Systems;
using Content.Server._WF.NpcCrew.Components;
using Content.Server._WF.ShipAccess;
using Content.Server.Chat.Managers;
using Content.Server.GameTicking;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Events;
using Content.Server.Station.Systems;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WF.DroneRings;
using Content.Shared._WF.Encounters;
using Content.Shared._WF.NpcCrew;
using Content.Shared._WF.ShipAccess;
using Content.Shared.GameTicking;
using Content.Shared.Station;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._WF.RoundSetup;

/// <summary>Down With Ye' Ship!: the SY Reach in drone space, ringed by three Reaver warships.</summary>
public sealed partial class WFDoomedScenarioSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private MapLoaderSystem _loader = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private StationSystem _stations = default!;
    [Dependency] private ShipRepairSystem _repair = default!;
    [Dependency] private WFShipScenarioSystem _ships = default!;
    [Dependency] private WFShipAccessServerSystem _access = default!;
    [Dependency] private WFEncounterSystem _encounters = default!;
    [Dependency] private WFSafeSpawnSystem _safeSpawn = default!;

    /// <summary>The hull every doomed crew starts aboard.</summary>
    public static readonly ProtoId<VesselPrototype> Vessel = "WFReach";

    /// <summary>The three Reaver warships waiting for it.</summary>
    public static readonly ProtoId<WFEncounterPrototype> Ambush = "WFDoomedReaverAmbush";

    /// <summary>The ambush's threat tier is drawn from this range, inclusive.</summary>
    public const int MinTier = 4, MaxTier = 5;

    /// <summary>Drone zone 1-2 bounds when the sector has fewer than two drone rings.</summary>
    private const float FallbackInner = 4000f, FallbackOuter = 8000f;

    /// <summary>How far from the hull's origin other grids must be: past the Reach's size plus the ambush ring.</summary>
    private const float ClearRadius = 1500f;

    private readonly Dictionary<int, Scenario> _scenarios = new();

    private sealed class Scenario(EntityUid station)
    {
        public readonly EntityUid Station = station;
        public readonly HashSet<EntityUid> Grids = new();
        public EntityUid? Encounter;
        public bool Used;
    }

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GridSplitEvent>(OnGridSplit);
        SubscribeLocalEvent<ConsoleFTLAttemptEvent>(OnFTLAttempt);
        SubscribeLocalEvent<Content.Server._WF.Planets.Hop.WFPlanetHopAttemptEvent>(OnHopAttempt);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    /// <summary>Whether the hull and the ambush both exist on this server.</summary>
    public bool IsAvailable => _prototypes.HasIndex(Vessel) && _prototypes.HasIndex(Ambush);

    /// <summary>The station used for this lobby's normal job and mind setup.</summary>
    public bool TryGetStation(int lobbyId, out EntityUid station)
    {
        station = EntityUid.Invalid;
        if (!_scenarios.TryGetValue(lobbyId, out var scenario) || TerminatingOrDeleted(scenario.Station))
            return false;
        station = scenario.Station;
        return true;
    }

    /// <summary>Loads the Reach on clear space between drone zones 1 and 2. The ambush waits for the first arrival.</summary>
    public bool TryPrepare(int lobbyId, out EntityUid station, out string reason)
    {
        reason = string.Empty;
        if (TryGetStation(lobbyId, out station))
            return true;
        if (_scenarios.ContainsKey(lobbyId))
        {
            reason = "wf-round-setup-doomed-gone";
            return false;
        }

        reason = "wf-round-setup-doomed-failed";
        var mapId = _ticker.DefaultMap;
        if (!IsAvailable || !_map.TryGetMap(mapId, out var mapUid))
        {
            Log.Warning($"Doomed scenario for lobby {lobbyId}: no default map.");
            return false;
        }
        var vessel = _prototypes.Index(Vessel);
        // Loaded straight onto clear space: loaded at the centre, over Central, its loose items moved onto the station.
        if (!TryFindClearSpot(mapUid.Value, mapId, out var spot))
        {
            Log.Warning($"Doomed scenario for lobby {lobbyId}: no clear space between drone zones 1 and 2.");
            return false;
        }

        if (!_loader.TryLoadGrid(mapId, vessel.ShuttlePath, out var loaded, offset: spot))
        {
            Log.Warning($"Doomed scenario for lobby {lobbyId}: {vessel.ShuttlePath} failed to load.");
            return false;
        }

        var hull = loaded.Value.Owner;
        EntityManager.AddComponents(hull, vessel.AddComponents);
        if (!HasComp<ShuttleComponent>(hull) || !TryFindFloor(hull, out _) || !TryPlace(hull, loaded.Value.Comp, mapUid.Value, mapId))
        {
            Log.Warning($"Doomed scenario for lobby {lobbyId}: no clear space between drone zones 1 and 2.");
            Del(hull);
            return false;
        }

        _repair.GenerateRepairData(hull);
        station = _stations.InitializeNewStation(new StationConfig
        {
            StationPrototype = "WFDoomedScenarioStation",
            StationComponentOverrides = new ComponentRegistry(),
        }, new[] { hull }, Loc.GetString("wf-round-setup-doomed-station", ("ship", vessel.Name)));
        EnsureComp<WFDoomedScenarioStationComponent>(station).LobbyId = lobbyId;
        _ships.AddVesselExpeditions(station, vessel);
        EnsureComp<WFDoomedScenarioComponent>(hull).LobbyId = lobbyId;
        // Taking to the pods is no way out: the ambush goes on until the hull is gone.
        EnsureComp<WFCrewHuntedHullComponent>(hull);
        // Registered to nobody: no deed to sell, and crew access opens it for the crew.
        _access.SetupRegisteredShip(hull, vessel.Name, Array.Empty<NetUserId>());
        var scenario = new Scenario(station);
        scenario.Grids.Add(hull);
        _scenarios[lobbyId] = scenario;
        return true;
    }

    /// <summary>Finds intact deck on this lobby's hull or its surviving sections.</summary>
    public bool TryGetSpawn(int lobbyId, out EntityCoordinates coordinates)
    {
        coordinates = default;
        if (!_scenarios.TryGetValue(lobbyId, out var scenario))
            return false;
        foreach (var grid in scenario.Grids)
        {
            if (!TerminatingOrDeleted(grid) && TryFindFloor(grid, out coordinates))
                return true;
        }
        return false;
    }

    /// <summary>Links crew access, springs the ambush on the first arrival and warns each newcomer.</summary>
    public void NotifyArrival(int lobbyId, ICommonSession session)
    {
        if (!_scenarios.TryGetValue(lobbyId, out var scenario) || session.AttachedEntity is not { } body)
            return;
        scenario.Used = true;
        foreach (var grid in scenario.Grids)
        {
            if (TryComp<WFShipAccessComponent>(grid, out var access) && access.CrewId == null &&
                _access.TryLinkCrew((grid, access), body))
                _access.SetMode((grid, access), WFShipAccessMode.Crew);
        }

        if (scenario.Encounter == null && scenario.Grids.FirstOrDefault(grid => !TerminatingOrDeleted(grid)) is { Valid: true } hull &&
            _encounters.TrySpawn(_prototypes.Index(Ambush), _transform.GetMapCoordinates(hull), out var encounter,
                offBudget: true, tier: _random.Next(MinTier, MaxTier + 1)))
        {
            scenario.Encounter = encounter;
            if (TryComp<WFEncounterComponent>(encounter, out var comp))
            {
                // @player skips station ships without a deed, so the Reach is named outright.
                foreach (var (key, ship) in comp.Ships)
                {
                    EnsureComp<WFCrewOnSightComponent>(ship.Grid);
                    _encounters.SetOrders((encounter, comp), key, new List<WFCrewObjective>
                    {
                        new() { Kind = WFCrewObjectiveKind.Attack, Target = GetNetEntity(hull) },
                    });
                }
            }
        }

        _chat.DispatchServerMessage(session, Loc.GetString("wf-round-setup-doomed-warning"));
    }

    /// <summary>Returns the surviving scenario sections for crew identity and announcements.</summary>
    public IReadOnlyCollection<EntityUid> GetGrids(int lobbyId) => _scenarios.TryGetValue(lobbyId, out var scenario)
        ? scenario.Grids : Array.Empty<EntityUid>();

    /// <summary>Forgets a lobby; only a never-used preparation is deleted.</summary>
    public void RemoveScenario(int lobbyId)
    {
        if (!_scenarios.TryGetValue(lobbyId, out var scenario) || scenario.Used)
            return;
        _scenarios.Remove(lobbyId);
        foreach (var grid in scenario.Grids)
            QueueDel(grid);
        QueueDel(scenario.Station);
    }

    /// <summary>The ring between the first two drone rings the hull may start in, kept off its edges.</summary>
    private (float Inner, float Outer) Band(EntityUid map)
    {
        var (inner, outer) = (FallbackInner, FallbackOuter);
        if (TryComp<WFDroneRingsComponent>(map, out var rings) && rings.Radii.Count > 0)
        {
            inner = rings.Radii[0];
            outer = rings.Radii.Count > 1 ? rings.Radii[1] : inner + FallbackOuter - FallbackInner;
        }
        // Keep the ambush ring inside the band too.
        var margin = Math.Min(1000f, (outer - inner) / 4f);
        return (inner + margin, outer - margin);
    }

    /// <summary>A point in the band with no grid within <see cref="ClearRadius"/>, to load the hull at.</summary>
    private bool TryFindClearSpot(EntityUid map, MapId mapId, out Vector2 spot)
    {
        var (inner, outer) = Band(map);
        var nearby = new List<Entity<MapGridComponent>>();
        for (var attempt = 0; attempt < 32; attempt++)
        {
            spot = _random.NextVector2(inner, outer);
            nearby.Clear();
            _map.FindGridsIntersecting(mapId, Box2.CenteredAround(spot, new Vector2(ClearRadius * 2)), ref nearby, approx: true, includeMap: false);
            if (nearby.Count == 0)
                return true;
        }

        spot = default;
        return false;
    }

    /// <summary>Keeps the hull where it loaded if that is clear, else moves it to clear space in the band.</summary>
    private bool TryPlace(EntityUid hull, MapGridComponent grid, EntityUid map, MapId mapId)
    {
        var (inner, outer) = Band(map);
        var nearby = new List<Entity<MapGridComponent>>();
        for (var attempt = 0; attempt < 32; attempt++)
        {
            if (attempt > 0)
                _transform.SetCoordinates(hull, new EntityCoordinates(map, _random.NextVector2(inner, outer)));
            var bounds = _transform.GetWorldMatrix(hull).TransformBox(grid.LocalAABB).Enlarged(1200f);
            nearby.Clear();
            _map.FindGridsIntersecting(mapId, bounds, ref nearby, approx: true, includeMap: false);
            if (nearby.All(other => other.Owner == hull))
                return true;
        }
        return false;
    }

    /// <summary>Picks a breathable interior deck tile, never exterior plating or a gun.</summary>
    private bool TryFindFloor(EntityUid hull, out EntityCoordinates coordinates) =>
        _safeSpawn.TryFindSafeFloor(hull, out coordinates);

    /// <summary>The Reavers hold the ship until their ambush is over, so it can't climb to orbit either.</summary>
    private void OnHopAttempt(ref Content.Server._WF.Planets.Hop.WFPlanetHopAttemptEvent args)
    {
        if (!TryComp<WFDoomedScenarioComponent>(args.Uid, out var doomed) ||
            !_scenarios.TryGetValue(doomed.LobbyId, out var scenario) || scenario.Encounter is not { } encounter ||
            !TryComp<WFEncounterComponent>(encounter, out var comp) || comp.Resolution != null)
            return;
        args.Cancelled = true;
        args.Reason = Loc.GetString("wf-round-setup-doomed-jammed");
    }

    /// <summary>The Reavers jam the drive until their ambush is over.</summary>
    private void OnFTLAttempt(ref ConsoleFTLAttemptEvent args)
    {
        if (!TryComp<WFDoomedScenarioComponent>(args.Uid, out var doomed) ||
            !_scenarios.TryGetValue(doomed.LobbyId, out var scenario) || scenario.Encounter is not { } encounter ||
            !TryComp<WFEncounterComponent>(encounter, out var comp) || comp.Resolution != null)
            return;
        args.Cancelled = true;
        args.Reason = Loc.GetString("wf-round-setup-doomed-jammed");
    }

    /// <summary>Keeps late arrivals on fragments of the Reach.</summary>
    private void OnGridSplit(ref GridSplitEvent args)
    {
        if (!TryComp<WFDoomedScenarioComponent>(args.Grid, out var original))
            return;
        foreach (var fragment in args.NewGrids)
        {
            EnsureComp<WFDoomedScenarioComponent>(fragment).LobbyId = original.LobbyId;
            if (_scenarios.TryGetValue(original.LobbyId, out var scenario))
                scenario.Grids.Add(fragment);
        }
    }

    private void OnRoundRestart(RoundRestartCleanupEvent args)
    {
        _scenarios.Clear();
    }
}
