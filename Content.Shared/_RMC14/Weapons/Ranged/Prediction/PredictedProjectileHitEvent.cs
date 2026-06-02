using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._RMC14.Weapons.Ranged.Prediction;

[Serializable, NetSerializable]
public sealed class PredictedProjectileHitEvent(int projectile, NetEntity hitTarget, MapCoordinates hitCoordinates)
    : EntityEventArgs
{
    public readonly int Projectile = projectile;
    public readonly NetEntity HitTarget = hitTarget;
    public readonly MapCoordinates HitCoordinates = hitCoordinates;
}
