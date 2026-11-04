using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Server._WF.NpcVehicleCrew;
using Content.Shared._WF.PlanetPois;
using Content.Shared._WF.Vehicles.Core;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._WF.PlanetPois;

/// <summary>
/// Vehicles about a world once it exists: working ones parked by the roads and out in the open with the key on the
/// driver's seat, or in the hand of a squatter holed up inside; and crewed patrols driving up and down the roads.
/// </summary>
public sealed partial class WFPlanetPoiSystem
{
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private VehicleInteriorSystem _interiors = default!;
    [Dependency] private WFVehicleCrewSystem _vehicleCrews = default!;

    /// <summary>Tiles of open ground a parked vehicle needs on every side of the tile it stands on.</summary>
    private const int VehicleClearance = 3;

    /// <summary>Road tiles between one patrol point and the next.</summary>
    private const int PatrolPointSpacing = 14;

    /// <summary>Random starts tried for a patrol's stretch of road.</summary>
    private const int PatrolRouteTries = 8;

    /// <summary>The shortest stretch of road worth patrolling.</summary>
    private const int MinPatrolRoute = 12;

    /// <summary>The longest stretch of road a patrol drives before it turns round.</summary>
    private const int PatrolRouteLength = 420;

    private async Task SpawnVehiclesAsync(Entity<BiomeComponent> ground, MapGridComponent grid, WFPlanetPoiComponent record)
    {
        if (record.PendingVehicles is not { } vehicles)
            return;
        record.PendingVehicles = null;

        var roadside = record.ShoulderTiles.ToList();
        var count = _random.Next(vehicles.MinCount, vehicles.MaxCount + 1);
        for (var i = 0; i < count; i++)
        {
            await _preload.Breathe();
            if (TerminatingOrDeleted(ground))
                return;
            var byRoad = roadside.Count > 0 && _random.Prob(vehicles.Roadside);
            if (FindParking(ground, grid, record, byRoad ? roadside : null) is not { } spot)
                continue;

            foreach (var id in _entityTables.GetSpawns(vehicles.Spawns))
            {
                var vehicle = Spawn(id, new EntityCoordinates(ground, (Vector2) spot + new Vector2(0.5f)));
                // Left with whatever was in the tank.
                if (TryComp<WFVehicleEngineComponent>(vehicle, out var engine))
                {
                    engine.Fuel = engine.Capacity * _random.NextFloat(0.25f, 0.75f);
                    Dirty(vehicle, engine);
                }
                if (vehicles.Squatter is { } squatter && _random.Prob(vehicles.SquatterChance))
                    HoleUp(vehicle, squatter);
                break;
            }
        }
    }

    /// <summary>Open ground with room for a vehicle: beside a road when given its shoulders, else anywhere off the landing zone.</summary>
    private Vector2i? FindParking(Entity<BiomeComponent> ground, MapGridComponent grid, WFPlanetPoiComponent record, List<Vector2i>? roadside)
    {
        bool Clear(Vector2i tile)
        {
            for (var x = -VehicleClearance; x <= VehicleClearance; x++)
            for (var y = -VehicleClearance; y <= VehicleClearance; y++)
            {
                var near = tile + new Vector2i(x, y);
                if (_maps.GetTileRef(ground, grid, near).Tile.IsEmpty || _maps.GetAnchoredEntitiesEnumerator(ground, grid, near).MoveNext(out _))
                    return false;
            }
            return true;
        }

        var inner = record.RoamerInner;
        var outer = record.RoamerRadius;
        for (var attempt = 0; attempt < 64; attempt++)
        {
            Vector2i tile;
            if (roadside != null)
                tile = _random.Pick(roadside);
            else if (outer > inner)
                tile = (record.RoamerCentre + _random.NextAngle().ToVec() * MathF.Sqrt(_random.NextFloat(inner * inner, outer * outer))).Floored();
            else
                return null;

            if (Vector2.Distance(tile, record.RoamerCentre) < inner || roadside == null && (_biome.WfIsPinned(ground, tile) || _biome.WfIsWater(ground, tile)))
                continue;
            if (Clear(tile))
                return tile;
        }
        return null;
    }

