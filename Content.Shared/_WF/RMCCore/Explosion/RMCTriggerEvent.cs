namespace Content.Shared._WF.RMCCore.Explosion;

[ByRefEvent]
public record struct RMCTriggerEvent(EntityUid? User, bool Handled);
