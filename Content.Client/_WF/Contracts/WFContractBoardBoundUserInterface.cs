using Content.Client.UserInterface.Controls;
using Content.Shared._WF.Contracts;
using Robust.Client.UserInterface;

namespace Content.Client._WF.Contracts;

/// <summary>The contract board's window. Its state comes as a message to this viewer alone.</summary>
public sealed class WFContractBoardBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    [ViewVariables]
    private WFContractBoardWindow? _window;

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<WFContractBoardWindow>();
        _window.SetInfoFromEntity(EntMan, Owner);
        _window.OnAccept += id => SendMessage(new WFContractAcceptMessage(id));
        _window.OnAbandon += id => SendMessage(new WFContractAbandonMessage(id));
        SendMessage(new WFContractRefreshMessage());
    }

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        base.ReceiveMessage(message);
        if (message is WFContractBoardStateMessage state)
            _window?.UpdateState(state.State);
    }
}
