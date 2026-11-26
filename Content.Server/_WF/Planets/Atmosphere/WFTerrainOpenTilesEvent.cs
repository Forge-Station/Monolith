using Content.Shared.Maps;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Planets.Atmosphere;

/// <summary>
/// Raised broadcast while a planet layer gets its terrain atmosphere, so other modules can name more of the ground
/// they lay on it as bare ground. Anything not named counts as built and holds its own air.
/// </summary>
[ByRefEvent]
public readonly record struct WFTerrainOpenTilesEvent(EntityUid Map, HashSet<ProtoId<ContentTileDefinition>> Tiles);