    /// <summary>Someone has taken the vehicle: they wait inside with its key and go for whoever comes in.</summary>
    public void HoleUp(EntityUid vehicle, EntProtoId squatterId)
    {
        if (!_interiors.TryLoadInterior(vehicle, out var interior))
            return;

        EntityCoordinates? seat = null;
        var seats = EntityQueryEnumerator<VehicleDriverSeatComponent, TransformComponent>();
        while (seats.MoveNext(out _, out _, out var seatXform))
        {
            if (seatXform.MapID != interior.MapId)
                continue;
            seat = seatXform.Coordinates;
            break;
        }
        if (seat is not { } at)
            return;

        var squatter = Spawn(squatterId, at);
        _interiors.TrackInteriorOccupant(vehicle, squatter);
        var keys = EntityQueryEnumerator<VehicleKeyComponent, TransformComponent>();
        while (keys.MoveNext(out var key, out _, out var keyXform))
        {
            if (keyXform.MapID != interior.MapId)
                continue;
            _hands.TryPickupAnyHand(squatter, key);
            break;
        }
    }

    /// <summary>Sends each patrol up and down a stretch of the roads, from a start well off the landing zone.</summary>
    private async Task SpawnRoadPatrolsAsync(EntityUid ground, MapGridComponent grid, WFPlanetPoiComponent record)
    {
        var patrols = record.PendingRoadPatrols.ToList();
        record.PendingRoadPatrols.Clear();
        if (patrols.Count == 0 || record.Centreline.Count == 0)
            return;

        // Well off the landing zone if the roads go that far, anywhere on them if not.
        var starts = record.Centreline.Where(tile => Vector2.Distance(tile, record.RoamerCentre) > record.RoamerInner).ToList();
        if (starts.Count == 0)
            starts = record.Centreline.ToList();

        foreach (var patrol in patrols)
        {
            var count = _random.Next(patrol.MinCount, patrol.MaxCount + 1);
            for (var i = 0; i < count; i++)
            {
                await _preload.Breathe();
                if (TerminatingOrDeleted(ground))
                    return;
                var route = PatrolRoute(record.Centreline, starts);
                if (route.Count < MinPatrolRoute)
                    continue;

                // A short road gets its points closer together, so there are still a few to drive between.
                var spacing = Math.Min(PatrolPointSpacing, route.Count / 3);
                var points = new List<MapCoordinates>();
                for (var step = 0; step < route.Count; step += spacing)
                    points.Add(_transform.ToMapCoordinates(new EntityCoordinates(ground, (Vector2) route[step] + new Vector2(0.5f))));
                // There and back along the same road.
                for (var back = points.Count - 2; back > 0; back--)
                    points.Add(points[back]);

                if (!_vehicleCrews.TrySpawn(patrol.Crew, new EntityCoordinates(ground, (Vector2) route[0] + new Vector2(0.5f)), out var vehicle))
                    continue;
                _vehicleCrews.SetWaypoints(vehicle, points);
            }
        }
    }

    /// <summary>
    /// The longest of a few stretches of road, each followed both ways from a random start, so a start on a short spur
    /// still finds the road it joins.
    /// </summary>
    private List<Vector2i> PatrolRoute(HashSet<Vector2i> centreline, List<Vector2i> starts)
    {
        var best = new List<Vector2i>();
        for (var attempt = 0; attempt < PatrolRouteTries && best.Count < PatrolRouteLength; attempt++)
        {
            var start = _random.Pick(starts);
            var ahead = TraceRoad(centreline, start, new HashSet<Vector2i>());
            var behind = TraceRoad(centreline, start, new HashSet<Vector2i>(ahead));
            behind.RemoveAt(0);
            behind.Reverse();
            behind.AddRange(ahead);
            if (behind.Count > best.Count)
                best = behind;
        }
        return best.Count > PatrolRouteLength ? best.GetRange(0, PatrolRouteLength) : best;
    }

    /// <summary>
    /// Follows the road's centreline from a tile as far as it goes, past the tiles already visited, up to <see cref="PatrolRouteLength"/>: straight on
    /// where it can, a random way at a junction.
    /// </summary>
    private List<Vector2i> TraceRoad(HashSet<Vector2i> centreline, Vector2i start, HashSet<Vector2i> visited)
    {
        var route = new List<Vector2i> { start };
        visited.Add(start);
        var heading = Vector2.Zero;
        var here = start;
        while (route.Count < PatrolRouteLength)
        {
            Vector2i? best = null;
            var bestScore = float.MinValue;
            for (var x = -1; x <= 1; x++)
            for (var y = -1; y <= 1; y++)
            {
                var next = here + new Vector2i(x, y);
                if (next == here || visited.Contains(next) || !centreline.Contains(next))
                    continue;
                var step = Vector2.Normalize(new Vector2(x, y));
                var score = Vector2.Dot(step, heading) + _random.NextFloat(0f, 0.5f);
                if (score <= bestScore)
                    continue;
                best = next;
                bestScore = score;
            }

            if (best is not { } chosen)
                break;
            heading = Vector2.Normalize(chosen - here);
            here = chosen;
            visited.Add(here);
            route.Add(here);
        }
        return route;
    }
}
