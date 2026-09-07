using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;

namespace Content.Shared._Mono.ArmorPlate;

/// <summary>
/// Handles armor plates installed directly into an entity.
/// This system is separate from SharedArmorPlateSystem and does not modify it.
/// </summary>
public sealed class BodyArmorPlateSystem : EntitySystem
{
    [Dependency] private readonly SharedArmorPlateSystem _armorPlate = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BodyArmorPlateComponent, BeforeDamageChangedEvent>(
            OnBeforeDamageChanged);
    }

    private void OnBeforeDamageChanged(
        Entity<BodyArmorPlateComponent> ent,
        ref BeforeDamageChangedEvent args)
    {
        if (args.Cancelled || !args.Damage.AnyPositive())
            return;

        if (args.Origin == null &&
            args.OriginFlag != DamageableSystem.DamageOriginFlag.Explosion)
            return;

        if (!TryComp<ArmorPlateHolderComponent>(ent, out var holder))
            return;

        if (!_armorPlate.TryGetActivePlate((ent.Owner, holder), out var plate))
            return;

        _armorPlate.CalcPlateDamages(
            args.Damage,
            plate.Comp,
            out var remainder,
            out var absorbed,
            out var plateDamage);

        ApplyDamage(ent.Owner, plate, absorbed, plateDamage);

        if (remainder.Empty)
        {
            args.Cancelled = true;
            return;
        }

        args.Damage.DamageDict.Clear();

        foreach (var (type, amount) in remainder.DamageDict)
            args.Damage.DamageDict.Add(type, amount);
    }

    private void ApplyDamage(
        EntityUid wearer,
        Entity<ArmorPlateItemComponent> plate,
        FixedPoint2 absorbed,
        FixedPoint2 plateDamage)
    {
        if (plateDamage > FixedPoint2.Zero)
        {
            var damage = new DamageSpecifier();
            damage.DamageDict.Add("Blunt", plateDamage);

            _damageable.TryChangeDamage(
                plate.Owner,
                damage,
                ignoreResistances: true);
        }
    }
}