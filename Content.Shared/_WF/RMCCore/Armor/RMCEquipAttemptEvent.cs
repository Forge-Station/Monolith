using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;

namespace Content.Shared._WF.RMCCore.Armor;

[ByRefEvent]
public readonly struct RMCEquipAttemptEvent(
    BeingEquippedAttemptEvent @event,
    SlotFlags targetSlots
) : IInventoryRelayEvent
{
    public readonly BeingEquippedAttemptEvent Event = @event;
    public SlotFlags TargetSlots { get; } = targetSlots;
}
