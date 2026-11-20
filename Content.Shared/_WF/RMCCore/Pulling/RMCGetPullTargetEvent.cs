namespace Content.Shared._WF.RMCCore.Pulling;

[ByRefEvent]
public record struct RMCGetPullTargetEvent(EntityUid User, EntityUid Target);
