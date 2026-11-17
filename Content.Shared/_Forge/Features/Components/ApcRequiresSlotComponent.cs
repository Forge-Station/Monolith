using Content.Shared.Containers.ItemSlots;
using Robust.Shared.GameStates;

namespace Content.Shared._Forge.Features.Components;

/// <summary>
/// APC that will not supply power until the configured item is inserted into its slot.
/// </summary>
[RegisterComponent, NetworkedComponent]
[Access(typeof(SharedApcRequiresSlotSystem))]
public sealed partial class ApcRequiresSlotComponent : Component
{
    public const string BatterySlotId = "ancient-battery-slot";

    [DataField("batterySlot")]
    public ItemSlot BatterySlot = new();
}
