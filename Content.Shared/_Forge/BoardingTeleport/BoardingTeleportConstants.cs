namespace Content.Shared._Forge.BoardingTeleport;

public static class BoardingTeleportConstants
{
    public const float DefaultRange = 500f;
    public const float DefaultMaxTargetVelocity = 45f;
    public const float DefaultMaxTargetAngularVelocity = 45f;

    public const int MaxPlatformLandingSlots = 4;

    public const float StealthDelayMultiplier = 1.15f;
    public const float PreciseDelayMultiplier = 1.6f;
    public const float RapidDelayMultiplier = 0.65f;
    public const float MinDepartureDelay = 2.5f;

    public const float DistanceUnitsPerScaleStep = 120f;
    public const float MaxDistanceScale = 4f;

    public const float StealthScatter = 1.20f;
    public const float PreciseScatter = 0.60f;
    public const float RapidScatter = 4.50f;

    public const float StealthRisk = 0.12f;
    public const float PreciseRisk = 0.06f;
    public const float RapidRisk = 0.30f;

    public const float MinDestabilizationChance = 0.02f;
    public const float MaxDestabilizationChance = 0.85f;
    public const float RiskDistanceScaleFactor = 0.8f;
    public const float ApcUnderloadRiskFactor = 0.28f;

    public const float ExperimentalRiskMultiplier = 1.35f;
    public const float ExperimentalScatterMultiplier = 1.25f;
    public const float ExperimentalVelocityToleranceMultiplier = 1.25f;

    public const int ScatterSampleAttempts = 16;
    public const int PhaseShiftNeighborAttempts = 8;

    public const float LockDegradeIntervalSeconds = 30f;
    public const float LockDegradeScatterBonus = 0.20f;
    public const float LockDegradeRiskBonus = 0.03f;
    public const float LockMaxAgeSeconds = 180f;

    public const float DefaultEngineJumpCooldown = 60f;
    public const float EmergencyReturnDelay = 3f;
    public const float EmergencyReturnRisk = 0.40f;
    public const float EmergencyReturnScatter = 2.5f;

    /// <summary>
    /// When remaining return window is above this fraction, early return requires confirmation.
    /// </summary>
    public const float EarlyReturnSafeRemainingFraction = 0.15f;

    public const float EarlyReturnMinExplosionRisk = 0.05f;
    public const float EarlyReturnMaxExplosionRisk = 0.90f;

    public const float EarlyReturnCatastropheDelaySeconds = 5f;
    public const int EarlyReturnCatastropheSwellingSteps = 10;
    public const float EarlyReturnCatastropheMinScale = 1.15f;
    public const float EarlyReturnCatastropheMaxScale = 2.05f;

    public const float ScramblerDefaultScatterBonus = 1.5f;
    public const float ScramblerDefaultRiskBonus = 0.18f;

    public const float ChargeLockCheckIntervalSeconds = 1f;
    public const float DetectionBlipDurationSeconds = 45f;
}
