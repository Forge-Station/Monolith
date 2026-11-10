namespace Content.Server._WF.Hailing;

/// <summary>
/// Raised on each hail service on a responder's grid when it answers. Lines added are spoken after the greeting.
/// </summary>
[ByRefEvent]
public readonly record struct HailServiceGreetEvent(EntityUid? Ship, List<string> Lines);

/// <summary>
/// Raised on a per-caller hail service when its window opens on a helm for an actor. Ship is the helm's grid.
/// </summary>
[ByRefEvent]
public readonly record struct HailServiceOpenedEvent(EntityUid Helm, EntityUid? Ship, EntityUid Actor);
