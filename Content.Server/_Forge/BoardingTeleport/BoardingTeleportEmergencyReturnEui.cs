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
    private readonly BoardingTeleportPlatformSystem _platformSystem;

    public BoardingTeleportEmergencyReturnEui(
        EntityUid platform,
        EntityUid user,
        string title,
        string message,
        BoardingTeleportPlatformSystem platformSystem)
    {
        _platform = platform;
        _user = user;
        _title = title;
        _message = message;
        _platformSystem = platformSystem;
    }

    public override EuiStateBase GetNewState()
    {
        return new BoardingTeleportEmergencyReturnState(_title, _message);
    }

    public override void HandleMessage(EuiMessageBase msg)
    {
        base.HandleMessage(msg);

        if (msg is BoardingTeleportEmergencyReturnResponseMessage response)
            _platformSystem.CompleteEmergencyReturnResponse(_user, _platform, response.Accepted);

        Close();
    }

    public override void Closed()
    {
        _platformSystem.OnEmergencyReturnEuiClosed(_user);
        base.Closed();
    }
}
