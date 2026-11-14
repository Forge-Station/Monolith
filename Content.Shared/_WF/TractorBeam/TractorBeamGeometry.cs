using System;
using System.Numerics;
using Robust.Shared.Maths;

namespace Content.Shared._WF.TractorBeam;

/// <summary>
/// The tractor collection cone and the hull clearance used only by its visible fan.
/// </summary>
public static class TractorBeamGeometry
{
    /// <summary>
    /// Clips one narrow triangle of the visible fan to an enclosing ellipse around the hull.
    /// Coordinates remain in world space; the envelope follows the target's local bounds and rotation.
    /// Collection still uses the complete cone, including the invisible hull clearance.
    /// </summary>
    public static bool ClipVisualSection(Vector2 start, Vector2 left, Vector2 right, Box2 localBounds,
        Matrix3x2 worldToLocal, float clearance, out Vector2 clippedLeft, out Vector2 clippedRight)
    {
        clippedLeft = clippedRight = start;
        if (!ValidBounds(localBounds) || !float.IsFinite(clearance) || clearance <= 0f)
            return false;

        var localStart = Vector2.Transform(start, worldToLocal);
        var localLeft = Vector2.Transform(left, worldToLocal);
        var localRight = Vector2.Transform(right, worldToLocal);
        if (!IsFinite(localStart) || !IsFinite(localLeft) || !IsFinite(localRight))
            return false;

        var leftFraction = HullEllipseRayFraction(localStart, localLeft, localBounds, clearance);
        var rightFraction = HullEllipseRayFraction(localStart, localRight, localBounds, clearance);
        if (leftFraction <= 0f || rightFraction <= 0f)
            return false;

        localLeft = Vector2.Lerp(localStart, localLeft, leftFraction);
        localRight = Vector2.Lerp(localStart, localRight, rightFraction);

        // Connecting samples on a convex exclusion curve cuts slightly into that curve. Keep
        // even a coarse segment outside the hull, including very close or narrow rotated ships.
        var safeBounds = localBounds.Enlarged(clearance * 0.25f);
        var scale = 1f;
        if (TriangleIntersectsBox(localStart, localLeft, localRight, safeBounds))
        {
            if (safeBounds.Contains(localStart))
                return false;

            var low = 0f;
            var high = 1f;
            for (var i = 0; i < 16; i++)
            {
                var middle = (low + high) * 0.5f;
                if (TriangleIntersectsBox(localStart, Vector2.Lerp(localStart, localLeft, middle),
                        Vector2.Lerp(localStart, localRight, middle), safeBounds))
                    high = middle;
                else
                    low = middle;
            }
            scale = low;
        }

        clippedLeft = Vector2.Lerp(start, left, leftFraction * scale);
        clippedRight = Vector2.Lerp(start, right, rightFraction * scale);
        return scale > 0f;
    }

    private static float HullEllipseRayFraction(Vector2 start, Vector2 end, Box2 bounds, float clearance)
    {
        // sqrt(2) encloses all four corners while preserving a long narrow hull's aspect ratio.
        // Adding clearance to both radii keeps a visible gap even at those corners.
        var radiusX = ((double) bounds.Right - bounds.Left) / Math.Sqrt(2.0) + clearance;
        var radiusY = ((double) bounds.Top - bounds.Bottom) / Math.Sqrt(2.0) + clearance;
        var centerX = ((double) bounds.Left + bounds.Right) * 0.5;
        var centerY = ((double) bounds.Bottom + bounds.Top) * 0.5;
        var dx = ((double) end.X - start.X) / radiusX;
        var dy = ((double) end.Y - start.Y) / radiusY;
        var ox = (start.X - centerX) / radiusX;
        var oy = (start.Y - centerY) / radiusY;
        var c = ox * ox + oy * oy - 1.0;
        if (c <= 0.0)
            return 0f;
        var a = dx * dx + dy * dy;
        var b = ox * dx + oy * dy;
        var discriminant = b * b - a * c;
        if (a <= 0.0 || discriminant < 0.0)
            return 1f;
        var fraction = (-b - Math.Sqrt(discriminant)) / a;
        return fraction >= 0.0 && fraction <= 1.0 ? (float) fraction : 1f;
    }

    private static bool TriangleIntersectsBox(Vector2 a, Vector2 b, Vector2 c, Box2 bounds)
    {
        return Overlaps(1.0, 0.0) && Overlaps(0.0, 1.0) &&
               Overlaps(a.Y - b.Y, b.X - a.X) && Overlaps(b.Y - c.Y, c.X - b.X) &&
               Overlaps(c.Y - a.Y, a.X - c.X);

        bool Overlaps(double x, double y)
        {
            var pa = x * a.X + y * a.Y;
            var pb = x * b.X + y * b.Y;
            var pc = x * c.X + y * c.Y;
            var minimum = x * (x >= 0 ? bounds.Left : bounds.Right) +
                          y * (y >= 0 ? bounds.Bottom : bounds.Top);
            var maximum = x * (x >= 0 ? bounds.Right : bounds.Left) +
                          y * (y >= 0 ? bounds.Top : bounds.Bottom);
            return Math.Min(pa, Math.Min(pb, pc)) <= maximum && minimum <= Math.Max(pa, Math.Max(pb, pc));
        }
    }

