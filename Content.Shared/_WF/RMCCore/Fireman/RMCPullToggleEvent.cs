namespace Content.Shared._WF.RMCCore.Fireman;

[ByRefEvent]
public record struct RMCPullToggleEvent(bool Handled = false);
