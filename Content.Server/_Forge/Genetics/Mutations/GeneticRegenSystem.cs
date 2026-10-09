using Content.Shared._Forge.Genetics.Mutations;
using Content.Shared.Damage;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Timing;

namespace Content.Server._Forge.Genetics.Mutations;

public sealed class GeneticRegenSystem : EntitySystem
{
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GeneticRegenComponent, ComponentStartup>(OnStartup);
    }

    private void OnStartup(Entity<GeneticRegenComponent> ent, ref ComponentStartup args)
    {
        ent.Comp.NextHeal = _timing.CurTime + ent.Comp.Interval;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var query = EntityQueryEnumerator<GeneticRegenComponent, DamageableComponent>();
        while (query.MoveNext(out var uid, out var regen, out var damageable))
        {
            if (regen.NextHeal > _timing.CurTime)
                continue;

            regen.NextHeal = _timing.CurTime + regen.Interval;
            if (_mobState.IsDead(uid))
                continue;

            var heal = new DamageSpecifier();
            foreach (var (type, amount) in damageable.Damage.DamageDict)
            {
                if (amount <= 0)
                    continue;
                heal.DamageDict[type] = -Math.Min((float) amount, regen.HealPerTick);
            }

            if (heal.DamageDict.Count > 0)
                _damageable.TryChangeDamage(uid, heal, ignoreResistances: true, interruptsDoAfters: false);
        }
    }
}
