using Content.Client._Forge.BlackMarket.UI;
using Content.Shared._Forge.BlackMarket.BUI;
using JetBrains.Annotations;

namespace Content.Client._Forge.BlackMarket.BUI;

[UsedImplicitly]
public sealed class BlackMarketConsoleBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private BlackMarketMenu? _menu;

    public BlackMarketConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _menu = new BlackMarketMenu();
        _menu.OnClose += Close;
        _menu.OnPurchasePressed += slotIndex => SendMessage(new BlackMarketPurchaseMessage(slotIndex));
        _menu.OpenCentered();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not BlackMarketConsoleState consoleState)
            return;

        _menu?.UpdateState(consoleState);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing)
            return;

        _menu?.Dispose();
    }
}
