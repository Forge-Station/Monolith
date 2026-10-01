namespace Content.Shared._WF.Wolfmed.Consciousness;

/// <summary>
/// M2 (plan §5.2): how deep the dying view draws an unconscious body, from how far its active route has got toward
/// arrest rather than from a pressure past 1 (which never happened: the old term was dead code). Downed carries the
/// first part of the view, Unconscious the rest.
/// </summary>
public static class WolfmedDyingDepth
{
    /// <summary>Where the Unconscious band starts; 1 is the arrest line of the worst route.</summary>
    public const float UnconsciousStart = 0.55f;

    /// <summary>
    /// 0 at the Unconscious line, 1 at the arrest line, for whichever route is further along: blood from its
    /// Unconscious line to its arrest line, brain oxygenation from its Unconscious line to its arrest line.
    /// </summary>
    public static float RouteProgress(float blood, float bloodOut, float bloodArrest, float oxygenation, float oxygenOut,
        float oxygenArrest)
    {
        var bloodProgress = bloodOut > bloodArrest ? (bloodOut - blood) / (bloodOut - bloodArrest) : 0f;
        var oxygenProgress = oxygenOut > oxygenArrest ? (oxygenOut - oxygenation) / (oxygenOut - oxygenArrest) : 0f;
        return Math.Clamp(MathF.Max(bloodProgress, oxygenProgress), 0f, 1f);
    }

    /// <summary>The view's depth for an unconscious body at this route progress.</summary>
    public static float Unconscious(float progress) =>
        UnconsciousStart + (1f - UnconsciousStart) * Math.Clamp(progress, 0f, 1f);
}
