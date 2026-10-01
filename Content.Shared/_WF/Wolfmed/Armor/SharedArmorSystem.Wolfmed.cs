// WOLFGATE: HOOK 10 body. SharedArmorSystem.cs calls TryApplyWoundHostArmor for a wound host instead of the
// flat ApplyModifierSet path; this file is that branch plus Onyx's ApplyWoundSystemicArmor helper, applying
// the wearer's armour to a wound host's systemic (whole-body) damage only. Localized types are armoured
// separately by WolfmedPartArmorSystem once routing resolves the struck part, so neither applies twice.

using Content.Shared._Onyx.Wounds;
using Content.Shared.Damage;
using Content.Shared.Inventory;

namespace Content.Shared.Armor;

public abstract partial class SharedArmorSystem
{
    /// <summary>
    /// WOLFGATE (HOOK 10): a wound host armours only its systemic damage here. Wolfgate's InventoryRelayedEvent
    /// carries no Owner, so the wearer is the equipped item's transform parent.
    /// </summary>
    private bool TryApplyWoundHostArmor(EntityUid uid, ArmorComponent component, InventoryRelayedEvent<DamageModifyEvent> args)
    {
        if (!TryComp(Transform(uid).ParentUid, out WoundHostComponent? host))
            return false;

        args.Args.Damage = ApplyWoundSystemicArmor(args.Args.Damage,
            DamageSpecifier.PenetrateArmor(component.Modifiers, args.Args.ArmorPenetration),
            host);
        return true;
    }

    /// <summary>
    /// WOLFGATE (HOOK 10): ported from Onyx's SharedArmorSystem.ApplyWoundSystemicArmor. Applies the already
    /// penetration-adjusted modifiers to a wound host's systemic damage only, leaving localized types untouched.
    /// </summary>
    private static DamageSpecifier ApplyWoundSystemicArmor(
        DamageSpecifier damage,
        DamageModifierSet modifiers,
        WoundHostComponent host)
    {
        var systemic = new DamageSpecifier(damage);
        var hasSystemic = false;
        foreach (var (type, value) in damage.DamageDict)
        {
            if (host.LocalizedDamageTypes.Contains(type))
                systemic.DamageDict.Remove(type);
            else if (value != 0)
                hasSystemic = true;
        }

        if (!hasSystemic)
            return damage;

        var reduced = DamageSpecifier.ApplyModifierSet(systemic, modifiers);

        var result = damage.Clone();
        foreach (var (type, _) in systemic.DamageDict)
        {
            if (reduced.DamageDict.TryGetValue(type, out var value))
                result.DamageDict[type] = value;
            else
                result.DamageDict.Remove(type);
        }

        return result;
    }
}
