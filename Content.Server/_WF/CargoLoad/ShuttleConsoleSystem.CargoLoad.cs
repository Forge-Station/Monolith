using Content.Server._WF.CargoLoad;
using Content.Shared._Mono.Ships;

namespace Content.Server.Shuttles.Systems;

public sealed partial class ShuttleConsoleSystem
{
    [Dependency] private WFCargoLoadSystem _wfCargoLoad = default!;

    /// <summary>Seconds of FTL spool the cargo aboard adds to a jump.</summary>
    private float WfCargoSpoolPenalty(EntityUid shuttle, FTLDriveComponent drive) => _wfCargoLoad.SpoolPenalty(shuttle, drive);
}
