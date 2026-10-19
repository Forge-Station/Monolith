namespace Content.Shared._WF.Xenonids.Tracker;

/// <summary>Asks a trackable entity for the name trackers show; moved out of RMC's squad leader tracker.</summary>
[ByRefEvent]
public record struct RequestTrackableNameEvent(string? Name = null, bool Handled = false);
