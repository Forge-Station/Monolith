using System.Numerics;
using Robust.Shared.Map.Components;

namespace Content.Shared._Crescent.ShipShields;

/// <summary>
/// Forge-Change: shared math for shield oval outlines (server fixture + client radar draw).
/// </summary>
public static class ShipShieldOutline
{
    public const int DefaultSegments = 64;

    /// <summary>
    /// Builds a closed loop of vertices for the shield oval around a grid center, matching server physics.
    /// </summary>
    public static Vector2[] GetVertices(MapGridComponent mapGrid, float padding, int segments = DefaultSegments)
    {
        var aabb = mapGrid.LocalAABB;
        var height = aabb.Height + padding;
        var width = aabb.Width + padding;

        float radius;
        float scale;
        var scaleX = true;

        if (width > height)
        {
            radius = 0.5f * height;
            scale = width / height;
        }
        else
        {
            radius = 0.5f * width;
            scale = height / width;
            scaleX = false;
        }

        var center = aabb.Center;
        var vertices = new Vector2[segments + 1];

        for (var i = 0; i <= segments; i++)
        {
            var angle = i * MathF.Tau / segments;
            var v = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
            if (scaleX)
                v.X *= scale;
            else
                v.Y *= scale;
            vertices[i] = center + v;
        }

        return vertices;
    }
}
