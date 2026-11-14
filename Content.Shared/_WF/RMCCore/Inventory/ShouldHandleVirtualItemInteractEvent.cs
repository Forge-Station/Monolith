using Content.Shared.Interaction;

namespace Content.Shared._WF.RMCCore.Inventory;

[ByRefEvent]
public record struct ShouldHandleVirtualItemInteractEvent(BeforeRangedInteractEvent Event, bool Handle = false);
