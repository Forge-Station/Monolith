using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Vehicles.Weapons.BulletBox;

/// <summary>Ends the do-after of moving rounds between a box and a magazine or gun.</summary>
[Serializable, NetSerializable]
public sealed partial class BulletBoxTransferDoAfterEvent : SimpleDoAfterEvent
{
    public readonly bool ToBox;

    public BulletBoxTransferDoAfterEvent(bool toFrom)
    {
        ToBox = toFrom;
    }
}
