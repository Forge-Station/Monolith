using Content.Shared._WF.TradeGoods;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.TradeGoods.Components;

/// <summary>One station's market, on the grid its chutes stand on. Added on the first quote.</summary>
[RegisterComponent]
public sealed partial class WFTradeMarketComponent : Component
{
    [DataField]
    public ProtoId<WFTradeMarketPrototype> Profile = "WFTradeMarketDefault";

    /// <summary>Recent-sales pressure per good and when it was last decayed.</summary>
    [ViewVariables]
    public Dictionary<string, (float Pressure, TimeSpan Touched)> Pressure = new();

    /// <summary>Recent-purchase scarcity per good at this station's vendors and when it was last decayed.</summary>
    [ViewVariables]
    public Dictionary<string, (float Scarcity, TimeSpan Touched)> Scarcity = new();

    /// <summary>Crates of each good delivered here this round.</summary>
    [ViewVariables]
    public Dictionary<string, int> Delivered = new();
}

/// <summary>A chute's sale in flight: the amount quoted when the crate went in, paid when the chute dispenses.</summary>
[RegisterComponent]
public sealed partial class WFTradeChuteComponent : Component
{
    [ViewVariables]
    public int? PendingAmount;
}

/// <summary>Marks a map besides the sector map as a market map, such as a planet with outposts.</summary>
[RegisterComponent]
public sealed partial class WFTradeMarketMapComponent : Component;
