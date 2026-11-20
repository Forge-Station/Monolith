using Content.Shared._Mono.SpaceArtillery;
using Content.Shared._Mono.Weapons.Ranged.Components;

namespace Content.Server.Explosion.EntitySystems;

public sealed partial class ProjectileGrenadeSystem
{
    /// <summary>Keeps deployed ship fragments and mines phased through their originating hull and field.</summary>
    private void InheritWolfgateShipProjectileSource(EntityUid parent, EntityUid projectile)
    {
        if (!HasComp<ShipWeaponProjectileComponent>(projectile) ||
            !TryComp<ProjectileGridPhaseComponent>(parent, out var source) || source.SourceGrid is not { } grid)
            return;
        var phase = EnsureComp<ProjectileGridPhaseComponent>(projectile);
        phase.SourceGrid = grid;
        Dirty(projectile, phase);
    }
}
