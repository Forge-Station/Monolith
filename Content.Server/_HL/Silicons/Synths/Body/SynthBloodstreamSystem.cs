using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Shared._HL.Silicons.Synths.Body;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._HL.Silicons.Synths.Body;

public sealed partial class SynthBloodstreamSystem : EntitySystem
{
    private static readonly TimeSpan UpdateRate = TimeSpan.FromSeconds(1);

    [Dependency] private BloodstreamSystem _bloodstream = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private HungerSystem _hunger = default!;
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private IGameTiming _timing = default!;

    private TimeSpan _nextUpdate;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextUpdate)
            return;

        _nextUpdate = _timing.CurTime + UpdateRate;

        var query = EntityQueryEnumerator<SynthBloodstreamComponent, HungerComponent, BloodstreamComponent>();
        while (query.MoveNext(out var uid, out var synthBloodstream, out var hunger, out var bloodstream))
        {
            if (!TryComp(uid, out DamageableComponent? damageable) ||
                !TryComp(uid, out MobStateComponent? mobState) ||
                mobState.CurrentState == MobState.Dead)
                continue;

            UpdateSynthBloodstream((uid, synthBloodstream, hunger, bloodstream, damageable));
        }
    }

    private void UpdateSynthBloodstream(Entity<SynthBloodstreamComponent, HungerComponent, BloodstreamComponent, DamageableComponent> ent)
    {
        var hunger = _hunger.GetHunger(ent.Comp2);
        if (hunger <= ent.Comp1.MinHunger)
            return;

        var availableHunger = MathF.Max(0f, hunger - ent.Comp1.MinHunger);
        var hungerCost = 0f;

        UpdatePassiveRepair(ent, ref hungerCost, availableHunger);
        UpdateBloodRegeneration(ent, ref hungerCost, availableHunger);

        if (hungerCost <= 0f)
            return;

        _hunger.ModifyHunger(ent.Owner, -MathF.Min(hungerCost, availableHunger), ent.Comp2);
    }

    private void UpdatePassiveRepair(
        Entity<SynthBloodstreamComponent, HungerComponent, BloodstreamComponent, DamageableComponent> ent,
        ref float hungerCost,
        float availableHunger)
    {
        var bloodEfficiency = GetBloodEfficiency(ent.Comp1, _bloodstream.GetBloodLevelPercentage(ent.Owner, ent.Comp3));
        if (bloodEfficiency <= 0f)
            return;

        var damage = BuildPassiveRepair(ent.Comp1, ent.Comp4, bloodEfficiency);
        LimitRepairByHunger(damage, ent.Comp1.HungerCostPerRepair, availableHunger);

        if (!damage.Empty)
        {
            var repaired = _damageable.TryChangeDamage(
                ent.Owner,
                damage,
                true,
                false,
                ent.Comp4);
            if (repaired != null)
            {
                var repairedTotal = MathF.Max(0f, -repaired.GetTotal().Float());
                hungerCost += repairedTotal * ent.Comp1.HungerCostPerRepair;
            }
        }

        var remainingHunger = MathF.Max(0f, availableHunger - hungerCost);
        if (ent.Comp1.BleedReductionAmount <= 0f || ent.Comp3.BleedAmount <= 0f || remainingHunger <= 0f)
            return;

        var sealedAmount = MathF.Min(ent.Comp3.BleedAmount, ent.Comp1.BleedReductionAmount * bloodEfficiency);
        if (ent.Comp1.HungerCostPerBleed > 0f)
            sealedAmount = MathF.Min(sealedAmount, remainingHunger / ent.Comp1.HungerCostPerBleed);

        if (_bloodstream.TryModifyBleedAmount(ent.Owner, -sealedAmount, ent.Comp3))
            hungerCost += sealedAmount * ent.Comp1.HungerCostPerBleed;
    }

    private void UpdateBloodRegeneration(
        Entity<SynthBloodstreamComponent, HungerComponent, BloodstreamComponent, DamageableComponent> ent,
        ref float hungerCost,
        float availableHunger)
    {
        var regeneration = ent.Comp1.BloodRegeneration;

        if (_bloodstream.GetBloodLevelPercentage(ent.Owner, ent.Comp3) >= regeneration.TargetBloodLevel)
            return;

        var refreshAmount = regeneration.BloodRefreshAmount;
        if (regeneration.HungerCostPerUnit > 0f)
        {
            var remainingHunger = MathF.Max(0f, availableHunger - hungerCost);
            var hungerLimitedAmount = FixedPoint2.New(remainingHunger / regeneration.HungerCostPerUnit);
            refreshAmount = FixedPoint2.Min(refreshAmount, hungerLimitedAmount);
        }

        if (refreshAmount <= FixedPoint2.Zero ||
            !_bloodstream.TryModifyBloodLevel(ent.Owner, refreshAmount, ent.Comp3))
            return;

        if (regeneration.HungerCostPerUnit > 0f)
            hungerCost += refreshAmount.Float() * regeneration.HungerCostPerUnit;
    }

    private DamageSpecifier BuildPassiveRepair(
        SynthBloodstreamComponent component,
        DamageableComponent damageable,
        float bloodEfficiency)
    {
        var repair = component.PassiveRepair;
        repair.DamageDict.Clear();

        foreach (var (type, amount) in component.Damage.Types)
        {
            var scaledAmount = amount * bloodEfficiency;
            if (scaledAmount == FixedPoint2.Zero)
                continue;

            repair.DamageDict[type] = scaledAmount;
        }

        foreach (var (groupId, amount) in component.Damage.Groups)
        {
            if (!_prototype.TryIndex<DamageGroupPrototype>(groupId, out var group))
                continue;

            var scaledAmount = amount * bloodEfficiency;
            if (scaledAmount != FixedPoint2.Zero)
                AddGroupDamage(repair, damageable, group, scaledAmount);
        }

        return repair;
    }

    private static void AddGroupDamage(
        DamageSpecifier repair,
        DamageableComponent damageable,
        DamageGroupPrototype group,
        FixedPoint2 damageAmount)
    {
        if (damageAmount < FixedPoint2.Zero)
        {
            AddSmartGroupRepair(repair, damageable, group, -damageAmount);
            return;
        }

        AddGroupDamageEvenly(repair, group, damageAmount);
    }

    private static void AddSmartGroupRepair(
        DamageSpecifier repair,
        DamageableComponent damageable,
        DamageGroupPrototype group,
        FixedPoint2 repairBudget)
    {
        var remainingTypes = 0;
        var remainingGroupDamage = FixedPoint2.Zero;

        foreach (var type in group.DamageTypes)
        {
            if (!damageable.Damage.DamageDict.TryGetValue(type, out var currentDamage) ||
                currentDamage <= FixedPoint2.Zero)
                continue;

            repair.DamageDict.TryGetValue(type, out var existingRepair);
            var damagedAmount = currentDamage + existingRepair;
            if (damagedAmount <= FixedPoint2.Zero)
                continue;

            remainingTypes++;
            remainingGroupDamage += damagedAmount;
        }

        var remainingRepair = FixedPoint2.Min(repairBudget, remainingGroupDamage);

        foreach (var type in group.DamageTypes)
        {
            if (remainingRepair <= FixedPoint2.Zero || remainingTypes <= 0)
                break;

            if (!damageable.Damage.DamageDict.TryGetValue(type, out var currentDamage) ||
                currentDamage <= FixedPoint2.Zero)
                continue;

            repair.DamageDict.TryGetValue(type, out var existingRepair);
            var remainingTypeDamage = currentDamage + existingRepair;
            if (remainingTypeDamage <= FixedPoint2.Zero)
                continue;

            var repairAmount = FixedPoint2.Min(remainingRepair / FixedPoint2.New(remainingTypes), remainingTypeDamage);
            repair.DamageDict[type] = existingRepair - repairAmount;
            remainingRepair -= repairAmount;
            remainingTypes--;
        }
    }

    private static void AddGroupDamageEvenly(
        DamageSpecifier repair,
        DamageGroupPrototype group,
        FixedPoint2 damageAmount)
    {
        var remainingTypes = group.DamageTypes.Count;
        var remainingDamage = damageAmount;

        foreach (var type in group.DamageTypes)
        {
            var damage = remainingDamage / FixedPoint2.New(remainingTypes);
            if (repair.DamageDict.TryGetValue(type, out var existing))
                repair.DamageDict[type] = existing + damage;
            else
                repair.DamageDict[type] = damage;

            remainingDamage -= damage;
            remainingTypes -= 1;
        }
    }

    private static void LimitRepairByHunger(DamageSpecifier repair, float hungerCostPerRepair, float availableHunger)
    {
        if (hungerCostPerRepair <= 0f)
            return;

        var availableRepair = FixedPoint2.New(availableHunger / hungerCostPerRepair);
        if (availableRepair <= FixedPoint2.Zero)
        {
            repair.DamageDict.Clear();
            return;
        }

        var totalRepair = FixedPoint2.Zero;
        foreach (var amount in repair.DamageDict.Values)
        {
            if (amount < FixedPoint2.Zero)
                totalRepair -= amount;
        }

        if (totalRepair <= availableRepair)
            return;

        var scale = availableRepair / totalRepair;
        foreach (var type in repair.DamageDict.Keys)
        {
            if (repair.DamageDict[type] < FixedPoint2.Zero)
                repair.DamageDict[type] *= scale;
        }
    }

    private static float GetBloodEfficiency(SynthBloodstreamComponent component, float bloodLevel)
    {
        if (bloodLevel < component.MinBloodLevel)
            return 0f;

        if (bloodLevel >= component.FullEfficiencyBloodLevel)
            return 1f;

        if (component.FullEfficiencyBloodLevel <= component.MinBloodLevel)
            return 1f;

        var progress = (bloodLevel - component.MinBloodLevel) /
            (component.FullEfficiencyBloodLevel - component.MinBloodLevel);

        return MathHelper.Lerp(component.MinBloodEfficiency, 1f, Math.Clamp(progress, 0f, 1f));
    }
}
