namespace Content.Shared._WF.RMCCore.Actions;

[ByRefEvent]
public record struct RMCActionUseAttemptEvent(EntityUid User, EntityUid? Target, bool Cancelled = false);
