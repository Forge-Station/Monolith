using System.Numerics;

namespace Content.Shared._WF.ShipShields;

/// <summary>Finds the first protected perimeter crossing along a finite hitscan ray.</summary>
public static class WFShipShieldHitscanMath
{
    /// <summary>Clips a ray against shield contours and angular coverage.</summary>
    public static bool TryIntersect(Vector2[][] contours, Vector2 origin, Vector2 direction, float maximum,
        WFShipShieldShuntComponent? allocation, out float distance, out Vector2 position, out float strength)
    {
        distance = maximum;
        position = default;
        strength = 0f;
        var found = false;
        foreach (var contour in contours)
        for (var i = 0; i < contour.Length; i++)
        {
            var start = contour[i];
            var edge = contour[(i + 1) % contour.Length] - start;
            var denominator = Cross(direction, edge);
            if (MathF.Abs(denominator) < 0.000001f)
                continue;
            var offset = start - origin;
            var alongRay = Cross(offset, edge) / denominator;
            var alongEdge = Cross(offset, direction) / denominator;
            if (alongRay < 0f || alongRay > distance || alongEdge < 0f || alongEdge > 1f)
                continue;
            var point = origin + direction * alongRay;
            var multiplier = allocation == null ? 1f : WFShipShieldShuntMath.StrengthMultiplier(point,
                allocation.Center, allocation.DirectionRadians, allocation.Concentration, allocation.ArcRadians);
            if (multiplier <= 0f)
                continue;
            found = true;
            distance = alongRay;
            position = point;
            strength = multiplier;
        }
        return found;
    }

    /// <summary>Checks that an originating ship's beam is actually launched inside its perimeter.</summary>
    public static bool Contains(Vector2[][] contours, Vector2 point)
    {
        var inside = false;
        foreach (var contour in contours)
        for (var i = 0; i < contour.Length; i++)
        {
            var a = contour[i];
            var b = contour[(i + 1) % contour.Length];
            if ((a.Y > point.Y) == (b.Y > point.Y))
                continue;
            var crossing = a.X + (point.Y - a.Y) * (b.X - a.X) / (b.Y - a.Y);
            if (point.X < crossing)
                inside = !inside;
        }
        return inside;
    }

    private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;
}
