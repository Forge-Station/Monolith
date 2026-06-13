using Content.Shared.Containers.ItemSlots;

namespace Content.Shared._Forge.Shipyard.Components;

[RegisterComponent]
public sealed partial class ShipBrokerConsoleComponent : Component
{
    public const string SellerSlotId = "ship-broker-seller";
    public const string BuyerSlotId = "ship-broker-buyer";

    [DataField]
    public ItemSlot SellerSlot = new();

    [DataField]
    public ItemSlot BuyerSlot = new();

    [DataField]
    public float BrokerFeeRate = 0.08f;
}
