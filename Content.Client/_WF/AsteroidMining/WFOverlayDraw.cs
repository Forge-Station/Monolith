using System.Numerics;
using Robust.Client.Graphics;

namespace Content.Client._WF.AsteroidMining;

/// <summary>Drawing helpers shared by the asteroid mining overlays.</summary>
public static class WFOverlayDraw
{
    private static readonly Dictionary<int, Vector2[]> Circles = new();

    /// <summary>A disc fading from its centre colour to clear at its rim, with one side per three vertices of the buffer.</summary>
    public static void Glow(DrawingHandleWorld handle, DrawVertexUV2DColor[] fan, Vector2 center, float radius, Color color)
    {
        var sides = fan.Length / 3;
        var circle = UnitCircle(sides);
        var clear = color.WithAlpha(0f);
        var count = 0;
        for (var i = 0; i < sides; i++)
        {
            fan[count++] = new DrawVertexUV2DColor(center, color);
            fan[count++] = new DrawVertexUV2DColor(center + circle[i] * radius, clear);
            fan[count++] = new DrawVertexUV2DColor(center + circle[i + 1] * radius, clear);
        }

        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, Texture.White,
            new ReadOnlySpan<DrawVertexUV2DColor>(fan, 0, count));
    }

    /// <summary>
    /// A band from <paramref name="inner"/> to <paramref name="outer"/> that fades from its inner edge to clear at its
    /// outer edge, with one side per two vertices of the buffer, less one.
    /// </summary>
    public static void Ring(DrawingHandleWorld handle, DrawVertexUV2DColor[] strip, Vector2 center, float inner,
        float outer, Color color)
    {
        var sides = strip.Length / 2 - 1;
        var circle = UnitCircle(sides);
        var clear = color.WithAlpha(0f);
        var count = 0;
        for (var i = 0; i <= sides; i++)
        {
            strip[count++] = new DrawVertexUV2DColor(center + circle[i] * outer, clear);
            strip[count++] = new DrawVertexUV2DColor(center + circle[i] * inner, color);
        }

        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleStrip, Texture.White,
            new ReadOnlySpan<DrawVertexUV2DColor>(strip, 0, count));
    }

    /// <summary>Points round a unit circle, the first repeated at the end, cached per side count.</summary>
    private static Vector2[] UnitCircle(int sides)
    {
        if (Circles.TryGetValue(sides, out var circle))
            return circle;

        circle = new Vector2[sides + 1];
        for (var i = 0; i <= sides; i++)
        {
            var angle = MathF.Tau * i / sides;
            circle[i] = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
        }

        Circles[sides] = circle;
        return circle;
    }

    /// <summary>A cheap repeatable pseudo-random value in [0, 1) for a seed.</summary>
    public static float Hash(float value)
    {
        return Frac(MathF.Sin(value * 12.9898f) * 43758.5453f);
    }

    /// <summary>The fractional part of a value, in [0, 1) even for negatives.</summary>
    public static float Frac(float value)
    {
        return value - MathF.Floor(value);
    }
}
