namespace Content.Shared._WF.Xenonids.Stomp;

[ByRefEvent]
public record struct XenoStompAttemptEvent(bool Cancelled);
