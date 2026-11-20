namespace Content.Shared._WF.ShipPa;

/// <summary>
/// A one-off ship-wide track started with StartTrack has played through to its end and stopped itself.
/// Raised on the server only; whoever started the track uses it to clean up after it.
/// </summary>
[ByRefEvent]
public readonly record struct ShipPaTrackFinishedEvent(EntityUid Grid, string Key);
