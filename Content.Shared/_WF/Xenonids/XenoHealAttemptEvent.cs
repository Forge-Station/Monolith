namespace Content.Shared._WF.Xenonids;

[ByRefEvent]
public record struct XenoHealAttemptEvent(bool Cancelled = false);
