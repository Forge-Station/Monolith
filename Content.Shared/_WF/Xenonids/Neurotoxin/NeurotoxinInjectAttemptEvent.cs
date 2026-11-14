namespace Content.Shared._WF.Xenonids.Neurotoxin;

[ByRefEvent]
public record struct NeurotoxinInjectAttemptEvent(bool Cancelled = false);
