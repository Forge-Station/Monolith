using Content.Shared.Actions;
using Content.Shared.Clothing.Components;
using Content.Shared._Forge.Clothing.Components;
using Content.Shared.Clothing.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Item;
using Content.Shared.Verbs;

namespace Content.Shared._Forge.Clothing.EntitySystems;

public sealed class ClothingStyleCyclerSystem : EntitySystem
{
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private ClothingSystem _clothing = default!;
    [Dependency] private readonly SharedItemSystem _itemSys = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<ClothingStyleCyclerComponent, GetItemActionsEvent>(OnGetActions);
        SubscribeLocalEvent<ClothingStyleCyclerComponent, CycleScarfEvent>(OnCycleStyleAction);
        SubscribeLocalEvent<ClothingStyleCyclerComponent, GetVerbsEvent<AlternativeVerb>>(OnAlternateVerb);
        // SubscribeLocalEvent<ScarfStyleCyclerComponent, GotUnequippedEvent>(OnUnequipped); Remove by save style yo
    }

    private void OnGetActions(Entity<ClothingStyleCyclerComponent> ent, ref GetItemActionsEvent args)
    {
        if (!ent.Comp.CycleViaAction || !InValidSlot(ent))
            return;
        args.AddAction(ref ent.Comp.ActionEntity, ent.Comp.Action);
    }

    private void OnCycleStyleAction(Entity<ClothingStyleCyclerComponent> ent, ref CycleClothingStyleEvent args)
        => CycleStyle(ent);

    private void OnAlternateVerb(Entity<ClothingStyleCyclerComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!ent.Comp.CycleViaAltVerb || !args.CanAccess || !args.CanInteract)
            return;

        args.Verbs.Add
        (new AlternativeVerb
        {
            Text = Loc.GetString("clothing-style-cycle-verb"),
            Act = () => CycleStyle(ent)
        }
        );
    }

    private void CycleStyle(Entity<ClothingStyleCyclerComponent> ent)
    {
        var comp = ent.Comp;
        if (comp.States.Count == 0)
            return;

        comp.CurrentIndex = (comp.CurrentIndex + 1) % comp.States.Count;
        ApplyStyle(ent);
        Dirty(ent);
    }

    private void ApplyStyle(Entity<ClothingStyleCyclerComponent> ent)
    {
        if (!TryComp(ent.Owner, out ClothingComponent? clothing))
            return;
        var entry = ent.Comp.States[ent.Comp.CurrentIndex];
        _clothing.SetLayerState(clothing, ent.Comp.Slot, ent.Comp.MapKey, entry.State);
        _clothing.SetLayerColor(clothing, ent.Comp.Slot, ent.Comp.MapKey, entry.Color);
        _itemSys.VisualsChanged(ent.Owner);
        Dirty(ent.Owner, clothing);
    }

    private bool inValidSlot(Entity<ClothingStyleCyclerComponent> ent)
    {
        if (ent.Comp.RequiredSlots != SlotFlags.NONE)
            return _inventory.InSlotWithFlags(ent.Owner, ent.Comp.RequiredSlots);

        return TryComp(ent.Owner, out ClothingComponent? clothing)
            && clothing.InSlotFlag is {} flag
            && clothing.Slots.HasFlag(flag);
    }
}
