using Content.Shared.Actions;
using Content.Shared.Clothing.Components;
using Content.Shared._Forge.Clothing.Components;
using Content.Shared.Clothing.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;
using Content.Shared.Popups;

namespace Content.Shared._Forge.Clothing.Components;

public sealed class ScarfStyleCyclerSystem : EntitySystem
{
    [Dependency] private InventorySystem _inventorySystem = default!;
    [Dependency] private ClothingSystem _clothing = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<ScarfStyleCyclerComponent, GetItemActionsEvent>(OnGetActions);
        SubscribeLocalEvent<ScarfStyleCyclerComponent, CycleScarfEvent>(OnCycleStyle);
        SubscribeLocalEvent<ScarfStyleCyclerComponent, GotUnequippedEvent>(OnUnequipped);
    }

    private void OnGetActions(Entity<ScarfStyleCyclerComponent> ent, ref GetItemActionsEvent args)
    {
        if (_inventory.InSlotWithFlags(ent, SlotFlags.NECK))
            args.AddAction(ref ent.Comp.ActionEntity, ent.Comp.Action);
    }

    private void OnSycleStyle(Entity<ScarfStyleCyclerComponent> ent, ref CycleScarfEvent args)
    {
        var comp = ent.Comp;
        if (comp.ActionEntity == null || comp.Prefixes.Count == 0)
            return;

        comp.CurrentIndex = (comp.CurrentIndex + 1) % comp.Prefixes.Count;
        var prefix = comp.Prefixes[comp.CurrentIndex];
        _clothing.SetEquippedPrefix(ent, string.IsNullOrEmpty(prefix) ? null : prefix);
        Dirty(ent);
    }

    private void OnUnequipped(Entity<ScarfStyleCyclerComponent> ent, ref GotUnequippedEvent args)
    {
        ent.Comp.CurrentIndex = 0;
        _clothing.SetEquippedPrefix(ent, null);
        Dirty(ent);
    }
}
