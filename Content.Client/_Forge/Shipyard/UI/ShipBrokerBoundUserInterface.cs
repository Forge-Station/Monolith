using Content.Shared._Forge.Shipyard;
using JetBrains.Annotations;

namespace Content.Client._Forge.Shipyard.UI;

[UsedImplicitly]
public sealed class ShipBrokerBoundUserInterface : BoundUserInterface
{
    private ShipBrokerWindow? _window;

    public ShipBrokerBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _window = new ShipBrokerWindow(this);
        _window.OnClose += Close;
        _window.OpenCentered();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
            return;
        _window?.Dispose();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is ShipBrokerBoundUserInterfaceState cast)
            _window?.UpdateState(cast);
    }

    public void SendPrice(int price) => SendMessage(new ShipBrokerSetPriceMessage(price));
    public void SendConfirm() => SendMessage(new ShipBrokerConfirmSaleMessage());
}
