using Content.Shared.Containers.ItemSlots;

namespace Content.Shared._Forge.Features.Components;

public abstract class SharedApcRequiresSlotSystem : EntitySystem
{
    [Dependency] private readonly ItemSlotsSystem _slots = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ApcRequiresSlotComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<ApcRequiresSlotComponent, ComponentRemove>(OnRemove);
    }

    private void OnInit(EntityUid uid, ApcRequiresSlotComponent component, ComponentInit args)
    {
        _slots.AddItemSlot(uid, ApcRequiresSlotComponent.BatterySlotId, component.BatterySlot);
    }

    private void OnRemove(EntityUid uid, ApcRequiresSlotComponent component, ComponentRemove args)
    {
        _slots.RemoveItemSlot(uid, component.BatterySlot);
    }
}
