using Robust.Shared.Serialization;

namespace Content.Shared._Forge.BlackMarket.BUI;

[Serializable, NetSerializable]
public enum BlackMarketConsoleUiKey : byte
{
    Key,
}

[NetSerializable, Serializable]
public sealed class BlackMarketConsoleState : BoundUserInterfaceState
{
    public List<BlackMarketSlotState> Slots = new();
    public int Balance;

    public BlackMarketConsoleState(List<BlackMarketSlotState> slots, int balance)
    {
        Slots = slots;
        Balance = balance;
    }
}

[NetSerializable, Serializable]
public sealed class BlackMarketSlotState
{
    public int SlotIndex;
    public string CategoryId = string.Empty;
    public string? ContractId;
    public string? IconPrototype;
    public int Price;
    public int PurchasesRemaining = -1;
    public TimeSpan UntilRefresh;
    public bool Available;

    public BlackMarketSlotState(
        int slotIndex,
        string categoryId,
        string? contractId,
        string? iconPrototype,
        int price,
        int purchasesRemaining,
        TimeSpan untilRefresh,
        bool available)
    {
        SlotIndex = slotIndex;
        CategoryId = categoryId;
        ContractId = contractId;
        IconPrototype = iconPrototype;
        Price = price;
        PurchasesRemaining = purchasesRemaining;
        UntilRefresh = untilRefresh;
        Available = available;
    }
}

[Serializable, NetSerializable]
public sealed class BlackMarketPurchaseMessage : BoundUserInterfaceMessage
{
    public int SlotIndex;

    public BlackMarketPurchaseMessage(int slotIndex)
    {
        SlotIndex = slotIndex;
    }
}
