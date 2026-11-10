using Content.Server.Shuttles.Components;
using Content.Shared.Shuttles.Components;
using Content.Shared._Mono.Ships.Components;
using Content.Shared._WF.RoundSetup;
using Content.Shared.Station;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Map.Components;

namespace Content.Server._WF.RoundSetup;

public sealed partial class WFShipScenarioSystem
{
    private sealed class AssignedShip(EntityUid hull, EntityUid station)
    {
        public readonly EntityUid Station = station;
        public readonly HashSet<EntityUid> Grids = new() { hull };
    }

    private readonly Dictionary<int, AssignedShip> _assignedShips = new();

    /// <summary>A replacement remains authoritative even after its last surviving deck is destroyed.</summary>
    public bool HasAssignedShip(int lobbyId) => _assignedShips.ContainsKey(lobbyId);

    /// <summary>Returns only the current hull and its fragments, never an abandoned crew ship.</summary>
    public IReadOnlyCollection<EntityUid> GetActiveGrids(int lobbyId) => _assignedShips.TryGetValue(lobbyId, out var assigned)
        ? assigned.Grids : _scenarios.TryGetValue(lobbyId, out var scenario) ? scenario.Grids : Array.Empty<EntityUid>();

    /// <summary>Builds public ship identity from live metadata, including shipyard renames.</summary>
    public WFRoundSetupShip? SnapshotShip(EntityUid? hull) => hull is { } grid && !TerminatingOrDeleted(grid)
        ? new WFRoundSetupShip(GetNetEntity(grid), Name(grid), CompOrNull<VesselComponent>(grid)?.VesselId.Id) : null;

    /// <summary>Attaches an already-owned ship without buying, moving, selling or deleting either hull.</summary>
    public bool AssignShip(int lobbyId, EntityUid hull)
    {
        if (TerminatingOrDeleted(hull) || !HasComp<ShuttleComponent>(hull) || !HasComp<MapGridComponent>(hull) || !TryFindFloor(hull, out _))
            return false;
        // Keep crew job slots and records independent of the ship's station and its faction quotas.
        var station = _assignedShips.TryGetValue(lobbyId, out var previous) && !TerminatingOrDeleted(previous.Station)
            ? previous.Station
            : _stations.InitializeNewStation(new StationConfig
            {
                StationPrototype = "WFShipScenarioStation",
                StationComponentOverrides = new ComponentRegistry(),
            }, null, Name(hull));
        EnsureComp<WFShipScenarioStationComponent>(station).LobbyId = lobbyId;
        _assignedShips[lobbyId] = new AssignedShip(hull, station);
        EnsureComp<WFShipScenarioComponent>(hull).LobbyId = lobbyId;
        return true;
    }

    /// <summary>Resolves a replacement's station and a surviving deck; never falls back to the original ship.</summary>
    public bool TryGetAssignedSpawn(int lobbyId, out EntityUid station, out EntityCoordinates coordinates)
    {
        station = EntityUid.Invalid;
        coordinates = default;
        if (!_assignedShips.TryGetValue(lobbyId, out var assigned) || TerminatingOrDeleted(assigned.Station))
            return false;
        station = assigned.Station;
        foreach (var grid in assigned.Grids)
        {
            if (!TerminatingOrDeleted(grid) && TryFindFloor(grid, out coordinates))
                return true;
        }
        return false;
    }
}
