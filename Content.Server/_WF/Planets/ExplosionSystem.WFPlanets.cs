using Content.Server._WF.Planets;

namespace Content.Server.Explosion.EntitySystems;

public sealed partial class ExplosionSystem
{
    [Dependency] private WFPlanetTileBreakSystem _wfPlanetTileBreak = default!;
}
