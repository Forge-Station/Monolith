namespace Content.Shared._WF.Vehicles.Core;

public sealed partial class VehicleLockSystem
{
    /// <summary>Gives a vehicle a new key id, so no old key fits it, and leaves its lock unlocked and whole.</summary>
    public string Rekey(EntityUid vehicle)
    {
        var lockComp = EnsureComp<VehicleLockComponent>(vehicle);
        StopBreakAlarm(lockComp);
        lockComp.KeyId = Guid.NewGuid().ToString("N");
        lockComp.Locked = false;
        lockComp.Broken = false;
        Dirty(vehicle, lockComp);
        RefreshLockAction(vehicle, lockComp);
        return lockComp.KeyId;
    }
}
