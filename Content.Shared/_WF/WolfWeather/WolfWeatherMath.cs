using System.Numerics;
using Robust.Shared.Random;

namespace Content.Shared._WF.WolfWeather;

/// <summary>Random and smoothing helpers for weather.</summary>
public static class WolfWeatherMath
{
    /// <summary>A random value from range X to range Y, where Y below X counts as X.</summary>
    public static float Roll(this IRobustRandom random, Vector2 range)
    {
        return random.NextFloat(range.X, MathF.Max(range.X, range.Y));
    }

    /// <summary>The share of the way to a target to move this frame, easing with time constant tau.</summary>
    public static float Smooth(float frameTime, float tau)
    {
        return 1f - MathF.Exp(-frameTime / tau);
    }
}
