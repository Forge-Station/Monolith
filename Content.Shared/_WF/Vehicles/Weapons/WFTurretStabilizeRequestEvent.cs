using Robust.Shared.Serialization;

namespace Content.Shared._WF.Vehicles.Weapons;

/// <summary>The dashboard's turret lock: the gunner's selected turret holds its bearing, or turns with the hull.</summary>
[Serializable, NetSerializable]
public sealed class WFTurretStabilizeRequestEvent(NetEntity vehicle, bool stabilized) : EntityEventArgs
{
    public NetEntity Vehicle = vehicle;
    public bool Stabilized = stabilized;
}
