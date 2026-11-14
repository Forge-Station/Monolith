using Content.Server._WF.CargoLoad;

namespace Content.Server.Physics.Controllers;

public sealed partial class MoverController
{
    [Dependency] private WFCargoLoadSystem _wfCargoLoad = default!;

    /// <summary>Share of thrust a grid's docked set keeps in space under its cargo.</summary>
    private float WfCargoSpaceFactor(EntityUid grid) => _wfCargoLoad.GetSpaceFactor(grid);
}
