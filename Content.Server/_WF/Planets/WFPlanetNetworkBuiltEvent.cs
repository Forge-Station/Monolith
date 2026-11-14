using Content.Shared._WF.Planets;

namespace Content.Server._WF.Planets;

/// <summary>Raised broadcast last in BuildNetwork, after InitializeZNetwork re-stamped network components.</summary>
[ByRefEvent]
public readonly record struct WFPlanetNetworkBuiltEvent(
    EntityUid Network, EntityUid Ground, WFPlanetSurfacePrototype Surface, IReadOnlyList<EntityUid> Lower);
