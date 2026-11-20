namespace Content.Shared._WF.Xenonids.Charge;

[ByRefEvent]
public record struct XenoChargeAttemptEvent(bool Cancelled);
