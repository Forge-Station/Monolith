using Content.Shared.Actions;
using Content.Shared.Clothing.Components;
using Content.Shared._Forge.Clothing.Components;
using Content.Shared.Clothing.EntitySystems;
using Content.Shared.DoAfter;
using Content.Shared.Inventory;
using Content.Shared.Item;
using Content.Shared.Verbs;
using Robust.Shared.Audio.Systems;

namespace Content.Shared._Forge.Clothing.EntitySystems;

public sealed class ClothingStyleCyclerSystem : EntitySystem
{
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private ClothingSystem _clothing = default!;
    [Dependency] private readonly SharedItemSystem _itemSys = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ClothingStyleCyclerComponent, GetItemActionsEvent>(OnGetActions);
        SubscribeLocalEvent<ClothingStyleCyclerComponent, CycleClothingStyleEvent>(OnCycleStyleAction);
        SubscribeLocalEvent<ClothingStyleCyclerComponent, GetVerbsEvent<AlternativeVerb>>(OnAlternateVerb);
        SubscribeLocalEvent<ClothingStyleCyclerComponent, ClothingStyleCycleDoAfterEvent>(OnCycleDoAfter);
    }

    private void OnGetActions(Entity<ClothingStyleCyclerComponent> ent, ref GetItemActionsEvent args)
    {
        if (!ent.Comp.CycleViaAction || !InValidSlot(ent))
            return;
        args.AddAction(ref ent.Comp.ActionEntity, ent.Comp.Action);
        Dirty(ent);
    }

    private void OnCycleStyleAction(Entity<ClothingStyleCyclerComponent> ent, ref CycleClothingStyleEvent args)
        => TryStartCycle(ent, args.Performer);

    private void OnAlternateVerb(Entity<ClothingStyleCyclerComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!ent.Comp.CycleViaAltVerb || !args.CanAccess || !args.CanInteract)
            return;

        var user = args.User;
        args.Verbs.Add
        (new AlternativeVerb
        {
            Text = Loc.GetString("clothing-style-cycle-verb"),
            Act = () => TryStartCycle(ent, user)
        }
        );
    }

    private void TryStartCycle(Entity<ClothingStyleCyclerComponent> ent, EntityUid user)
    {
        if (ent.Comp.States.Count == 0)
            return;

        var doAfterArgs = new DoAfterArgs(
            EntityManager, user, ent.Comp.CycleDelay,
            new ClothingStyleCycleDoAfterEvent(),
            ent.Owner, target: ent.Owner, used: ent.Owner)

        {
            BreakOnMove = true,
            BreakOnDamage = true,
            BlockDuplicate = true,
            NeedHand = false
        };

        _doAfter.TryStartDoAfter(doAfterArgs);
    }

    private void OnCycleDoAfter(Entity<ClothingStyleCyclerComponent> ent, ref ClothingStyleCycleDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;
        CycleStyle(ent, args.User);
    }

    private void CycleStyle(Entity<ClothingStyleCyclerComponent> ent, EntityUid user)
    {
        var comp = ent.Comp;
        if (comp.States.Count == 0)
            return;

        comp.CurrentIndex = (comp.CurrentIndex + 1) % comp.States.Count;
        ApplyStyle(ent);

        if (comp.CycleSound != null)
            _audio.PlayPredicted(comp.CycleSound, ent.Owner, user);

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

    private bool InValidSlot(Entity<ClothingStyleCyclerComponent> ent)
    {
        return ent.Comp.RequiredSlots == SlotFlags.NONE
            || _inventory.InSlotWithFlags(ent.Owner, ent.Comp.RequiredSlots);
    }
}
