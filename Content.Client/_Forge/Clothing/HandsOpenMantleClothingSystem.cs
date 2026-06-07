using Content.Client.Items.Systems;
using Content.Shared._Forge.Clothing;
using Content.Shared.Clothing;
using Content.Shared.Clothing.Components;
using Content.Shared.Clothing.EntitySystems;
using Content.Shared.Hands;
using Content.Shared.Hands.Components;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;

namespace Content.Client._Forge.Clothing;

/// <summary>
/// Swaps equipped mantle RSI states when the wearer's hand contents change.
/// </summary>
public sealed class HandsOpenMantleClothingSystem : EntitySystem
{
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly ItemSystem _item = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<HandsOpenMantleClothingComponent, GetEquipmentVisualsEvent>(OnGetVisuals,
            after: [typeof(ClothingSystem)]);
        SubscribeLocalEvent<HandsOpenMantleClothingComponent, GotEquippedEvent>(OnMantleEquipped);
        SubscribeLocalEvent<HandsComponent, DidEquipHandEvent>(OnHandsChanged);
        SubscribeLocalEvent<HandsComponent, DidUnequipHandEvent>(OnHandsChanged);
    }

    private void OnGetVisuals(Entity<HandsOpenMantleClothingComponent> ent, ref GetEquipmentVisualsEvent args)
    {
        if (!TryComp(ent, out ClothingComponent? clothing) || clothing.MappedLayer == null)
            return;

        if (!TryComp(args.Equipee, out HandsComponent? hands))
            return;

        var state = GetEquippedState(hands, ent.Comp);

        foreach (var layer in args.Layers)
        {
            if (layer.Item1 != clothing.MappedLayer)
                continue;

            layer.Item2.State = state;
        }
    }

    private void OnMantleEquipped(Entity<HandsOpenMantleClothingComponent> ent, ref GotEquippedEvent args)
    {
        _item.VisualsChanged(ent);
    }

    private void OnHandsChanged<T>(EntityUid uid, HandsComponent hands, T args)
        where T : notnull
    {
        RefreshWearerMantle(uid);
    }

    private void RefreshWearerMantle(EntityUid wearer)
    {
        if (!_inventory.TryGetSlotEntity(wearer, "neck", out var mantle) || mantle == null)
            return;

        if (!HasComp<HandsOpenMantleClothingComponent>(mantle))
            return;

        _item.VisualsChanged(mantle.Value);
    }

    private static string GetEquippedState(HandsComponent hands, HandsOpenMantleClothingComponent comp)
    {
        var leftOccupied = false;
        var rightOccupied = false;

        foreach (var hand in hands.Hands.Values)
        {
            if (hand.HeldEntity == null)
                continue;

            switch (hand.Location)
            {
                case HandLocation.Left:
                    leftOccupied = true;
                    break;
                case HandLocation.Right:
                case HandLocation.Middle:
                    rightOccupied = true;
                    break;
            }
        }

        if (leftOccupied && rightOccupied)
            return comp.BothHandsState;

        if (rightOccupied)
            return comp.RightHandState;

        if (leftOccupied)
            return comp.LeftHandState;

        return comp.ClosedState;
    }
}
