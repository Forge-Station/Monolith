using Content.Shared._WF.Shuttles;

namespace Content.Shared.Shuttles.Systems;

public abstract partial class SharedShuttleSystem
{
    /// <summary>
    /// Reports whether a ship can jump, by the two checks <see cref="GetFTLRange"/> makes: a drive exists, and the one
    /// it picks is powered.
    /// </summary>
    public WFFtlDriveStatus GetFtlDriveStatus(EntityUid shuttleUid)
    {
        if (!TryGetFTLDrive(shuttleUid, out var drive, out _))
            return WFFtlDriveStatus.Missing;

        return _powerReceiverSystem.IsPowered(drive.Value) ? WFFtlDriveStatus.Ready : WFFtlDriveStatus.Unpowered;
    }
}
