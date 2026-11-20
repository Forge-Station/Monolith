using Content.Server.Emp;
using Content.Server.Explosion.Components;
using Content.Shared.Explosion.Components;
using Content.Shared._Crescent.ShipShields;
using Content.Shared.Projectiles;
using Robust.Shared.Physics.Components;

namespace Content.Server._Crescent.ShipShields;

public sealed partial class ShipShieldsSystem
{
    [Dependency] private EmpSystem _wfShieldEmp = default!;

    /// <summary>Disarms pending payloads before an absorbed projectile is queued for deletion.</summary>
    private void DisarmWolfgateAbsorbedProjectile(EntityUid projectile)
    {
        RemComp<ActiveTimerTriggerComponent>(projectile);
        if (TryComp<TriggerOnProximityComponent>(projectile, out var proximity))
        {
            proximity.Enabled = false;
            proximity.Colliding.Clear();
        }
    }

    /// <summary>Preserves an absorbed warhead's EMP without activating its other payloads.</summary>
    private void PulseWolfgateAbsorbedEmp(EntityUid projectile, EmpOnTriggerComponent emp)
    {
        _wfShieldEmp.EmpPulse(_transformSystem.GetMapCoordinates(Transform(projectile)), emp.Range,
            emp.EnergyConsumption, TimeSpan.FromSeconds(emp.DisableDuration));
    }

    /// <summary>Consumes a shield contact before a collision-triggered warhead can detonate.</summary>
    public bool AbsorbWolfgateTriggerCollision(EntityUid shield, EntityUid projectile)
    {
        if (!IsWolfgateShieldLive(shield) || !TryComp<PhysicsComponent>(shield, out var physics) || !physics.CanCollide ||
            !TryComp<ShipShieldComponent>(shield, out var field) || !_shipWeaponProjectileQuery.HasComponent(projectile) ||
            !_projectileQuery.TryGetComponent(projectile, out var component) || IsWolfgateShieldFriendlyProjectile(shield, projectile))
            return false;
        if (component.ProjectileSpent)
            return true;
        var contact = new ProjectileReflectAttemptEvent(projectile, component, false);
        OnWolfgateShieldContact(shield, field, ref contact);
        return component.ProjectileSpent;
    }
}
