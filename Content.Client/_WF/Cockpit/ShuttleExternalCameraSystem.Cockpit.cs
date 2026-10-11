using Content.Shared.Shuttles.Components;

namespace Content.Client._WF.Shuttles.Systems;

public sealed partial class ShuttleExternalCameraSystem
{
    private void WfSendOrderedCameraInput(EntityUid console, BoundUserInterfaceMessage message)
    {
        if (_ui.TryGetOpenUi(console, ShuttleConsoleUiKey.Key, out var bui))
            bui.SendPredictedMessage(message);
    }

    /// <summary>Stops queued wheel retries and dragging before the server restores the prior cockpit camera.</summary>
    public void WfEndCockpitInput()
    {
        _pendingZoom = null;
        _sentZoom = null;
        _wheel = 0f;
        EndDrag();
        _sentLook = _look;
        _sentAt = _timing.CurTime;
    }
}
