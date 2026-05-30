namespace Content.Shared._Forge.BoardingTeleport;

/// <summary>
/// Raised on the server when the console requests a synchronized volley from all ready platforms.
/// </summary>
public readonly record struct BoardingTeleportSyncVolleyEvent(EntityUid ConsoleUid);
