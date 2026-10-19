namespace Content.Shared._WF.RMCCore.OnCollide;

[ByRefEvent]
public readonly record struct DamageCollideEvent(EntityUid Target);
