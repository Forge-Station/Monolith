namespace Content.Shared._WF.Xenobiology;

/// <summary>Raised on a slime before it grows a stage or splits; cancel it to hold the slime as it is.</summary>
[ByRefEvent]
public record struct SlimeGrowthAttemptEvent(bool Cancelled = false);
