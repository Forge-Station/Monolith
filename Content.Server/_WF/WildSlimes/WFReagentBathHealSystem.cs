using Content.Shared._WF.WildSlimes;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;

namespace Content.Server._WF.WildSlimes;

/// <summary>Heals mobs with <see cref="WFReagentBathHealComponent"/> while they touch a pool or puddle of their reagent.</summary>
public sealed class WFReagentBathHealSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private DamageableSystem _damage = default!;
    [Dependency] private MobStateSystem _mob = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;

    private readonly HashSet<EntityUid> _contacts = new();

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WFReagentBathHealComponent, DamageableComponent>();
        while (query.MoveNext(out var uid, out var bath, out var damageable))
        {
            if (now < bath.NextCheck)
                continue;

            bath.NextCheck = now + bath.Interval;
            if (damageable.TotalDamage <= 0 || _mob.IsDead(uid) || !IsBathing((uid, bath)))
                continue;

            _damage.TryChangeDamage(uid, bath.Heal, true, false, damageable);
        }
    }

    /// <summary>Whether the mob touches anything holding one of its reagents.</summary>
    public bool IsBathing(Entity<WFReagentBathHealComponent> ent)
    {
        _contacts.Clear();
        _physics.GetContactingEntities(ent.Owner, _contacts);
        foreach (var contact in _contacts)
        {
            if (!TryComp<SolutionContainerManagerComponent>(contact, out var manager))
                continue;

            foreach (var (_, solution) in _solutions.EnumerateSolutions((contact, manager)))
            {
                foreach (var reagent in ent.Comp.Reagents)
                {
                    if (solution.Comp.Solution.ContainsPrototype(reagent.Id))
                        return true;
                }
            }
        }

        return false;
    }
}
