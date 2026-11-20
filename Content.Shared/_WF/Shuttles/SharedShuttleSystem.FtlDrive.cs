using Content.Shared._WF.Lattice;
using Content.Shared._WF.Shuttles;

namespace Content.Shared.Shuttles.Systems;

public abstract partial class SharedShuttleSystem
{
    /// <summary>
    /// Reports whether a ship can jump, by the two checks <see cref="GetFTLRange"/> makes: a drive exists, and the one
    /// it picks is powered; and that the Lattice band it is in lets drives work.
    /// </summary>
    public WFFtlDriveStatus GetFtlDriveStatus(EntityUid shuttleUid)
    {
        if (!TryGetFTLDrive(shuttleUid, out var drive, out _))
            return WFFtlDriveStatus.Missing;

        if (TryComp<WFDriftComponent>(shuttleUid, out var drift) && drift.NoFtl)
            return WFFtlDriveStatus.Unmoored;

        return _powerReceiverSystem.IsPowered(drive.Value) ? WFFtlDriveStatus.Ready : WFFtlDriveStatus.Unpowered;
    }
}
