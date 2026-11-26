using Content.Shared._WF.NpcVehicleCrew;

namespace Content.Client._WF.NpcVehicleCrew;

/// <summary>Carries the vehicle crew panel's requests to the server and its answers back, refreshing while it is open.</summary>
public sealed class WFVehicleCrewPanelSystem : EntitySystem
{
    /// <summary>Seconds between refreshes of the live crews while a panel is open.</summary>
    private const float RefreshInterval = 2f;

    public event Action<WFVehicleCrewPanelState>? Received;

    private float _timer;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WFVehicleCrewPanelState>(state => Received?.Invoke(state));
    }

    public void Send(WFVehicleCrewPanelRequest request)
    {
        RaiseNetworkEvent(request);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);
        if (Received == null)
        {
            _timer = 0f;
            return;
        }

        _timer += frameTime;
        if (_timer < RefreshInterval)
            return;
        _timer = 0f;
        Send(new WFVehicleCrewPanelRequest { Action = WFVehicleCrewPanelAction.List });
    }
}
