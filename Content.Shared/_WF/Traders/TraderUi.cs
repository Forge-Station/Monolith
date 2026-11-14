using Robust.Shared.Serialization;

namespace Content.Shared._WF.Traders;

/// <summary>
/// UI keys a trader can open on a customer.
/// </summary>
[Serializable, NetSerializable]
public enum TraderUiKey : byte
{
    Dialogue,
    Shop,
    UsedShips,
}

/// <summary>
/// The customer picked a numbered dialogue option.
/// </summary>
[Serializable, NetSerializable]
public sealed class TraderDialogueSelectMessage : BoundUserInterfaceMessage
{
    public int Index;

    public TraderDialogueSelectMessage(int index)
    {
        Index = index;
    }
}

/// <summary>
/// The customer answered a yes/no question.
/// </summary>
[Serializable, NetSerializable]
public sealed class TraderConfirmMessage : BoundUserInterfaceMessage
{
    public bool Accepted;

    public TraderConfirmMessage(bool accepted)
    {
        Accepted = accepted;
    }
}

/// <summary>
/// The customer asked to buy everything in their basket, as item prototype to quantity.
/// </summary>
[Serializable, NetSerializable]
public sealed class TraderShopCheckoutMessage : BoundUserInterfaceMessage
{
    public Dictionary<string, int> Items;

    public TraderShopCheckoutMessage(Dictionary<string, int> items)
    {
        Items = items;
    }
}

/// <summary>
/// How a checkout went, so the client knows whether to keep the basket.
/// </summary>
[Serializable, NetSerializable]
public sealed class TraderShopCheckoutResultMessage : BoundUserInterfaceMessage
{
    public bool Success;

    public TraderShopCheckoutResultMessage(bool success)
    {
        Success = success;
    }
}

/// <summary>
/// What the dialogue window shows right now.
/// </summary>
[Serializable, NetSerializable]
public sealed class TraderDialogueState : BoundUserInterfaceState
{
    /// <summary>
    /// Line the trader last said.
    /// </summary>
    public string Line;

    /// <summary>
    /// Option prompts, in menu order.
    /// </summary>
    public List<string> Options;

    /// <summary>
    /// Show yes/no instead of the option list.
    /// </summary>
    public bool Confirming;

    /// <summary>
    /// Show a text box with this placeholder instead of the option list.
    /// </summary>
    public string? TextPrompt;

    /// <summary>
    /// Longest answer the text box takes.
    /// </summary>
    public int TextMaxLength;

    public TraderDialogueState(string line, List<string> options, bool confirming, string? textPrompt = null, int textMaxLength = 0)
    {
        Line = line;
        Options = options;
        Confirming = confirming;
        TextPrompt = textPrompt;
        TextMaxLength = textMaxLength;
    }
}

/// <summary>
/// The customer's answer to a text prompt; null when they backed out.
/// </summary>
[Serializable, NetSerializable]
public sealed class TraderTextMessage : BoundUserInterfaceMessage
{
    public string? Text;

    public TraderTextMessage(string? text)
    {
        Text = text;
    }
}

/// <summary>
/// One line of a trader's shop stock.
/// </summary>
[Serializable, NetSerializable]
public record struct TraderShopEntry(string Item, int Price);

/// <summary>
/// The customer asked to buy a used ship off the lot.
/// </summary>
[Serializable, NetSerializable]
public sealed class TraderUsedShipBuyMessage : BoundUserInterfaceMessage
{
    public int Listing;

    public TraderUsedShipBuyMessage(int listing)
    {
        Listing = listing;
    }
}

/// <summary>
/// One ship on the used lot, as the window shows it.
/// </summary>
[Serializable, NetSerializable]
public record struct TraderUsedShipEntry(int Id, string ShipName, string Design, string Seller, int Price);

/// <summary>
/// What the used ship window shows right now.
/// </summary>
[Serializable, NetSerializable]
public sealed class TraderUsedShipsState : BoundUserInterfaceState
{
    /// <summary>
    /// Ships currently on the lot, cheapest first.
    /// </summary>
    public List<TraderUsedShipEntry> Entries;

    /// <summary>
    /// Cash sitting in the barter zone.
    /// </summary>
    public int CashInZone;

    /// <summary>
    /// Owner of the ID in the barter zone, if there is a usable one.
    /// </summary>
    public string? IdName;

    /// <summary>
    /// Customer's bank balance.
    /// </summary>
    public int Balance;

    public TraderUsedShipsState(List<TraderUsedShipEntry> entries, int cashInZone, string? idName, int balance)
    {
        Entries = entries;
        CashInZone = cashInZone;
        IdName = idName;
        Balance = balance;
    }
}

/// <summary>
/// What the shop window shows right now.
/// </summary>
[Serializable, NetSerializable]
public sealed class TraderShopState : BoundUserInterfaceState
{
    /// <summary>
    /// Stock, already priced by the server.
    /// </summary>
    public List<TraderShopEntry> Entries;

    /// <summary>
    /// Cash sitting in the barter zone.
    /// </summary>
    public int CashInZone;

    /// <summary>
    /// Owner of the ID in the barter zone, if there is a usable one.
    /// </summary>
    public string? IdName;

    /// <summary>
    /// Customer's bank balance.
    /// </summary>
    public int Balance;

    public TraderShopState(List<TraderShopEntry> entries, int cashInZone, string? idName, int balance)
    {
        Entries = entries;
        CashInZone = cashInZone;
        IdName = idName;
        Balance = balance;
    }
}
