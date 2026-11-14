using System.Linq;
using System.Numerics;
using Content.Server.Station.Components;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._WF.Remade.Corruption;

/// <summary>Where nothing may grow: main stations, and a stretch of space round them.</summary>
public sealed partial class WFCorruptionSystem
{
    [Dependency] private IConfigurationManager _cfg = default!;

    private static readonly TimeSpan NearStationTtl = TimeSpan.FromSeconds(5);

    /// <summary>Whether each grid lay within the sanctuary radius of a main station when last looked at, and until when that holds.</summary>
    private readonly Dictionary<EntityUid, (TimeSpan Until, bool Near)> _nearStation = new();

    private float _sanctuaryRadius;
    private readonly HashSet<string> _sanctuaryStations = new();

    private void InitializeSanctuary()
    {
        Subs.CVar(_cfg, WFRemadeSanctuaryCVars.Radius, radius =>
        {
            _sanctuaryRadius = radius;
            _nearStation.Clear();
        }, true);

        Subs.CVar(_cfg, WFRemadeSanctuaryCVars.Stations, ids =>
        {
            _sanctuaryStations.Clear();
            foreach (var id in ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                _sanctuaryStations.Add(id);
            }

            _nearStation.Clear();
        }, true);
    }

    private void PruneNearStation()
    {
        foreach (var grid in _nearStation.Keys.ToList())
        {
            if (TerminatingOrDeleted(grid))
                _nearStation.Remove(grid);
        }
    }

    /// <summary>
    /// Whether this grid is a sanctuary station, named in <see cref="WFRemadeSanctuaryCVars.Stations"/>. Grid protection alone
    /// doesn't count: mining asteroids, outposts and transit buses carry it too.
    /// </summary>
    public bool IsMainStation(EntityUid grid)
    {
        return HasComp<WFRemadeSanctuaryComponent>(grid)
               || TryComp<BecomesStationComponent>(grid, out var station) && _sanctuaryStations.Contains(station.Id);
    }

    /// <summary>
    /// Whether a thing, on a grid or loose in space, is a sanctuary: on a main station, or within the sanctuary radius of one.
    /// Use this for a Monolith that may be off any grid; <see cref="IsSanctuary"/> only has its grid to go by.
    /// </summary>
    public bool IsSanctuaryAt(EntityUid entity)
    {
        var xform = Transform(entity);
        return xform.GridUid is { } grid ? IsSanctuary(grid) : NearMainStation(_transform.GetMapCoordinates(entity));
    }

    /// <summary>Whether the grid lies within the sanctuary radius of a main station, edge to edge.</summary>
    private bool NearMainStation(EntityUid grid)
    {
        if (_sanctuaryRadius <= 0f || !TryComp<MapGridComponent>(grid, out var gridComp))
            return false;

        var now = _timing.CurTime;
        if (_nearStation.TryGetValue(grid, out var cached) && now < cached.Until)
            return cached.Near;

        var (centre, radius) = Bounds(grid, gridComp);
        var near = NearMainStation(Transform(grid).MapID, centre, radius, grid);
        _nearStation[grid] = (now + NearStationTtl, near);
        return near;
    }

    private bool NearMainStation(MapCoordinates at)
    {
        return _sanctuaryRadius > 0f && NearMainStation(at.MapId, at.Position, 0f, null);
    }

    private bool NearMainStation(MapId map, Vector2 centre, float radius, EntityUid? except)
    {
        var stations = EntityQueryEnumerator<BecomesStationComponent, MapGridComponent, TransformComponent>();
        while (stations.MoveNext(out var uid, out _, out var grid, out var xform))
        {
            if (Within(uid, grid, xform, map, centre, radius, except))
                return true;
        }

        var marked = EntityQueryEnumerator<WFRemadeSanctuaryComponent, MapGridComponent, TransformComponent>();
        while (marked.MoveNext(out var uid, out _, out var grid, out var xform))
        {
            if (Within(uid, grid, xform, map, centre, radius, except))
                return true;
        }

        return false;
    }

    /// <summary>Whether a sanctuary grid lies within the radius of a circle, edge to edge.</summary>
    private bool Within(EntityUid uid, MapGridComponent grid, TransformComponent xform, MapId map, Vector2 centre, float radius, EntityUid? except)
    {
        if (uid == except || xform.MapID != map || !IsMainStation(uid))
            return false;

        var (stationCentre, stationRadius) = Bounds(uid, grid);
        return (stationCentre - centre).Length() - stationRadius - radius <= _sanctuaryRadius;
    }

    /// <summary>A grid's centre in the world and the radius of a circle round its box.</summary>
    private (Vector2 Centre, float Radius) Bounds(EntityUid uid, MapGridComponent grid)
    {
        var box = grid.LocalAABB;
        return (Vector2.Transform(box.Center, _transform.GetWorldMatrix(uid)), box.Size.Length() / 2f);
    }
}
