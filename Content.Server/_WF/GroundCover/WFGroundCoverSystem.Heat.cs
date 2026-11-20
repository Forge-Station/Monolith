using System.Numerics;
using Content.Server._Mono.Temperature.Components;
using Content.Server._Mono.Temperature.Systems;
using Content.Shared._WF.GroundCover;
using Robust.Shared.Map.Components;

namespace Content.Server._WF.GroundCover;

public sealed partial class WFGroundCoverSystem
{
    [Dependency] private EntityRadiusHeaterSystem _radiusHeater = default!;

    /// <summary>How often lit fires and running heaters melt the snow round them.</summary>
    private static readonly TimeSpan HeatInterval = TimeSpan.FromSeconds(2);

    /// <summary>Snow levels a minute melted beside a heat source, falling off to nothing at its warmth radius.</summary>
    private const float HeatMeltLevelsPerMinute = 6f;

    private TimeSpan _nextHeat;

    /// <summary>Melts the snow within reach of every lit fire and running heater standing on a snowy grid.</summary>
    private void UpdateHeat(TimeSpan now)
    {
        if (now < _nextHeat)
            return;

        _nextHeat = now + HeatInterval;
        var perPass = HeatMeltLevelsPerMinute * (float) HeatInterval.TotalMinutes * WFSnowCover.UnitsPerLevel;
        var query = EntityQueryEnumerator<EntityRadiusHeaterComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var heater, out var xform))
        {
            if (xform.GridUid is not { } grid
                || !HasComp<WFSnowCoverComponent>(grid)
                || !TryComp<MapGridComponent>(grid, out var gridComp)
                || !_radiusHeater.IsActive(uid, heater))
                continue;

            var centre = _map.TileIndicesFor(grid, gridComp, xform.Coordinates);
            var reach = (int) MathF.Ceiling(heater.Radius);
            for (var x = -reach; x <= reach; x++)
            for (var y = -reach; y <= reach; y++)
            {
                var distance = new Vector2(x, y).Length();
                if (distance > heater.Radius)
                    continue;

                var tile = centre + new Vector2i(x, y);
                var depth = GetDepth(grid, tile);
                if (depth == 0)
                    continue;

                var melt = (int) MathF.Ceiling(perPass * (1f - distance / (heater.Radius + 1f)));
                SetDepth(grid, tile, depth - melt);
            }
        }
    }
}
