using Robust.Shared.Serialization;

namespace Content.Shared._WF.TradeGoods;

/// <summary>What the Market PDA app shows: live station prices and sector alerts.</summary>
[Serializable, NetSerializable]
public sealed class WFMarketUiState : BoundUserInterfaceState
{
    public List<WFMarketStation> Stations = new();
    public List<WFMarketAlert> Alerts = new();

    /// <summary>
    /// This copy leaves out the hidden markets and the holder's own alerts: the full one was sent to the viewers
    /// alone in a <see cref="WFMarketPrivateStateEvent"/>, since UI state reaches every client near the PDA.
    /// </summary>
    public bool Private;
}

/// <summary>The full Market app state for a holder who sees the hidden markets, sent only to the players viewing it.</summary>
[Serializable, NetSerializable]
public sealed class WFMarketPrivateStateEvent(NetEntity loader, WFMarketUiState state) : EntityEventArgs
{
    public NetEntity Loader = loader;
    public WFMarketUiState State = state;
}

[Serializable, NetSerializable]
public sealed class WFMarketStation
{
    public string Name = string.Empty;

    /// <summary>What the station's chutes pay for each good.</summary>
    public List<WFMarketPrice> Prices = new();

    /// <summary>What the station's trade vendors charge for each good, cheapest first.</summary>
    public List<WFMarketPrice> Buys = new();

    /// <summary>An off-the-books market, listed under the app's Underground tab.</summary>
    public bool Underground;

    /// <summary>The faction the market belongs to, already localized; stations are grouped by it.</summary>
    public string Faction = string.Empty;

    public Color FactionColor = Color.Gray;
}

/// <summary>One good's live sell or buy price at a station; <c>Reason</c> is already localized.</summary>
[Serializable, NetSerializable]
public readonly record struct WFMarketPrice(string Good, int Amount, float Factor, string Reason, float Tonnes, bool Highlight);

/// <summary>A sector event in the Alerts tab; <c>Ends</c> is game time, or null for no countdown.</summary>
[Serializable, NetSerializable]
public readonly record struct WFMarketAlert(string Text, TimeSpan? Ends, bool Urgent, WFMarketAlertKind Kind = WFMarketAlertKind.Deal);

/// <summary>What an alert is about, for its colour and tag in the app.</summary>
[Serializable, NetSerializable]
public enum WFMarketAlertKind : byte
{
    /// <summary>Better prices or paid work: rushes, consignments, shortages, relief, war and raid demand, merchants.</summary>
    Deal,

    /// <summary>Ships moving goods: freighters inbound.</summary>
    Traffic,

    /// <summary>Trouble: blockades, traders who won't deal with the holder.</summary>
    Warning,

    /// <summary>About the holder alone: their faction's war pay.</summary>
    Personal,
}
