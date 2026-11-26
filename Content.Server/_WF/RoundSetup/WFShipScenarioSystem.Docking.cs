using Content.Server._NF.SectorServices;
using Content.Server._NF.Station.Components;
using Content.Server.Shuttles.Systems;
using Content.Server.Station.Components;
using Content.Server.Station.Systems;
using Content.Shared._WF.RoundSetup;
using Content.Shared.Station.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._WF.RoundSetup;

public sealed partial class WFShipScenarioSystem
{
    [Dependency] private DockingSystem _docking = default!;
    [Dependency] private ShuttleSystem _shuttles = default!;
    [Dependency] private ServerStationJobsSystem _stationJobs = default!;

    /// <summary>Uses the station's existing services or command job, even when that job is occupied.</summary>
    private bool IsDepartureStation(EntityUid station, WFCrewFaction faction) => faction == WFCrewFaction.None
        ? HasComp<StationSectorServiceHostComponent>(station)
        : HasComp<StationSpawningComponent>(station) && !HasComp<ExtraShuttleInformationComponent>(station) &&
          !HasComp<WFShipScenarioStationComponent>(station) &&
          WFFactionCrewPolicy.CommandJob(faction) is { } command && _stationJobs.TryGetJobSlot(station, command, out _);

    /// <summary>Docks at the selected station, or finds clear space around it when all compatible berths are occupied.</summary>
    private bool TryStartAtStation(EntityUid hull, MapGridComponent hullGrid, EntityUid map, MapId mapId, WFCrewFaction faction)
    {
        var stations = EntityQueryEnumerator<StationDataComponent>();
        while (stations.MoveNext(out var station, out var data))
        {
            if (!IsDepartureStation(station, faction))
                continue;
            foreach (var grid in data.Grids)
            {
                if (TerminatingOrDeleted(grid) || Transform(grid).MapID != mapId || !HasComp<MapGridComponent>(grid))
                    continue;
                if (TryDockAtGrid(hull, grid))
                    return true;
            }
            foreach (var grid in data.Grids)
            {
                if (TerminatingOrDeleted(grid) || Transform(grid).MapID != mapId || !TryComp<MapGridComponent>(grid, out var target))
                    continue;
                var bounds = _transform.GetWorldMatrix(grid).TransformBox(target.LocalAABB);
                var radius = bounds.Size.Length() / 2f + hullGrid.LocalAABB.Size.Length() / 2f + 100f;
                if (FindClearPosition(hull, hullGrid, map, mapId, bounds.Center, radius, radius + 800f))
                    return true;
            }
        }
        return false;
    }

    private bool TryDockAtGrid(EntityUid hull, EntityUid grid)
    {
        if (_docking.GetDockingConfig(hull, grid) is not { } config)
            return false;
        _shuttles.FTLDock((hull, Transform(hull)), config);
        foreach (var (_, target, component, _) in config.Docks)
        {
            if (component.DockedWith == target)
                return true;
        }
        return false;
    }
}