    /// <summary>
    /// Half the target hull's width projected across the beam, accounting for the hull's rotation.
    /// This remains the same as distance changes and is not capped for large ships.
    /// </summary>
    public static float GetTargetHalfWidth(Vector2 start, Vector2 end, Box2 localBounds, Matrix3x2 worldMatrix)
    {
        if (!IsFinite(start) || !IsFinite(end) || !ValidBounds(localBounds))
            return 0f;

        var dx = (double) end.X - start.X;
        var dy = (double) end.Y - start.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length <= 0.0)
            return 0f;

        var perpendicularX = -dy / length;
        var perpendicularY = dx / length;
        var extentX = ((double) localBounds.Right - localBounds.Left) * 0.5;
        var extentY = ((double) localBounds.Top - localBounds.Bottom) * 0.5;
        var width = extentX * Math.Abs(perpendicularX * worldMatrix.M11 + perpendicularY * worldMatrix.M12) +
                    extentY * Math.Abs(perpendicularX * worldMatrix.M21 + perpendicularY * worldMatrix.M22);
        return double.IsFinite(width) && width > 0.0 && width <= float.MaxValue ? (float) width : 0f;
    }

    /// <summary>
    /// Includes the fan edges and end cap, but excludes points behind the dish or beyond the target.
    /// </summary>
    public static bool ContainsPoint(Vector2 start, Vector2 end, Vector2 point, float halfWidth)
    {
        if (!IsFinite(point) || !TryGetCone(start, end, halfWidth, out var dx, out var dy, out var length))
            return false;

        var px = (double) point.X - start.X;
        var py = (double) point.Y - start.Y;
        var projected = px * dx + py * dy;
        var perpendicular = Math.Abs(px * dy - py * dx);
        return projected >= 0.0 && projected <= dx * dx + dy * dy &&
               perpendicular <= halfWidth * projected / length;
    }

    /// <summary>
    /// Tests the complete triangle against an axis-aligned world box, including edge contact.
    /// A grid can intersect the beam even when its center and all box corners lie outside it.
    /// </summary>
    public static bool IntersectsBox(Vector2 start, Vector2 end, Box2 worldBounds, float halfWidth)
    {
        if (!ValidBounds(worldBounds) ||
            !TryGetCone(start, end, halfWidth, out var dx, out var dy, out var length))
            return false;

        // Work relative to the dish in doubles, avoiding overflow for finite world coordinates.
        var left = (double) worldBounds.Left - start.X;
        var right = (double) worldBounds.Right - start.X;
        var bottom = (double) worldBounds.Bottom - start.Y;
        var top = (double) worldBounds.Top - start.Y;
        var offsetX = -dy / length * halfWidth;
        var offsetY = dx / length * halfWidth;
        var bx = dx + offsetX;
        var by = dy + offsetY;
        var cx = dx - offsetX;
        var cy = dy - offsetY;

        // Separating-axis test: two box normals and the three triangle edge normals.
        return Overlaps(1.0, 0.0) && Overlaps(0.0, 1.0) &&
               Overlaps(-by, bx) && Overlaps(-cy, cx) && Overlaps(dx, dy);

        bool Overlaps(double axisX, double axisY)
        {
            var b = axisX * bx + axisY * by;
            var c = axisX * cx + axisY * cy;
            var triangleMin = Math.Min(0.0, Math.Min(b, c));
            var triangleMax = Math.Max(0.0, Math.Max(b, c));
            var boxMin = axisX * (axisX >= 0.0 ? left : right) +
                         axisY * (axisY >= 0.0 ? bottom : top);
            var boxMax = axisX * (axisX >= 0.0 ? right : left) +
                         axisY * (axisY >= 0.0 ? top : bottom);
            return triangleMin <= boxMax && boxMin <= triangleMax;
        }
    }

    private static bool TryGetCone(Vector2 start, Vector2 end, float halfWidth,
        out double dx, out double dy, out double length)
    {
        dx = dy = length = 0.0;
        if (!IsFinite(start) || !IsFinite(end) || !float.IsFinite(halfWidth) || halfWidth <= 0f)
            return false;

        dx = (double) end.X - start.X;
        dy = (double) end.Y - start.Y;
        length = Math.Sqrt(dx * dx + dy * dy);
        if (length <= 0.0)
            return false;

        return true;
    }

    private static bool ValidBounds(Box2 bounds)
    {
        return float.IsFinite(bounds.Left) && float.IsFinite(bounds.Right) &&
               float.IsFinite(bounds.Bottom) && float.IsFinite(bounds.Top) &&
               bounds.Left <= bounds.Right && bounds.Bottom <= bounds.Top;
    }

    private static bool IsFinite(Vector2 vector)
    {
        return float.IsFinite(vector.X) && float.IsFinite(vector.Y);
    }
}
