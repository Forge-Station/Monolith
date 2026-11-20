using Content.Server.Administration.Managers;
using Content.Server.EUI;
using Content.Shared._WF.Announcements;
using Content.Shared.Administration;
using Content.Shared.Eui;

namespace Content.Server._WF.Announcements;

/// <summary>Server half of the roleplay announcement window.</summary>
public sealed partial class WFAnnounceEui : BaseEui
{
    [Dependency] private IAdminManager _admin = default!;
    [Dependency] private IEntitySystemManager _systems = default!;

    private string? _status;
    private bool _error;

    public WFAnnounceEui()
    {
        IoCManager.InjectDependencies(this);
    }

    public override void Opened()
    {
        StateDirty();
    }

    public override EuiStateBase GetNewState()
    {
        return new WFAnnounceEuiState(_status, _error);
    }

    public override void HandleMessage(EuiMessageBase msg)
    {
        base.HandleMessage(msg);

        if (msg is not WFAnnounceSendMessage send)
            return;

        if (!_admin.HasAdminFlag(Player, AdminFlags.Admin))
        {
            Close();
            return;
        }

        var error = _systems.GetEntitySystem<WFAnnounceSystem>().TrySend(Player, send.Request);
        _error = error != null;
        _status = Loc.GetString(error ?? "wf-announce-status-sent");
        StateDirty();

        if (error == null && send.CloseAfter)
            Close();
    }
}
