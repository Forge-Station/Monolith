using Content.Shared.Containers.ItemSlots;

namespace Content.Shared._Forge.Features.Components;

public abstract class SharedApcRequiresSlotSystem : EntitySystem
{
    [Dependency] private readonly ItemSlotsSystem _slots = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ApcRequiresSlotComponent, ComponentRemove>(OnRemove);
    }

    private void OnRemove(EntityUid uid, ApcRequiresSlotComponent component, ComponentRemove args)
    {
        if (!_slots.TryGetSlot(uid, ApcRequiresSlotComponent.BatterySlotId, out var slot))
            return;

        _slots.RemoveItemSlot(uid, slot);
    }
}
