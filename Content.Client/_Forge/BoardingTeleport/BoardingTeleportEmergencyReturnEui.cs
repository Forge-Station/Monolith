using Content.Client.Eui;
using Content.Shared._Forge.BoardingTeleport;
using Content.Shared.Eui;
using JetBrains.Annotations;

namespace Content.Client._Forge.BoardingTeleport;

[UsedImplicitly]
public sealed class BoardingTeleportEmergencyReturnEui : BaseEui
{
    private readonly BoardingTeleportEmergencyConfirmWindow _window;
    private bool _opened;

    public BoardingTeleportEmergencyReturnEui()
    {
        _window = new BoardingTeleportEmergencyConfirmWindow(string.Empty, string.Empty);

        _window.Confirmed += () =>
        {
            SendMessage(new BoardingTeleportEmergencyReturnResponseMessage(accepted: true));
            _window.Close();
        };

        _window.Cancelled += () =>
        {
            SendMessage(new BoardingTeleportEmergencyReturnResponseMessage(accepted: false));
            _window.Close();
        };
    }

    public override void HandleState(EuiStateBase state)
    {
        if (state is not BoardingTeleportEmergencyReturnState confirmState)
            return;

        _window.SetContent(confirmState.Title, confirmState.Message);

        if (_opened)
            return;

        _opened = true;
        _window.OpenCentered();
    }

    public override void Closed()
    {
        _opened = false;
        _window.Close();
    }
}
