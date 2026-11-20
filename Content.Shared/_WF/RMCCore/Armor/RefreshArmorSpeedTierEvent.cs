using Content.Shared.Inventory;

namespace Content.Shared._WF.RMCCore.Armor;

[ByRefEvent]
public record struct RefreshArmorSpeedTierEvent(
    SlotFlags TargetSlots,
    string? SpeedTier = ""
) : IInventoryRelayEvent;
