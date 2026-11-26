using Content.Shared._WF.PlanetPois;
using Content.Shared._WF.Planets;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.PlanetPois;

/// <summary>
/// Raised broadcast as a world's sites are drawn from its table, for other modules to add sites of their own this round.
/// Added sites are placed first, before the table's.
/// </summary>
[ByRefEvent]
public readonly record struct WFPlanetPoiExtraSitesEvent(EntityUid Ground, ProtoId<WFPlanetSurfacePrototype> Surface, List<WFPlanetPoiEntry> Sites);
