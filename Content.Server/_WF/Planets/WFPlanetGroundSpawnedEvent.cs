using Content.Shared._WF.Planets;

namespace Content.Server._WF.Planets;

/// <summary>Raised broadcast once a planet's ground map exists, before any of it generates.</summary>
[ByRefEvent]
public readonly record struct WFPlanetGroundSpawnedEvent(EntityUid Ground, WFPlanetSurfacePrototype Surface);
