using Robust.Shared.Serialization;
using Robust.Shared.Utility;

namespace Content.Shared._Forge.Shipyard;

[Serializable, NetSerializable]
public enum ShipBrokerUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class ShipBrokerBoundUserInterfaceState : BoundUserInterfaceState
{
    public readonly int? ListedPrice;
    public readonly int? Appraisal;
    public readonly string? VesselName;
    public readonly bool SellerPresent;
    public readonly bool BuyerPresent;
    public readonly float BrokerFeeRate;

    public ShipBrokerBoundUserInterfaceState(
        int? listedPrice,
        int? appraisal,
        string? vesselName,
        bool sellerPresent,
        bool buyerPresent,
        float brokerFeeRate)
    {
        ListedPrice = listedPrice;
        Appraisal = appraisal;
        VesselName = vesselName;
        SellerPresent = sellerPresent;
        BuyerPresent = buyerPresent;
        BrokerFeeRate = brokerFeeRate;
    }
}

[Serializable, NetSerializable]
public sealed class ShipBrokerSetPriceMessage : BoundUserInterfaceMessage
{
    public readonly int Price;

    public ShipBrokerSetPriceMessage(int price)
    {
        Price = price;
    }
}

[Serializable, NetSerializable]
public sealed class ShipBrokerConfirmSaleMessage : BoundUserInterfaceMessage;
