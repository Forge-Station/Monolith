namespace Content.Shared._WF.RMCCore.Projectiles;

/// <summary>
/// Used in RMC before accuracy would be calculated, for guaranteed projectile dodges etc
/// </summary>
[ByRefEvent]
public record struct RMCBeforeProjectileAccuracyEvent(EntityUid Projectile, bool GuaranteedMiss = false);
