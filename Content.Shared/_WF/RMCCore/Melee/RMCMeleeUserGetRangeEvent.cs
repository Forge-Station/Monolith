namespace Content.Shared._WF.RMCCore.Melee;

[ByRefEvent]
public record struct RMCMeleeUserGetRangeEvent(EntityUid? Target, float Range);
