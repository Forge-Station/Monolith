using Content.Shared._WF.TradeGoods;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.TradeGoods.Components;

/// <summary>One good's line in a station's ledger.</summary>
public sealed class WFTradeRow
{
    /// <summary>Crates on hand.</summary>
    public float Stock;

    /// <summary>The level prices see: stock smoothed over a few minutes.</summary>
    public float Priced;

    /// <summary>Crates owed from the sector's pool, waiting for a freighter.</summary>
    public float Inbound;

    /// <summary>Crates made and used per hour at the last tick.</summary>
    public float Made;

    public float Used;

    /// <summary>When the good last turned scarce here, or null if it isn't.</summary>
    public TimeSpan? ScarceSince;
}

/// <summary>One station's market, on the grid its chutes stand on. Added on the first quote.</summary>
[RegisterComponent]
public sealed partial class WFTradeMarketComponent : Component
{
    [DataField]
    public ProtoId<WFTradeMarketPrototype> Profile = "WFTradeMarketDefault";

    /// <summary>Console trading crate pressure per good and when it was last decayed.</summary>
    [ViewVariables]
    public Dictionary<string, (float Pressure, TimeSpan Touched)> Pressure = new();

    /// <summary>The station's ledger: a row per good it trades.</summary>
    [ViewVariables]
    public Dictionary<string, WFTradeRow> Ledger = new();

    /// <summary>Crates of each good delivered here this round.</summary>
    [ViewVariables]
    public Dictionary<string, int> Delivered = new();
}

/// <summary>Names the station a grid is, for its role in the sector economy: the id of its POI or game map.</summary>
[RegisterComponent]
public sealed partial class WFTradeStationComponent : Component
{
    [DataField(required: true)]
    public string Id = string.Empty;
}

/// <summary>The stock changes a station's material prices haven't caught up with yet, and what it has part used.</summary>
[RegisterComponent]
public sealed partial class WFMaterialPriceLagComponent : Component
{
    /// <summary>Fractions of a unit of each material used up, carried until a whole unit is gone.</summary>
    [ViewVariables]
    public Dictionary<string, double> Used = new();

    /// <summary>Each material's stock when it was last read.</summary>
    [ViewVariables]
    public Dictionary<string, int> Seen = new();

    /// <summary>Changes in stock still to reach prices, and when they do.</summary>
    [ViewVariables]
    public List<(string Good, int Change, TimeSpan Due)> Pending = new();
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
