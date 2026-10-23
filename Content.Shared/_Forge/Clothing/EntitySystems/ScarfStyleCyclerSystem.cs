using Content.Shared.Actions;
using Content.Shared.Clothing.Components;
using Content.Shared._Forge.Clothing.Components;
using Content.Shared.Clothing.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;
using Content.Shared.Item;
using Content.Shared.Verbs;

namespace Content.Shared._Forge.Clothing.EntitySystems;

public sealed class ScarfStyleCyclerSystem : EntitySystem
{
    public const string MapKey = "scarf-style";
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private ClothingSystem _clothing = default!;
    [Dependency] private readonly SharedItemSystem _itemSys = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<ScarfStyleCyclerComponent, GetItemActionsEvent>(OnGetActions);
        SubscribeLocalEvent<ScarfStyleCyclerComponent, CycleScarfEvent>(OnCycleStyle);
        SubscribeLocalEvent<ScarfStyleCyclerComponent, GetVerbsEvent<AlternativeVerb>>(OnAlternateVerb);
        // SubscribeLocalEvent<ScarfStyleCyclerComponent, GotUnequippedEvent>(OnUnequipped); Remove by save style yo
    }

    private void OnGetActions(Entity<ScarfStyleCyclerComponent> ent, ref GetItemActionsEvent args)
    {
        if (_inventory.InSlotWithFlags(ent.Owner, SlotFlags.NECK))
            args.AddAction(ref ent.Comp.ActionEntity, ent.Comp.Action);
    }

    private void OnCycleStyle(Entity<ScarfStyleCyclerComponent> ent, ref CycleScarfEvent args)
    {
        CycleStyle(ent);
    }

    private void OnAlternateVerb(Entity<ScarfStyleCyclerComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        args.Verbs.Add(new AlternativeVerb
        {
            Text = "Change style", // или Loc.GetString(...)
            Act = () => CycleStyle(ent)
        });
    }

    private void CycleStyle(Entity<ScarfStyleCyclerComponent> ent)
    {
        var comp = ent.Comp;
        if (comp.States.Count == 0)
            return;

        comp.CurrentIndex = (comp.CurrentIndex + 1) % comp.States.Count;
        ApplyStyle(ent);
        Dirty(ent);
    }

    private void OnUnequipped(Entity<ScarfStyleCyclerComponent> ent, ref GotUnequippedEvent args)
    {
        ent.Comp.CurrentIndex = 0;
        ApplyStyle(ent);
        Dirty(ent);
    }

    private void ApplyStyle(Entity<ScarfStyleCyclerComponent> ent)
    {
        if (!TryComp(ent.Owner, out ClothingComponent? clothing))
            return;

        var entry = ent.Comp.States[ent.Comp.CurrentIndex];
        _clothing.SetLayerState(clothing, "neck", MapKey, entry.State);
        _clothing.SetLayerColor(clothing, "neck", MapKey, entry.Color);
        _itemSys.VisualsChanged(ent.Owner);
        Dirty(ent.Owner, clothing);
    }
}
