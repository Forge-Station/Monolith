using Content.Shared.Containers.ItemSlots;
using Content.Shared._Forge.Crypto.Components;
using Robust.Client.GameObjects;

namespace Content.Client._Forge.Crypto.UI;

public sealed class CryptoSellConsoleBoundUserInterface : BoundUserInterface
{
    private CryptoSellConsoleWindow? _window;

    public CryptoSellConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = new CryptoSellConsoleWindow
        {
            Title = EntMan.GetComponent<MetaDataComponent>(Owner).EntityName
        };

        _window.InsertButton.OnPressed += _ => SendMessage(new ItemSlotButtonPressedEvent(CryptoSellConsoleComponent.BitcoinSlotId));
        _window.SellButton.OnPressed += _ => SendMessage(new CryptoSellRequestMessage());
        _window.EjectButton.OnPressed += _ => SendMessage(new CryptoSellEjectMessage());
        _window.OnClose += Close;
        _window.OpenCentered();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        _window?.UpdateState((CryptoSellConsoleBoundUserInterfaceState) state);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
            return;

        _window?.Dispose();
    }
}
