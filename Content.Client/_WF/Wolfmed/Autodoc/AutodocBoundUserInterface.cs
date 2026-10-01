using Content.Shared._WF.Wolfmed.Autodoc;
using Robust.Client.UserInterface;

namespace Content.Client._WF.Wolfmed.Autodoc;

/// <summary>Opens the autodoc window and relays its buttons to the server as messages.</summary>
public sealed class AutodocBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private AutodocWindow? _window;

    public AutodocBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<AutodocWindow>();
        _window.OnQueueAdd += (surgery, part) => SendMessage(new AutodocQueueAddMessage(surgery, part));
        _window.OnQueueRemove += index => SendMessage(new AutodocQueueRemoveMessage(index));
        _window.OnQueueMove += (index, up) => SendMessage(new AutodocQueueMoveMessage(index, up));
        _window.OnControl += control => SendMessage(new AutodocControlMessage(control));
        _window.OnAnaesthesia += enabled => SendMessage(new AutodocAnaesthesiaMessage(enabled));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is AutodocBuiState autodoc)
            _window?.Update(autodoc);
    }
}
