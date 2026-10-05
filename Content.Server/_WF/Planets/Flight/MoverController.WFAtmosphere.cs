using Content.Server._CE.ZLevels.Core;

namespace Content.Server.Physics.Controllers;

public sealed partial class MoverController
{
    [Dependency] private CEZLevelsSystem _wfFlightLevels = default!;

    private float WfAtmosphereManeuvering(EntityUid grid) => _wfFlightLevels.WfManeuveringFactor(grid);
}
