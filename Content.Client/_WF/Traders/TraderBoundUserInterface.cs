using Content.Shared._WF.Traders;
using Robust.Client.UserInterface;

namespace Content.Client._WF.Traders;

/// <summary>
/// Drives both of a trader's windows; which one is made depends on the UI key.
/// </summary>
public sealed class TraderBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private TraderDialogueWindow? _dialogue;

    [ViewVariables]
    private TraderShopWindow? _shop;

    [ViewVariables]
    private TraderUsedShipsWindow? _usedShips;

    public TraderBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        if (UiKey is TraderUiKey.UsedShips)
        {
            _usedShips = this.CreateWindow<TraderUsedShipsWindow>();
            _usedShips.Title = Loc.GetString("trader-used-window-title");
            _usedShips.OnBuyPressed += listing => SendMessage(new TraderUsedShipBuyMessage(listing));
            return;
        }

        if (UiKey is TraderUiKey.Shop)
        {
            _shop = this.CreateWindow<TraderShopWindow>();
            _shop.Title = Loc.GetString("trader-shop-window-title");
            _shop.OnPurchasePressed += items => SendMessage(new TraderShopCheckoutMessage(items));
            return;
        }

        _dialogue = this.CreateWindow<TraderDialogueWindow>();
        _dialogue.Title = Loc.GetString("trader-dialogue-window-title");
        _dialogue.SetTrader(Owner, Name(Owner));
        _dialogue.OnOptionPressed += index => SendMessage(new TraderDialogueSelectMessage(index));
        _dialogue.OnConfirmPressed += accepted => SendMessage(new TraderConfirmMessage(accepted));
    }

    private string Name(EntityUid uid)
    {
        return EntMan.TryGetComponent<MetaDataComponent>(uid, out var meta) ? meta.EntityName : string.Empty;
    }

    /// <summary>
    /// A basket only clears once the trader has actually been paid for it.
    /// </summary>
    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        base.ReceiveMessage(message);

        if (message is TraderShopCheckoutResultMessage { Success: true })
            _shop?.ClearBasket();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        switch (state)
        {
            case TraderDialogueState dialogue:
                _dialogue?.UpdateState(dialogue);
                break;
            case TraderShopState shop:
                _shop?.UpdateState(shop);
                break;
            case TraderUsedShipsState usedShips:
                _usedShips?.UpdateState(usedShips);
                break;
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing)
            return;

        _dialogue?.Dispose();
        _shop?.Dispose();
        _usedShips?.Dispose();
    }
}
