using Content.Server._WF.TradeGoods.Systems;
using Robust.Shared.Prototypes;

namespace Content.Server.VendingMachines;

public sealed partial class VendingMachineSystem
{
    [Dependency] private WFTradeMarketSystem _wfMarket = default!;

    /// <summary>The live price a trade vendor charges for a trade good, or the machine's own price for anything else.</summary>
    private int WfTradeBuyPrice(EntityUid vendor, EntityPrototype proto, int price)
    {
        return _wfMarket.BuyPrice(vendor, proto, price);
    }
}
