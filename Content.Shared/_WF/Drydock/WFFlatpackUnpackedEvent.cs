namespace Content.Shared._WF.Drydock;

/// <summary>Raised on a flatpack, on the server, once it has unpacked into <see cref="Spawned"/>.</summary>
[ByRefEvent]
public readonly record struct WFFlatpackUnpackedEvent(EntityUid Spawned);
