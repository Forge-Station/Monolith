using System.Diagnostics.CodeAnalysis;
using Content.Shared.Shuttles.BUIStates;

namespace Content.Server.Shuttles.Systems;

public sealed partial class ShuttleConsoleSystem
{
    /// <summary>How long consoles share one walk of the docks. Anchoring changes raise nothing, so they wait this long.</summary>
    private static readonly TimeSpan WFDocksLifetime = TimeSpan.FromSeconds(0.5);

    private Dictionary<NetEntity, List<DockingPortState>>? _wfDocks;
    private TimeSpan _wfDocksUntil;

    private bool WFTryGetCachedDocks([NotNullWhen(true)] out Dictionary<NetEntity, List<DockingPortState>>? docks)
    {
        docks = _wfDocks;
        return docks != null && _timing.CurTime < _wfDocksUntil;
    }

    private void WFCacheDocks(Dictionary<NetEntity, List<DockingPortState>> docks)
    {
        _wfDocks = docks;
        _wfDocksUntil = _timing.CurTime + WFDocksLifetime;
    }

    /// <summary>Drops the shared walk when something consoles should show at once has changed.</summary>
    public void WFForgetDocks()
    {
        _wfDocks = null;
    }
}
