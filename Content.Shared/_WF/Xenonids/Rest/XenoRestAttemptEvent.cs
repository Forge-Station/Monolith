namespace Content.Shared._WF.Xenonids.Rest;

[ByRefEvent]
public record struct XenoRestAttemptEvent(bool Cancelled);
