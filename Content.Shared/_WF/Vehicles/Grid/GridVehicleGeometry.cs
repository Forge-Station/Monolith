using System;
using System.Numerics;
using Robust.Shared.Maths;
using PhysicsTransform = Robust.Shared.Physics.Transform;

namespace Content.Shared._WF.Vehicles.Grid;

/// <summary>Pure geometry for the grid mover: tiles under a footprint and moves between a grid's frame and the world.</summary>
public static class GridVehicleGeometry
{
    /// <summary>The tiles a box overlaps, as an inclusive index range; an edge lying on a tile boundary does not reach the next tile.</summary>
    public static bool TryGetTileRange(Box2 box, float tileSize, out Vector2i min, out Vector2i max)
    {
        min = default;
        max = default;

        if (tileSize <= 0f || !(box.Width > 0f) || !(box.Height > 0f))
            return false;

        min = new Vector2i((int) MathF.Floor(box.Left / tileSize), (int) MathF.Floor(box.Bottom / tileSize));
        max = new Vector2i((int) MathF.Ceiling(box.Right / tileSize) - 1, (int) MathF.Ceiling(box.Top / tileSize) - 1);
        return max.X >= min.X && max.Y >= min.Y;
    }

    /// <summary>The box a tile covers in its grid's frame.</summary>
    public static Box2 GetTileBox(Vector2i tile, float tileSize)
    {
        var origin = new Vector2(tile.X * tileSize, tile.Y * tileSize);
        return new Box2(origin, origin + new Vector2(tileSize, tileSize));
    }

    /// <summary>The area of a tile that a box covers; zero when they only touch.</summary>
    public static float GetOverlapArea(Box2 box, Vector2i tile, float tileSize)
    {
        var tileBox = GetTileBox(tile, tileSize);
        var width = MathF.Min(box.Right, tileBox.Right) - MathF.Max(box.Left, tileBox.Left);
        var height = MathF.Min(box.Top, tileBox.Top) - MathF.Max(box.Bottom, tileBox.Bottom);
        return width > 0f && height > 0f ? width * height : 0f;
    }

    /// <summary>
    /// Whether a box turned about a point overlaps an upright box in the same frame; boxes that only touch do not.
    /// <paramref name="local"/> is the turned box in its own axes, placed at <paramref name="position"/>.
    /// </summary>
    public static bool Overlaps(Vector2 position, Angle rotation, Box2 local, Box2 other)
    {
        var along = rotation.RotateVec(Vector2.UnitX);
        var across = rotation.RotateVec(Vector2.UnitY);

        var a = position + rotation.RotateVec(local.BottomLeft);
        var b = position + rotation.RotateVec(local.BottomRight);
        var c = position + rotation.RotateVec(local.TopRight);
        var d = position + rotation.RotateVec(local.TopLeft);

        // The upright box's axes: the turned box's extent is that of its corners.
        var min = Vector2.Min(Vector2.Min(a, b), Vector2.Min(c, d));
        var max = Vector2.Max(Vector2.Max(a, b), Vector2.Max(c, d));
        if (max.X <= other.Left || min.X >= other.Right || max.Y <= other.Bottom || min.Y >= other.Top)
            return false;

        // The turned box's axes: its own extent is its local bounds.
        return OverlapsOn(along, position, local.Left, local.Right, other) &&
               OverlapsOn(across, position, local.Bottom, local.Top, other);
    }

    private static bool OverlapsOn(Vector2 axis, Vector2 origin, float low, float high, Box2 other)
    {
        var p0 = Vector2.Dot(other.BottomLeft - origin, axis);
        var p1 = Vector2.Dot(other.BottomRight - origin, axis);
        var p2 = Vector2.Dot(other.TopRight - origin, axis);
        var p3 = Vector2.Dot(other.TopLeft - origin, axis);
        var min = MathF.Min(MathF.Min(p0, p1), MathF.Min(p2, p3));
        var max = MathF.Max(MathF.Max(p0, p1), MathF.Max(p2, p3));

        return max > low && min < high;
    }

    /// <summary>The cardinal facing nearest a rotation in the grid's frame; a tie goes to the X axis.</summary>
    public static Vector2i CardinalFromRotation(Angle rotation)
    {
        var facing = rotation.ToWorldVec();
        return MathF.Abs(facing.X) >= MathF.Abs(facing.Y)
            ? new Vector2i(MathF.Sign(facing.X), 0)
            : new Vector2i(0, MathF.Sign(facing.Y));
    }

    /// <summary>The axis-aligned world box around a box given in a grid's frame, where <paramref name="gridTransform"/> is the grid's world pose.</summary>
    public static Box2 ToWorldBounds(Box2 local, PhysicsTransform gridTransform)
    {
        var a = PhysicsTransform.Mul(gridTransform, local.BottomLeft);
        var b = PhysicsTransform.Mul(gridTransform, local.BottomRight);
        var c = PhysicsTransform.Mul(gridTransform, local.TopRight);
        var d = PhysicsTransform.Mul(gridTransform, local.TopLeft);

        var min = Vector2.Min(Vector2.Min(a, b), Vector2.Min(c, d));
        var max = Vector2.Max(Vector2.Max(a, b), Vector2.Max(c, d));
        return new Box2(min, max);
    }
}
