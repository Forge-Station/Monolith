using Robust.Shared.Map;

namespace Content.Shared._WF.Explosion;

/// <summary>
/// Raised once, broadcast, when an explosion starts and its size is known.
/// </summary>
/// <param name="Epicenter">Where the explosion went off.</param>
/// <param name="Iterations">How many steps the blast expanded through, which is one tile of radius each.</param>
/// <param name="Cause">The entity that exploded, if there was one.</param>
[ByRefEvent]
public readonly record struct ExplosionShockwaveEvent(MapCoordinates Epicenter, int Iterations, EntityUid? Cause);
