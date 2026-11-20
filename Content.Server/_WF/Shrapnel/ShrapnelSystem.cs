using Content.Server.Weapons.Ranged.Systems;
using Robust.Shared.Map;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Random;

namespace Content.Server._WF.Shrapnel;

/// <summary>
/// Fires the shrapnel of a <see cref="ShrapnelBehavior"/>.
/// </summary>
public sealed partial class ShrapnelSystem : EntitySystem
{
    [Dependency] private GunSystem _gun = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    /// <summary>
    /// Fires the shrapnel outward from the entity, one projectile at a random angle within each slice of the circle.
    /// </summary>
    public void Fling(EntityUid uid, ShrapnelBehavior behavior)
    {
        var origin = _transform.GetMapCoordinates(uid);
        if (origin.MapId == MapId.Nullspace)
            return;

        var velocity = _physics.GetMapLinearVelocity(uid);
        var slice = 360f / behavior.Count;

        for (var i = 0; i < behavior.Count; i++)
        {
            var direction = Angle.FromDegrees(slice * (i + _random.NextFloat())).ToVec();
            var shrapnel = Spawn(behavior.Proto, origin.Offset(direction * behavior.SpawnRadius));

            // Gun velocity is relative to the projectile's parent, so shrapnel keeps a moving ship's velocity.
            var gunVelocity = velocity - _physics.GetMapLinearVelocity(shrapnel)
                + _random.NextVector2(behavior.MinJitter, behavior.MaxJitter);

            _gun.ShootProjectile(shrapnel, direction, gunVelocity, uid, null, behavior.Speed);
        }
    }
}
