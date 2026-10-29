namespace Content.Shared._WF.TradeGoods;

/// <summary>
/// A cargo-console trading crate whose pallet price follows the selling station's market: each sale there lowers what
/// the next one pays, and time restores it.
/// </summary>
[RegisterComponent]
public sealed partial class WFTradeCrateMarketComponent : Component
{
    /// <summary>The market good the crate counts as; crates sharing one share a station's demand.</summary>
    [DataField]
    public string Good = "WFTradeCrate";
}
