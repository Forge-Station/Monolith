namespace Content.Shared._WF.RMCCore.Pulling;

[ByRefEvent]
public record struct PullSlowdownAttemptEvent(EntityUid Target, bool Cancelled = false);
