using Content.Server.EUI;
using Content.Shared._Forge.BoardingTeleport;
using Content.Shared.Eui;

namespace Content.Server._Forge.BoardingTeleport;

public sealed class BoardingTeleportEmergencyReturnEui : BaseEui
{
    private readonly EntityUid _platform;
    private readonly EntityUid _user;
    private readonly string _title;
    private readonly string _message;
    private readonly string _confirmButton;
    private readonly BoardingTeleportReturnConfirmKind _kind;
    private readonly BoardingTeleportPlatformSystem _platformSystem;

    public BoardingTeleportEmergencyReturnEui(
        EntityUid platform,
        EntityUid user,
        string title,
        string message,
        string confirmButton,
        BoardingTeleportReturnConfirmKind kind,
        BoardingTeleportPlatformSystem platformSystem)
    {
        _platform = platform;
        _user = user;
        _title = title;
        _message = message;
        _confirmButton = confirmButton;
        _kind = kind;
        _platformSystem = platformSystem;
    }

    public override void Opened()
    {
        StateDirty();
    }

    public override EuiStateBase GetNewState()
    {
        return new BoardingTeleportEmergencyReturnState(_title, _message, _confirmButton, _kind);
    }

    public override void HandleMessage(EuiMessageBase msg)
    {
        base.HandleMessage(msg);

        if (msg is BoardingTeleportEmergencyReturnResponseMessage response)
            _platformSystem.CompleteReturnConfirmResponse(_user, _platform, response.Kind, response.Accepted);

        Close();
    }

    public override void Closed()
    {
        _platformSystem.OnReturnConfirmEuiClosed(_user);
        base.Closed();
    }
}
