namespace Content.Shared.DeviceLinking.Events;

/// <summary>
/// Raised on both sides of a device link when a port is disconnected.
/// Local systems still listen for this alongside the source and sink events.
/// </summary>
[ByRefEvent]
public readonly record struct PortDisconnectedEvent(string Port);
