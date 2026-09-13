using Content.Shared.Damage;
using Content.Shared.FixedPoint; // HardLight

namespace Content.Shared._FarHorizons.Damage;

public sealed class UniversalHealModifierSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<UniversalHealModifierComponent, HealModifyEvent>(OnHealModify);
    }

    private void OnHealModify(Entity<UniversalHealModifierComponent> ent, ref HealModifyEvent args)
    {
        // HardLight-edit start
        var damage = new DamageSpecifier(args.Damage);
        foreach (var (key, value) in args.Damage.DamageDict)
        {
            if (value < 0)
            {
                var modified = value * ent.Comp.Modifier;
                if (modified == FixedPoint2.Zero && ent.Comp.Modifier > 0f)
                    modified = -FixedPoint2.Epsilon;

                damage.DamageDict[key] = modified;
            }
        }
        // HardLight-edit end

        args.Damage = damage;
    }
}
