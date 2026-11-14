using Content.Server.Administration.Managers;
using Content.Server.EUI;
using Content.Shared._WF.Defcon;
using Content.Shared.Administration;
using Content.Shared.Eui;
using Robust.Shared.Timing;

namespace Content.Server._WF.Defcon;

/// <summary>Server half of the DEFCON admin panel.</summary>
public sealed partial class WFDefconEui : BaseEui
{
    [Dependency] private IAdminManager _admin = default!;
    [Dependency] private IEntitySystemManager _systems = default!;
    [Dependency] private IGameTiming _timing = default!;

    private readonly WFDefconSystem _defcon;

    public WFDefconEui()
    {
        IoCManager.InjectDependencies(this);
        _defcon = _systems.GetEntitySystem<WFDefconSystem>();
    }

    public override void Opened()
    {
        _defcon.Panels.Add(this);
        StateDirty();
    }

    public override void Closed()
    {
        _defcon.Panels.Remove(this);
    }

    public override EuiStateBase GetNewState()
    {
        return _defcon.PanelState();
    }

    public override void HandleMessage(EuiMessageBase msg)
    {
        base.HandleMessage(msg);

        if (!_admin.HasAdminFlag(Player, AdminFlags.Admin))
        {
            Close();
            return;
        }

        switch (msg)
        {
            case WFDefconSetLevelMessage set when set.Level is >= WFDefconTracker.Severest and <= WFDefconTracker.Calmest:
                _defcon.SetLevel(set.Level, set.Locked, set.Announce);
                break;
            case WFDefconUnlockMessage:
                _defcon.Unlock();
                break;
            case WFDefconBiasMessage bias when float.IsFinite(bias.Bias):
                _defcon.Bias = bias.Bias;
                _defcon.Evaluate(_timing.CurTime);
                break;
            case WFDefconEvaluateMessage:
                _defcon.Evaluate(_timing.CurTime);
                break;
            default:
                return;
        }

        StateDirty();
    }
}
