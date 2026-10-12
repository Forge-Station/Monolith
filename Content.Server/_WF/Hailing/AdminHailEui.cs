using Content.Server.Administration.Managers;
using Content.Server.EUI;
using Content.Shared._WF.Hailing;
using Content.Shared.Administration;
using Content.Shared.Eui;

namespace Content.Server._WF.Hailing;

/// <summary>
/// An admin's side of a hail made as God: the ordinary hail window, fed from a stand-in entity instead of a
/// console. Closing the window ends the hail.
/// </summary>
public sealed class AdminHailEui : BaseEui
{
    [Dependency] private IAdminManager _admins = default!;
    [Dependency] private IEntityManager _entManager = default!;

    private readonly EntityUid _endpoint;
    private readonly AdminHailSystem _system;

    public AdminHailEui(EntityUid endpoint, AdminHailSystem system)
    {
        IoCManager.InjectDependencies(this);
        _endpoint = endpoint;
        _system = system;
    }

    public override void Opened()
    {
        base.Opened();
        StateDirty();
    }

    public override EuiStateBase GetNewState()
    {
        return new AdminHailEuiState(_entManager.GetNetEntity(_endpoint));
    }

    public override void HandleMessage(EuiMessageBase msg)
    {
        base.HandleMessage(msg);

        if (IsShutDown)
            return;

        if (!_admins.HasAdminFlag(Player, AdminFlags.Fun))
        {
            Close();
            return;
        }

        switch (msg)
        {
            case AdminHailEuiMsg.Say say:
                _system.Say(_endpoint, say.Text);
                break;

            case AdminHailEuiMsg.End:
                _system.End(_endpoint);
                break;
        }
    }

    public override void Closed()
    {
        base.Closed();
        _system.Close(_endpoint);
    }

    /// <summary>Tells the admin's window why the hail or call stopped.</summary>
    public void SendEnded(HailEndReason reason, string ship)
    {
        if (!IsShutDown)
            SendMessage(new AdminHailEuiMsg.Ended(reason, ship));
    }
}
