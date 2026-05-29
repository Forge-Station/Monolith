namespace Content.Shared._Forge.BoardingTeleport;

/// <summary>
/// Single source for boarding teleport delay, scatter, risk, and distance scaling.
/// </summary>
public static class BoardingTeleportBalance
{
    public static float GetModeDelayMultiplier(BoardingTeleportInsertionMode mode) => mode switch
    {
        BoardingTeleportInsertionMode.Precise => BoardingTeleportConstants.PreciseDelayMultiplier,
        BoardingTeleportInsertionMode.Rapid => BoardingTeleportConstants.RapidDelayMultiplier,
        _ => BoardingTeleportConstants.StealthDelayMultiplier,
    };

    public static float GetModeScatterRadius(BoardingTeleportInsertionMode mode, bool experimentalScatterControl = false)
    {
        var radius = mode switch
        {
            BoardingTeleportInsertionMode.Precise => BoardingTeleportConstants.PreciseScatter,
            BoardingTeleportInsertionMode.Rapid => BoardingTeleportConstants.RapidScatter,
            _ => BoardingTeleportConstants.StealthScatter,
        };

        if (experimentalScatterControl)
            radius *= BoardingTeleportConstants.ExperimentalScatterMultiplier;

        return radius;
    }

    public static float GetModeBaseRisk(BoardingTeleportInsertionMode mode, bool experimental = false)
    {
        var risk = mode switch
        {
            BoardingTeleportInsertionMode.Precise => BoardingTeleportConstants.PreciseRisk,
            BoardingTeleportInsertionMode.Rapid => BoardingTeleportConstants.RapidRisk,
            _ => BoardingTeleportConstants.StealthRisk,
        };

        if (experimental)
            risk *= BoardingTeleportConstants.ExperimentalRiskMultiplier;

        return risk;
    }

    public static float GetDistanceScale(float distanceMeters)
    {
        return Math.Clamp(
            1f + distanceMeters / BoardingTeleportConstants.DistanceUnitsPerScaleStep,
            1f,
            BoardingTeleportConstants.MaxDistanceScale);
    }

    public static float ComputeDepartureDelay(
        float platformDepartureDelay,
        BoardingTeleportInsertionMode mode,
        float distanceScale,
        bool returning)
    {
        if (returning)
            return platformDepartureDelay;

        return MathF.Max(
            BoardingTeleportConstants.MinDepartureDelay,
            platformDepartureDelay * GetModeDelayMultiplier(mode) * MathF.Max(1f, distanceScale));
    }

    public static float ComputeScatterRadius(
        BoardingTeleportInsertionMode mode,
        float distanceScale,
        bool experimentalScatterControl = false)
    {
        return GetModeScatterRadius(mode, experimentalScatterControl) * MathF.Max(1f, distanceScale);
    }

    public static float ComputeDisplayedRiskPercent(
        BoardingTeleportInsertionMode mode,
        float distanceScale,
        bool experimental = false)
    {
        return ComputeDestabilizationChance(mode, distanceScale, experimental, apcReceivedRatio: 1f) * 100f;
    }

    public static float ComputeDestabilizationChance(
        BoardingTeleportInsertionMode mode,
        float distanceScale,
        bool experimental,
        float? apcReceivedRatio,
        float targetLinearVelocity = 0f,
        float targetAngularVelocity = 0f,
        float maxTargetVelocity = 0.05f,
        float maxTargetAngularVelocity = 0.01f)
    {
        var chance = GetModeBaseRisk(mode, experimental)
                     * MathF.Max(1f, distanceScale * BoardingTeleportConstants.RiskDistanceScaleFactor);

        if (maxTargetVelocity > 0.01f)
            chance += Math.Clamp(targetLinearVelocity / maxTargetVelocity * 0.22f, 0f, 0.22f);

        if (maxTargetAngularVelocity > 0.001f)
            chance += Math.Clamp(MathF.Abs(targetAngularVelocity) / maxTargetAngularVelocity * 0.16f, 0f, 0.16f);

        if (apcReceivedRatio is { } ratio && ratio < 1f)
            chance += (1f - ratio) * BoardingTeleportConstants.ApcUnderloadRiskFactor;

        return Math.Clamp(chance, BoardingTeleportConstants.MinDestabilizationChance, BoardingTeleportConstants.MaxDestabilizationChance);
    }

    public static float? GetApcReceivedRatio(float powerReceived, float load)
    {
        if (load <= 0.01f)
            return null;

        return powerReceived / load;
    }

    public static float GetApcRiskBonus(float? apcReceivedRatio)
    {
        if (apcReceivedRatio is not { } ratio || ratio >= 1f)
            return 0f;

        return (1f - ratio) * BoardingTeleportConstants.ApcUnderloadRiskFactor;
    }

    public static float ApplyLockScatterPenalty(float scatterRadius, float lockScatterPenalty)
    {
        return scatterRadius + MathF.Max(0f, lockScatterPenalty);
    }

    public static float ApplyScramblerScatterBonus(float scatterRadius, float scramblerScatterBonus)
    {
        return scatterRadius + MathF.Max(0f, scramblerScatterBonus);
    }

    public static float ApplyLockRiskPenalty(float chance, float lockRiskPenalty)
    {
        return Math.Clamp(chance + MathF.Max(0f, lockRiskPenalty), BoardingTeleportConstants.MinDestabilizationChance, BoardingTeleportConstants.MaxDestabilizationChance);
    }

    public static float ApplyScramblerRiskBonus(float chance, float scramblerRiskBonus)
    {
        return Math.Clamp(chance + MathF.Max(0f, scramblerRiskBonus), BoardingTeleportConstants.MinDestabilizationChance, BoardingTeleportConstants.MaxDestabilizationChance);
    }

    public static bool RequiresEarlyReturnConfirm(float remainingSeconds, float totalWindowSeconds)
    {
        if (totalWindowSeconds <= 0.01f)
            return false;

        return remainingSeconds / totalWindowSeconds > BoardingTeleportConstants.EarlyReturnSafeRemainingFraction;
    }

    public static float ComputeEarlyReturnExplosionRisk(float remainingSeconds, float totalWindowSeconds)
    {
        if (totalWindowSeconds <= 0.01f)
            return BoardingTeleportConstants.EarlyReturnMaxExplosionRisk;

        var remainingFraction = Math.Clamp(remainingSeconds / totalWindowSeconds, 0f, 1f);
        if (remainingFraction <= BoardingTeleportConstants.EarlyReturnSafeRemainingFraction)
            return BoardingTeleportConstants.EarlyReturnMinExplosionRisk;

        var earlySpan = 1f - BoardingTeleportConstants.EarlyReturnSafeRemainingFraction;
        var earlyAmount = (remainingFraction - BoardingTeleportConstants.EarlyReturnSafeRemainingFraction) / earlySpan;
        return MathF.Min(
            BoardingTeleportConstants.EarlyReturnMaxExplosionRisk,
            BoardingTeleportConstants.EarlyReturnMinExplosionRisk +
            (BoardingTeleportConstants.EarlyReturnMaxExplosionRisk - BoardingTeleportConstants.EarlyReturnMinExplosionRisk) * earlyAmount);
    }
}
