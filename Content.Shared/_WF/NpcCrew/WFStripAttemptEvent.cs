namespace Content.Shared._WF.NpcCrew;

/// <summary>Raised on someone as another starts to strip them or put something on them.</summary>
[ByRefEvent]
public readonly record struct WFStripAttemptEvent(EntityUid User, bool Stealth);
