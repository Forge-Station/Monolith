using System.Numerics;
using Content.Shared._WF.NpcIntents;
using Content.Shared._WF.Wolfmed.Compat;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Cargo.Components;
using Content.Shared.Hands.Components;
using Content.Shared.Inventory;
using Content.Shared.Item;
using Content.Shared.Stacks;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Containers;
using Robust.Shared.Random;

namespace Content.Server._WF.NpcIntents;

public sealed partial class WFNpcIntentSystem
{
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private WolfmedBodySystem _wolfBody = default!;

    private readonly List<(EntityUid Item, bool Worn)> _carried = new();

    private void TickTaking(Entity<WFNpcIntentComponent> ent, TimeSpan now)
    {
        var comp = ent.Comp;
        var victim = comp.Target!.Value;

        if (_mobState.IsDead(victim))
        {
            Leave(ent);
            return;
        }

        var here = _transform.GetMapCoordinates(ent);
        var there = _transform.GetMapCoordinates(victim);
        var near = there.MapId == here.MapId ? Vector2.Distance(here.Position, there.Position) : float.MaxValue;

        if (near <= comp.TakeRange)
        {
            Take(ent, victim);
            return;
        }

        // Out of time on the way: a victim a few steps off still gets robbed, one out of reach is left.
        if (now >= comp.PhaseEnd)
        {
            if (near <= comp.TakeRange * 3f)
                Take(ent, victim);
            else
                Leave(ent);
        }
    }

    /// <summary>Takes what the endpoint is after off a downed victim, says so, and leaves.</summary>
    public void Take(Entity<WFNpcIntentComponent> ent, EntityUid victim)
    {
        var set = _proto.Index(ent.Comp.Set);
        var took = ent.Comp.Active == WFNpcIntentKind.Harvest ? Harvest(ent, victim, set) : Mug(ent, victim, set);
        if (took)
            Say(ent, ent.Comp.Active == WFNpcIntentKind.Harvest ? set.HarvestLines : set.MugLines);

        Leave(ent);
    }

    /// <summary>Takes the victim's cash, and a valuable: their weapon some of the time.</summary>
    private bool Mug(Entity<WFNpcIntentComponent> ent, EntityUid victim, WFNpcIntentSetPrototype set)
    {
        _carried.Clear();
        Carried(victim);

        var took = false;
        foreach (var (item, worn) in _carried.ToArray())
        {
            if (worn || !HasComp<CashComponent>(item) || !TryComp(item, out StackComponent? stack) || stack.StackTypeId != "Credit")
                continue;

            ent.Comp.TookCash += stack.Count;
            _container.TryRemoveFromContainer(item);
            Stow(ent, item);
            ent.Comp.Took.Add(item);
            took = true;
        }

        _carried.Clear();
        Carried(victim);

        EntityUid? pick = null;
        if (_random.Prob(set.TakeWeaponChance))
            pick = Best(weapons: true);

        pick ??= Best(weapons: false);
        if (pick is not { } loot)
            return took;

        _container.TryRemoveFromContainer(loot);
        Stow(ent, loot);
        ent.Comp.Took.Add(loot);
        return true;
    }

    /// <summary>The most valuable thing carried and not worn, or the first weapon.</summary>
    private EntityUid? Best(bool weapons)
    {
        EntityUid? best = null;
        var bestPrice = 0.0;
        foreach (var (item, worn) in _carried)
        {
            if (worn || !HasComp<ItemComponent>(item) || HasComp<CashComponent>(item))
                continue;

            if (weapons && !HasComp<GunComponent>(item) && !HasComp<MeleeWeaponComponent>(item))
                continue;

            var price = _pricing.GetPrice(item, includeContents: false);
            if (weapons)
                return item;

            if (price <= bestPrice)
                continue;

            best = item;
            bestPrice = price;
        }

        return best;
    }

    /// <summary>Takes an organ, or sometimes a limb, from the victim's body. They live, and need a medic.</summary>
    private bool Harvest(Entity<WFNpcIntentComponent> ent, EntityUid victim, WFNpcIntentSetPrototype set)
    {
        if (_random.Prob(set.HarvestLimbChance) && TakeLimb(ent, victim, set))
            return true;

        var organs = new List<EntityUid>();
        foreach (var (id, organ) in _body.GetBodyOrgans(victim))
        {
            if (set.HarvestOrgans.Contains(organ.SlotId))
                organs.Add(id);
        }

        if (organs.Count > 0)
        {
            var organ = _random.Pick(organs);
            if (_body.RemoveOrgan(organ))
            {
                Stow(ent, organ);
                ent.Comp.Took.Add(organ);
                return true;
            }
        }

        return TakeLimb(ent, victim, set);
    }

    private bool TakeLimb(Entity<WFNpcIntentComponent> ent, EntityUid victim, WFNpcIntentSetPrototype set)
    {
        var limbs = new List<EntityUid>();
        foreach (var type in set.HarvestLimbs)
        {
            foreach (var (id, _) in _body.GetBodyChildrenOfType(victim, type))
            {
                limbs.Add(id);
            }
        }

        while (limbs.Count > 0)
        {
            var limb = _random.PickAndTake(limbs);
            if (!_wolfBody.TryDetachPart(limb))
                continue;

            Stow(ent, limb);
            ent.Comp.Took.Add(limb);
            return true;
        }

        return false;
    }

    /// <summary>Tiles round a downed body that count as what it dropped.</summary>
    private const float DropReach = 1.6f;

    private readonly HashSet<Entity<ItemComponent>> _loose = new();

    /// <summary>Everything a body holds, in its hands, pockets, worn clothes and the bags in them, and what it dropped going down.</summary>
    private void Carried(EntityUid body)
    {
        foreach (var item in _inventory.GetHandOrInventoryEntities(body))
        {
            var worn = _inventory.TryGetContainingSlot(item, out var slot) && (slot.SlotFlags & SlotFlags.POCKET) == 0;
            _carried.Add((item, worn));
            Inside(item, 0);
        }

        // A body going down lets go of what is in its hands.
        _loose.Clear();
        _lookup.GetEntitiesInRange(Transform(body).Coordinates, DropReach, _loose);
        foreach (var loose in _loose)
        {
            if (!_container.IsEntityInContainer(loose))
                _carried.Add((loose, false));
        }
    }

    private void Inside(EntityUid item, int depth)
    {
        if (depth > 3 || !TryComp(item, out ContainerManagerComponent? manager))
            return;

        foreach (var container in manager.Containers.Values)
        {
            foreach (var inner in container.ContainedEntities)
            {
                _carried.Add((inner, false));
                Inside(inner, depth + 1);
            }
        }
    }

    /// <summary>Pockets it if it can, else holds it, else it lands at the thief's feet.</summary>
    private void Stow(EntityUid thief, EntityUid item)
    {
        if (TryComp(thief, out InventoryComponent? inventory)
            && (_inventory.TryEquip(thief, item, "pocket1", silent: true, inventory: inventory)
                || _inventory.TryEquip(thief, item, "pocket2", silent: true, inventory: inventory)))
            return;

        _hands.PickupOrDrop(thief, item, checkActionBlocker: false, animate: false);
    }
}
