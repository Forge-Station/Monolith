namespace Content.Shared._WF.Xenonids.Weeds;

[ByRefEvent]
public readonly record struct AfterEntityWeedingEvent(EntityUid Weeds, EntityUid CoveredEntity);
