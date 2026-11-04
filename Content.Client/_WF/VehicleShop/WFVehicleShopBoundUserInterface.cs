using Content.Shared._WF.VehicleShop;
using Robust.Client.UserInterface;

namespace Content.Client._WF.VehicleShop;

/// <summary>Opens a vehicle purchase terminal's window and sends its buy, sell and repair presses.</summary>
public sealed class WFVehicleShopBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private WFVehicleShopWindow? _window;

    public WFVehicleShopBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<WFVehicleShopWindow>();
        _window.Title = EntMan.GetComponent<MetaDataComponent>(Owner).EntityName;
        _window.OnBuy += index => SendMessage(new WFVehicleShopBuyMessage(index));
        _window.OnSell += () => SendMessage(new WFVehicleShopSellMessage());
        _window.OnRepair += () => SendMessage(new WFVehicleShopRepairMessage());
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is WFVehicleShopState shop)
            _window?.UpdateState(shop);
    }
}
