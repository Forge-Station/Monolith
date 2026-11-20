using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Content.Shared._WF.RoundSetup;
using Robust.Shared.Timing;

namespace Content.Client._WF.RoundSetup;

public sealed partial class WFRoundSetupWindow
{
    /// <summary>Renews the server's live-update lease well inside its 60-second expiry.</summary>
    private static readonly TimeSpan WatchInterval = TimeSpan.FromSeconds(20);

    [Dependency] private IGameTiming _watchTiming = default!;
    private TimeSpan _nextWatch;

    /// <summary>Keeps snapshots flowing while open; a closed window lets the lease lapse.</summary>
    private void KeepWatching()
    {
        var now = _watchTiming.RealTime;
        if (_nextWatch == TimeSpan.Zero)
            _nextWatch = now + WatchInterval;
        if (now < _nextWatch)
            return;
        _nextWatch = now + WatchInterval;
        // Sent directly so the keep-alive leaves on-screen messages and redraw state alone.
        _system.Send(new WFRoundSetupRequest(WFRoundSetupAction.Watch));
    }

    /// <summary>Defers background rebuilding while typing or using this window's detached dropdown popup.</summary>
    private bool IsEditingControls()
    {
        for (var focused = UserInterfaceManager.KeyboardFocused; focused != null; focused = focused.Parent)
        {
            if (focused == this)
                return true;
        }
        return HasOpenSelector(this);
    }

    private static bool HasOpenSelector(Control root)
    {
        foreach (var child in root.Children)
        {
            if (child is OptionButton option && option.OptionsScroll.VisibleInTree || HasOpenSelector(child))
                return true;
        }
        return false;
    }
}
