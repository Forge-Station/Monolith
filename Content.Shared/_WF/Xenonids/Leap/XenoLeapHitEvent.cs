namespace Content.Shared._WF.Xenonids.Leap;

[ByRefEvent]
public readonly record struct XenoLeapHitEvent(XenoLeapingComponent Leaping, EntityUid Hit);
