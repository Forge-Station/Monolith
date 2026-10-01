using System.Linq;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Wolfmed.Compat;

/// <summary>Onyx-shaped DamageableSystem API over Wolfgate's older one. Vendored _Onyx files depend on this, never on DamageableSystem directly.</summary>
public sealed class WolfmedDamageableSystem : EntitySystem
{
    [Dependency] private DamageableSystem _damage = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;

    private EntityQuery<DamageableComponent> _damageableQuery;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        _damageableQuery = GetEntityQuery<DamageableComponent>();
    }

    /// <summary>Pass-through of Wolfgate's global topical-healing multiplier.</summary>
    public float UniversalTopicalsHealModifier => _damage.UniversalTopicalsHealModifier;

    /// <summary>Applies damage through Wolfgate's pipeline and returns the applied delta (never null).</summary>
    public DamageSpecifier ChangeDamage(
        Entity<DamageableComponent?> ent,
        DamageSpecifier damage,
        bool ignoreResistances = false,
        bool interruptsDoAfters = true,
        EntityUid? origin = null,
        bool ignoreGlobalModifiers = false,
        float armorPenetration = 0f,
        EntityUid? tool = null,
        DamageableSystem.DamageOriginFlag? originFlag = null)
    {
        if (!_damageableQuery.Resolve(ent.Owner, ref ent.Comp, false))
            return new DamageSpecifier();

        // canSever/canEvade are forced off: Wolfmed owns severing for wound hosts (GUARD B).
        return _damage.TryChangeDamage(
                   ent.Owner,
                   damage,
                   ignoreResistances,
                   interruptsDoAfters,
                   ent.Comp,
                   origin,
                   ignoreGlobalModifiers,
                   armorPenetration,
                   canSever: false,
                   canEvade: false,
                   partMultiplier: 1f,
                   targetPart: null,
                   tool: tool,
                   originFlag: originFlag)
               ?? new DamageSpecifier();
    }

    /// <summary>Bool-returning form with the applied delta. The only TryChangeDamage on this type.</summary>
    public bool TryChangeDamage(
        Entity<DamageableComponent?> ent,
        DamageSpecifier damage,
        out DamageSpecifier newDamage,
        bool ignoreResistances = false,
        bool interruptsDoAfters = true,
        EntityUid? origin = null,
        bool ignoreGlobalModifiers = false,
        float armorPenetration = 0f,
        EntityUid? tool = null,
        DamageableSystem.DamageOriginFlag? originFlag = null)
    {
        newDamage = ChangeDamage(
            ent,
            damage,
            ignoreResistances,
            interruptsDoAfters,
            origin,
            ignoreGlobalModifiers,
            armorPenetration,
            tool,
            originFlag);

        return !newDamage.Empty;
    }

    /// <summary>Copy of the entity's damage, positive entries only.</summary>
    public DamageSpecifier GetPositiveDamage(Entity<DamageableComponent> ent)
    {
        var damage = new DamageSpecifier();
        damage.DamageDict.EnsureCapacity(ent.Comp.Damage.DamageDict.Count);

        foreach (var (damageId, value) in ent.Comp.Damage.DamageDict)
        {
            if (value > FixedPoint2.Zero)
                damage.DamageDict.Add(damageId, value);
        }

        return damage;
    }

    /// <summary>Copy of the entity's positive damage restricted to one damage group.</summary>
    public DamageSpecifier GetPositiveDamage(Entity<DamageableComponent> ent, ProtoId<DamageGroupPrototype> group)
    {
        if (!_prototypes.TryIndex(group, out var groupProto))
            return new DamageSpecifier();

        var damage = new DamageSpecifier();
        damage.DamageDict.EnsureCapacity(groupProto.DamageTypes.Count);

        foreach (var damageId in groupProto.DamageTypes)
        {
            if (!ent.Comp.Damage.DamageDict.TryGetValue(damageId, out var value))
                continue;

            if (value > FixedPoint2.Zero)
                damage.DamageDict.Add(damageId, value);
        }

        return damage;
    }

    /// <summary>Copy of the entity's whole damage specifier.</summary>
    public DamageSpecifier GetAllDamage(Entity<DamageableComponent?> ent)
    {
        if (!_damageableQuery.Resolve(ent.Owner, ref ent.Comp, false))
            return new DamageSpecifier();

        return ent.Comp.Damage.Clone();
    }

    /// <summary>Total damage currently on the entity.</summary>
    public FixedPoint2 GetTotalDamage(Entity<DamageableComponent?> ent)
    {
        if (!_damageableQuery.Resolve(ent.Owner, ref ent.Comp, false))
            return FixedPoint2.Zero;

        return ent.Comp.TotalDamage;
    }

    /// <summary>Replaces the entity's damage wholesale and reports a real delta to DamageChangedEvent.</summary>
    public void SetDamage(Entity<DamageableComponent?> ent, DamageSpecifier damage)
    {
        if (!_damageableQuery.Resolve(ent.Owner, ref ent.Comp, false))
            return;

        // GUARD F: the delta has to be real, or every DamageChangedEvent listener that early-returns
        // on a null DamageDelta goes silent for wound hosts (bleeding, wake-on-damage, kill attribution...).
        var delta = damage - ent.Comp.Damage;
        delta.TrimZeros();

        // The dict is taken as a local because DamageableComponent.Damage is [Access]-restricted to
        // DamageableSystem for writes; this mirrors how DamageableSystem.TryChangeDamage writes it.
        var dict = ent.Comp.Damage.DamageDict;

        // WOLFGATE (D30): Onyx removes types missing from `damage`; Wolfgate zeroes them so
        // DamageableInit's container seeding survives and TryChangeDamage:256-257 keeps working.
        foreach (var type in dict.Keys.ToArray())
        {
            if (!damage.DamageDict.ContainsKey(type))
                dict[type] = FixedPoint2.Zero;
        }

        foreach (var (type, amount) in damage.DamageDict)
        {
            dict[type] = amount;
        }

        _damage.DamageChanged(ent.Owner, ent.Comp, delta.Empty ? null : delta, interruptsDoAfters: false);
    }

    /// <summary>Zeroes every damage type on the entity.</summary>
    public void ClearAllDamage(Entity<DamageableComponent?> ent)
    {
        SetAllDamage(ent, FixedPoint2.Zero);
    }

    /// <summary>Sets every damage type the entity supports to one value; negative values are ignored.</summary>
    public void SetAllDamage(Entity<DamageableComponent?> ent, FixedPoint2 newValue)
    {
        if (!_damageableQuery.Resolve(ent.Owner, ref ent.Comp, false))
            return;

        if (newValue < FixedPoint2.Zero)
            return;

        var dict = ent.Comp.Damage.DamageDict;

        foreach (var type in dict.Keys.ToArray())
        {
            dict[type] = newValue;
        }

        // Setting damage is not 'dealing' damage, so the delta is empty rather than real.
        _damage.DamageChanged(ent.Owner, ent.Comp, new DamageSpecifier(), interruptsDoAfters: false);
    }

    /// <summary>Whether the entity's damage container supports this damage type.</summary>
    public bool CanBeDamagedBy(Entity<DamageableComponent?> ent, ProtoId<DamageTypePrototype> type)
    {
        if (!_damageableQuery.Resolve(ent.Owner, ref ent.Comp, false))
            return false;

        // WOLFGATE (D19): Onyx reads InjurableComponent.DamageContainer; that component is not ported,
        // so this mirrors DamageableInit's seeding loop over DamageableComponent.DamageContainerID.
        if (ent.Comp.DamageContainerID is not { } containerId)
            return true;

        if (!_prototypes.TryIndex<DamageContainerPrototype>(containerId, out var container))
            return true;

        if (container.SupportedTypes.Contains(type.Id))
            return true;

        foreach (var groupId in container.SupportedGroups)
        {
            if (_prototypes.TryIndex<DamageGroupPrototype>(groupId, out var group)
                && group.DamageTypes.Contains(type.Id))
                return true;
        }

        return false;
    }

    /// <summary>Positive damage above the given amount, optionally restricted to one group.</summary>
    public bool TryGetDamageGreaterThan(
        Entity<DamageableComponent> ent,
        FixedPoint2 amount,
        out DamageSpecifier damage,
        ProtoId<DamageGroupPrototype>? group = null)
    {
        damage = group == null ? GetPositiveDamage(ent) : GetPositiveDamage(ent, group.Value);
        return damage.GetTotal() > amount;
    }

    /// <summary>Heals an equal share of the amount from every non-zero damage type.</summary>
    public DamageSpecifier HealEvenly(
        Entity<DamageableComponent?> ent,
        FixedPoint2 amount,
        ProtoId<DamageGroupPrototype>? group = null,
        EntityUid? origin = null)
    {
        var damageChange = new DamageSpecifier();

        if (!_damageableQuery.Resolve(ent.Owner, ref ent.Comp, false))
            return damageChange;

        if (amount >= 0)
            return damageChange;

        // Heal everything if the requested amount covers the whole pool.
        if (!TryGetDamageGreaterThan((ent.Owner, ent.Comp), -amount, out var damage, group))
            return ChangeDamage(ent, -damage, true, false, origin);

        damageChange.DamageDict.EnsureCapacity(damage.DamageDict.Count);
        foreach (var type in damage.DamageDict.Keys)
        {
            damageChange.DamageDict.Add(type, FixedPoint2.Zero);
        }

        var remaining = -amount;
        var keys = damage.DamageDict.Keys.ToList();

        while (remaining > 0)
        {
            var count = keys.Count;

            // Round up when dividing so the loop always terminates; over-healing is capped below.
            var maxHeal = count == 1 ? remaining : (remaining + FixedPoint2.Epsilon * (count - 1)) / count;

            // Backwards, because entries get removed once they are fully healed.
            for (var i = count - 1; i >= 0; i--)
            {
                var type = keys[i];
                var heal = damage.DamageDict[type] + damageChange.DamageDict[type];

                if (heal > maxHeal)
                    heal = maxHeal;
                else
                    keys.RemoveAt(i);

                if (heal >= remaining)
                {
                    damageChange.DamageDict[type] -= remaining;
                    remaining = FixedPoint2.Zero;
                    break;
                }

                remaining -= heal;
                damageChange.DamageDict[type] -= heal;
            }
        }

        return ChangeDamage(ent, damageChange, true, false, origin);
    }

    /// <summary>Heals the amount distributed proportionally to existing damage.</summary>
    public DamageSpecifier HealDistributed(
        Entity<DamageableComponent?> ent,
        FixedPoint2 amount,
        ProtoId<DamageGroupPrototype>? group = null,
        EntityUid? origin = null)
    {
        var damageChange = new DamageSpecifier();

        if (!_damageableQuery.Resolve(ent.Owner, ref ent.Comp, false))
            return damageChange;

        if (amount >= 0)
            return damageChange;

        if (!TryGetDamageGreaterThan((ent.Owner, ent.Comp), -amount, out var damage, group))
            return ChangeDamage(ent, -damage, true, false, origin);

        damageChange.DamageDict.EnsureCapacity(damage.DamageDict.Count);
        var total = damage.GetTotal();

        foreach (var (type, value) in damage.DamageDict)
        {
            damageChange.DamageDict.Add(type, value / total * amount);
        }

        return ChangeDamage(ent, damageChange, true, false, origin);
    }
}
