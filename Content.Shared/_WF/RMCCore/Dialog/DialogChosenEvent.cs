namespace Content.Shared._WF.RMCCore.Dialog;

[ByRefEvent]
public readonly record struct DialogChosenEvent(EntityUid Actor, int Index);
