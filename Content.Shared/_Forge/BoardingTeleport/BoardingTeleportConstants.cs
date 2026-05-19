namespace Content.Shared._Forge.BoardingTeleport;

public static class BoardingTeleportConstants
{
    public const float DefaultRange = 300f;
    public const float DefaultMaxTargetVelocity = 50f;
    public const float DefaultMaxTargetAngularVelocity = 15f;

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

    public const int ScatterSampleAttempts = 16;
    public const int PhaseShiftNeighborAttempts = 8;
}
