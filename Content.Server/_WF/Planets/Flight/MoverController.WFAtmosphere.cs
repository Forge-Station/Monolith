using Content.Server._CE.ZLevels.Core;

namespace Content.Server.Physics.Controllers;

public sealed partial class MoverController
{
    [Dependency] private CEZLevelsSystem _wfFlightLevels = default!;

    /// <summary>Thrust left for manoeuvres: after supporting the ship's weight in atmosphere, or after its cargo load in space.</summary>
    private float WfAtmosphereManeuvering(EntityUid grid) =>
        _thruster.WfInAtmosphere(grid) ? _wfFlightLevels.WfManeuveringFactor(grid) : WfCargoSpaceFactor(grid);
}
