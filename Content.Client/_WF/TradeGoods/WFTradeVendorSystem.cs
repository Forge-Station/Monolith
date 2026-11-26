using Content.Client.VendingMachines;
using Content.Shared._WF.TradeGoods;
using Content.Shared.VendingMachines;

namespace Content.Client._WF.TradeGoods;

/// <summary>Refreshes an open trade vendor's menu when its live prices change.</summary>
public sealed partial class WFTradeVendorSystem : EntitySystem
{
    [Dependency] private SharedUserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFTradeVendorComponent, AfterAutoHandleStateEvent>(OnPrices);
    }

    private void OnPrices(Entity<WFTradeVendorComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        if (_ui.TryGetOpenUi<VendingMachineBoundUserInterface>(ent.Owner, VendingMachineUiKey.Key, out var bui))
            bui.Update();
    }
}
