using System.Numerics;
using System.Linq;

namespace Content.Shared._WF.ShipShields;

/// <summary>Fits a symmetric oval around the occupied hull with a compact clearance.</summary>
public static class WFShipShieldGeometry
{
    /// <summary>Returns one counterclockwise oval enclosing every hull tile, without a repeated closing vertex.</summary>
    public static Vector2[][] CreateContours(IEnumerable<Vector2i> tiles, float tileSize = 1f, int padding = 5)
    {
        var hull = tiles.ToArray();
        if (hull.Length == 0)
            return Array.Empty<Vector2[]>();
        var min = new Vector2(hull[0].X, hull[0].Y);
        var max = min + Vector2.One;
        foreach (var tile in hull)
        {
            var point = new Vector2(tile.X, tile.Y);
            min = Vector2.Min(min, point);
            max = Vector2.Max(max, point + Vector2.One);
        }
        var center = (min + max) * 0.5f;
        var radii = (max - min) * 0.5f;
        var fit = 1f;
        foreach (var tile in hull)
        {
            // The furthest corner bounds the whole tile in the centered ellipse.
            var corner = Vector2.Max(Vector2.Abs(new Vector2(tile.X, tile.Y) - center),
                Vector2.Abs(new Vector2(tile.X + 1, tile.Y + 1) - center));
            fit = MathF.Max(fit, (corner / radii).Length());
        }
        radii = (radii * fit + new Vector2(padding)) * tileSize;
        center *= tileSize;
        var circumference = MathF.Tau * MathF.Sqrt((radii.X * radii.X + radii.Y * radii.Y) * 0.5f);
        var count = Math.Clamp((int)MathF.Ceiling(circumference / (2f * tileSize)), 64, 512);
        count = (count + 3) / 4 * 4;
        // Circumscribe the ellipse so straight collision chords cannot cut back into the fitted hull.
        radii /= MathF.Cos(MathF.PI / count);
        var contour = new Vector2[count];
        for (var i = 0; i < count; i++)
        {
            var angle = i * MathF.Tau / count;
            contour[i] = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radii;
        }
        return new[] { contour };
    }
    /// <summary>Projects an impact onto the closest shield edge.</summary>
    public static Vector2 ClosestPoint(Vector2[][] contours, Vector2 point)
    {
        var closest = point;
        var distance = float.PositiveInfinity;
        foreach (var contour in contours)
        for (var i = 0; i < contour.Length; i++)
        {
            var a = contour[i];
            var edge = contour[(i + 1) % contour.Length] - a;
            var projected = a + edge * Math.Clamp(Vector2.Dot(point - a, edge) / edge.LengthSquared(), 0f, 1f);
            var candidate = Vector2.DistanceSquared(projected, point);
            if (candidate >= distance)
                continue;
            closest = projected;
            distance = candidate;
        }
        return closest;
    }
}
