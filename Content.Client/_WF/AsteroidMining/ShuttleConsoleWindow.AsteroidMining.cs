using Content.Client._WF.AsteroidMining;
using Content.Client._WF.Cockpit;
using Content.Shared._WF.AsteroidMining;
using Robust.Client.UserInterface;

namespace Content.Client.Shuttles.UI;

public sealed partial class ShuttleConsoleWindow
{
    private WFCockpitMiningPage? _wfCockpitMining;

    /// <summary>The last mining scan sent through this helm, so a cockpit opened again starts from it.</summary>
    public WFMiningBeamGunneryState WfCockpitMiningState { get; private set; }

    /// <summary>Builds the cockpit's mining page, which sets the lasers through the helm's gunnery link.</summary>
    private Control WfCockpitMiningPage(WFCockpitLease lease)
    {
        var page = new WFCockpitMiningPage();
        page.PowerRequested += power => WfSendCockpitGunnery(new WFMiningBeamPowerMessage(power));
        page.UpdateState(WfCockpitMiningState);
        _wfCockpitMining = page;
        lease.Remember(() =>
        {
            if (_wfCockpitMining == page)
                _wfCockpitMining = null;
        });
        return page;
    }

    /// <summary>Applies a mining scan sent through the helm to the cockpit's mining page.</summary>
    public void WfReceiveCockpitMining(WFMiningBeamGunneryState state)
    {
        WfCockpitMiningState = state;
        _wfCockpitMining?.UpdateState(state);
    }
}
