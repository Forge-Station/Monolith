namespace Content.Shared._WF.AsteroidMining;

/// <summary>
/// The bands of a rock's heat. Below its window nothing happens; in the window it cracks cleanly; between the window
/// and the overcharge line it cracks faster but loses ore; held past the line it blows up.
/// </summary>
public static class WFAsteroidHeat
{
    /// <summary>Heat at which a rock starts to overcharge.</summary>
    public const float OverchargeLine = 0.85f;

    /// <summary>Seconds over the line before a rock blows up.</summary>
    public const float OverchargeDuration = 2f;

    /// <summary>Crack speed in the hot band, relative to the window.</summary>
    public const float HotCrackSpeed = 1.5f;

    /// <summary>Share of the ore lost if the whole crack is spent in the hot band.</summary>
    public const float HotYieldLoss = 0.5f;

    /// <summary>Heat at the right-hand end of the meter.</summary>
    public const float MeterScale = 1.1f;

    /// <summary>The share of a rock's mass a clean crack keeps, from how much of its progress was made hot.</summary>
    public static float Yield(float progress, float hotProgress)
    {
        return progress <= 0f ? 1f : 1f - HotYieldLoss * Math.Clamp(hotProgress / progress, 0f, 1f);
    }
}
