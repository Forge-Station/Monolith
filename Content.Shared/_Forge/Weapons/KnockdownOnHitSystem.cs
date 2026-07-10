using Content.Shared.Stunnable;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Random;

namespace Content.Shared._Forge.Weapons;

public sealed class KnockdownOnHitSystem : EntitySystem
{
    [Dependency] private readonly SharedStunSystem _stun = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<KnockdownOnHitComponent, MeleeHitEvent>(OnMeleeHit);
    }

    private void OnMeleeHit(EntityUid uid, KnockdownOnHitComponent knockdownOnHitComponent, MeleeHitEvent args)
    {
        if (!args.IsHit || args.HitEntities.Count == 0 || !_random.Prob(knockdownOnHitComponent.Chance))
            return;

        foreach (var target in args.HitEntities)
        {
            _stun.TryStun(target, TimeSpan.FromSeconds(knockdownOnHitComponent.Duration), true);
            _stun.TryKnockdown(target, TimeSpan.FromSeconds(knockdownOnHitComponent.Duration), true);
        }
    }
}
