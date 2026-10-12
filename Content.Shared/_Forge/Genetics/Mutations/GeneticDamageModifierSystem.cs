using Content.Shared.Damage;
using Content.Shared.Damage.Systems;

namespace Content.Shared._Forge.Genetics.Mutations;

public sealed class GeneticDamageModifierSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GeneticDamageModifierComponent, DamageModifyEvent>(OnModify);
    }

    private void OnModify(Entity<GeneticDamageModifierComponent> ent, ref DamageModifyEvent args)
    {
        args.Damage *= ent.Comp.Coefficient;
    }
}
