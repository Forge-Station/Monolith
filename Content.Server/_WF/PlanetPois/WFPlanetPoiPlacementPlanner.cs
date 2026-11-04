using System;
using System.Collections.Generic;
using System.Numerics;
using Robust.Shared.Maths;

namespace Content.Server._WF.PlanetPois;

/// <summary>Provides bounded fallback origins for footprints with a narrow placement region.</summary>
public static class WFPlanetPoiPlacementPlanner
{
    /// <summary>Reserves a complete road and shoulder outside every edge and corner.</summary>
    public static Box2i WithRoadClearance(Box2i bounds, int width, int shoulderWidth)
    {
        var margin = 2 * (width / 2 + shoulderWidth) + 2;
        return new Box2i(bounds.Left - margin, bounds.Bottom - margin, bounds.Right + margin, bounds.Top + margin);
    }
    /// <summary>Samples a seeded rotated lattice with resolution adapted to the footprint and world size.</summary>
    public static IEnumerable<Vector2i> FallbackOrigins(Vector2 centre, float reach, Box2i footprint, int seed)
    {
        if (!float.IsFinite(reach) || reach <= 0 || reach > 4096 || !float.IsFinite(centre.X) || !float.IsFinite(centre.Y))
            yield break;
        var shortest = Math.Min(footprint.Right - footprint.Left, footprint.Top - footprint.Bottom);
        if (shortest <= 0)
            yield break;
        // At most 129 by 129 probes, with finer spacing for small buildings.
        var step = Math.Max(Math.Clamp(shortest / 8, 4, 16), (int)MathF.Ceiling(reach / 64));
        var span = (int)MathF.Ceiling(reach / step);
        var random = new Random(seed);
        var angle = random.NextDouble() * Math.Tau;
        var cos = (float)Math.Cos(angle);
        var sin = (float)Math.Sin(angle);
        var phaseX = (float)random.NextDouble() * step;
        var phaseY = (float)random.NextDouble() * step;
        var emitted = new HashSet<Vector2i>();
        for (var row = -span; row <= span; row++)
        for (var column = -span; column <= span; column++)
        {
            var x = column * step + phaseX;
            var y = row * step + phaseY;
            var point = new Vector2i((int)MathF.Floor(centre.X + x * cos - y * sin), (int)MathF.Floor(centre.Y + x * sin + y * cos));
            if (Vector2.DistanceSquared(new Vector2(point.X, point.Y), centre) <= reach * reach && emitted.Add(point))
                yield return point;
        }
    }
}
