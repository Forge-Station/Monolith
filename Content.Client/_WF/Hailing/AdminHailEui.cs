using Content.Client.Eui;
using Content.Shared._WF.Hailing;
using Content.Shared.Eui;
using JetBrains.Annotations;

namespace Content.Client._WF.Hailing;

/// <summary>
/// An admin's window onto a hail made as God. It is the ordinary hail window without a contact list, reading
/// the hail off a stand-in entity instead of a console.
/// </summary>
[UsedImplicitly]
public sealed class AdminHailEui : BaseEui
{
    private readonly HailWindow _window;

    public AdminHailEui()
    {
        _window = new HailWindow();
        _window.SetStandalone();
        _window.OnClose += () => SendMessage(new CloseEuiMessage());
        _window.SayRequested += text => SendMessage(new AdminHailEuiMsg.Say(text));
        _window.EndRequested += () => SendMessage(new AdminHailEuiMsg.End());
    }

    public override void Opened()
    {
        _window.OpenCentered();
    }

    public override void Closed()
    {
        base.Closed();
        _window.Close();
    }

    public override void HandleState(EuiStateBase state)
    {
        if (state is AdminHailEuiState hail)
            _window.SetConsole(hail.Endpoint);
    }

    public override void HandleMessage(EuiMessageBase msg)
    {
        base.HandleMessage(msg);

        if (msg is AdminHailEuiMsg.Ended ended)
            _window.ShowEnded(ended.Reason, ended.Ship);
    }
}
