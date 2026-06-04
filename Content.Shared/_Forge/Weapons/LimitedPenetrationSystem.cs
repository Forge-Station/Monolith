using Content.Shared.Projectiles;

namespace Content.Shared._Forge.Weapons;

public sealed class LimitedPenetrationSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<LimitedPenetrationComponent, ProjectileHitEvent>(OnProjectileHit);
    }

    private void OnProjectileHit(Entity<LimitedPenetrationComponent> ent, ref ProjectileHitEvent args)
    {
        if (!TryComp<ProjectileComponent>(ent, out var projectile) || projectile.ProjectileSpent)
            return;

        ent.Comp.Hits++;
        Dirty(ent, ent.Comp);

        if (ent.Comp.Hits < ent.Comp.MaxHits)
            return;

        projectile.ProjectileSpent = true;
        Dirty(ent, projectile);
    }
}
