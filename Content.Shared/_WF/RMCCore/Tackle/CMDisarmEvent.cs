namespace Content.Shared._WF.RMCCore.Tackle;

[ByRefEvent]
public record struct CMDisarmEvent(EntityUid User, bool Handled = false);
