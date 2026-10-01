namespace Content.Shared._WF.Wolfmed.Compat;

/// <summary>Raised on a part when it gains a body. Onyx shape.</summary>
[ByRefEvent]
public readonly record struct OrganGotInsertedEvent(EntityUid Target);

/// <summary>Raised on a part when it loses a body. Onyx shape.</summary>
[ByRefEvent]
public readonly record struct OrganGotRemovedEvent(EntityUid Target);
