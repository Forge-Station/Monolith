using Content.Client.Eui;
using Content.Shared._Forge.BoardingTeleport;
using Content.Shared.Eui;
using JetBrains.Annotations;
using Robust.Client.Graphics;

namespace Content.Client._Forge.BoardingTeleport;

[UsedImplicitly]
public sealed class BoardingTeleportEmergencyReturnEui : BaseEui
{
    private readonly BoardingTeleportEmergencyConfirmWindow _window;
    private BoardingTeleportReturnConfirmKind _kind;

    public BoardingTeleportEmergencyReturnEui()
    {
        _window = new BoardingTeleportEmergencyConfirmWindow(string.Empty, string.Empty, string.Empty);

        _window.Confirmed += () =>
        {
            SendMessage(new BoardingTeleportEmergencyReturnResponseMessage(accepted: true, _kind));
            _window.Close();
        };

        _window.Cancelled += () =>
        {
            SendMessage(new BoardingTeleportEmergencyReturnResponseMessage(accepted: false, _kind));
            _window.Close();
        };
    }

    public override void Opened()
    {
        IoCManager.Resolve<IClyde>().RequestWindowAttention();
    }

    public override void HandleState(EuiStateBase state)
    {
        if (state is not BoardingTeleportEmergencyReturnState confirmState)
            return;

        _kind = confirmState.Kind;
        _window.SetContent(confirmState.Title, confirmState.Message, confirmState.ConfirmButton);
        _window.OpenCentered();
    }

    public override void Closed()
    {
        _window.Close();
    }
}
