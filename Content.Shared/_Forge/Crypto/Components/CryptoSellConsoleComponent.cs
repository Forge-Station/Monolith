using Content.Shared.Containers.ItemSlots;
using Content.Shared.Stacks;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Forge.Crypto.Components;

[RegisterComponent, NetworkedComponent]
public sealed partial class CryptoSellConsoleComponent : Component
{
    public const string BitcoinSlotId = "CryptoSellConsole-bitcoin";

    [DataField]
    public ItemSlot BitcoinSlot = new();

    [DataField]
    public ProtoId<StackPrototype> CashType = "Credit";
}

[Serializable, NetSerializable]
public sealed class CryptoSellRequestMessage : BoundUserInterfaceMessage
{
}

[Serializable, NetSerializable]
public sealed class CryptoSellEjectMessage : BoundUserInterfaceMessage
{
}

[Serializable, NetSerializable]
public sealed class CryptoSellConsoleBoundUserInterfaceState : BoundUserInterfaceState
{
    public readonly bool HasBitcoin;
    public readonly string InsertedName;
    public readonly int InsertedUnits;
    public readonly double CurrentUnitPrice;
    public readonly int PreviewPayout;
    public readonly List<double> PriceHistory;
    public readonly List<CryptoMarketUiData> Markets;

    public CryptoSellConsoleBoundUserInterfaceState(
        bool hasBitcoin,
        string insertedName,
        int insertedUnits,
        double currentUnitPrice,
        int previewPayout,
        List<double> priceHistory,
        List<CryptoMarketUiData> markets)
    {
        HasBitcoin = hasBitcoin;
        InsertedName = insertedName;
        InsertedUnits = insertedUnits;
        CurrentUnitPrice = currentUnitPrice;
        PreviewPayout = previewPayout;
        PriceHistory = priceHistory;
        Markets = markets;
    }
}

[Serializable, NetSerializable]
public sealed class CryptoMarketUiData
{
    public readonly string MarketId;
    public readonly double CurrentPrice;
    public readonly List<double> PriceHistory;

    public CryptoMarketUiData(string marketId, double currentPrice, List<double> priceHistory)
    {
        MarketId = marketId;
        CurrentPrice = currentPrice;
        PriceHistory = priceHistory;
    }
}

[Serializable, NetSerializable]
public enum CryptoSellConsoleUiKey : byte
{
    Key,
}
