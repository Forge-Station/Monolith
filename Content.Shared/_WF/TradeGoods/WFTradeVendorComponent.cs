using Robust.Shared.GameStates;

namespace Content.Shared._WF.TradeGoods;

/// <summary>
/// A station trade vendor whose trade goods cost the station's live price. The server keeps <see cref="Prices"/> so the
/// vending menu shows what it will charge.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true), UnsavedComponent]
public sealed partial class WFTradeVendorComponent : Component
{
    /// <summary>What each trade good this vendor sells costs now, by prototype.</summary>
    [DataField, AutoNetworkedField]
    public Dictionary<string, int> Prices = new();
}
