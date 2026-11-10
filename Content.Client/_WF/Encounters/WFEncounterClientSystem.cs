using Content.Shared._WF.Encounters;

namespace Content.Client._WF.Encounters;

/// <summary>Sends the admin encounter window's requests and keeps it refreshed while it is open.</summary>
public sealed partial class WFEncounterClientSystem : EntitySystem
{
    private const float PollInterval = 2f;

    /// <summary>Raised with each state the server sends; the window subscribes while open.</summary>
    public event Action<WFEncounterAdminState>? Received;

    private float _pollTimer;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WFEncounterAdminState>(state => Received?.Invoke(state));
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);
        if (Received == null)
        {
            _pollTimer = PollInterval;
            return;
        }

        _pollTimer += frameTime;
        if (_pollTimer < PollInterval)
            return;

        _pollTimer = 0f;
        Send(new WFEncounterAdminRequest());
    }

    public void Send(WFEncounterAdminRequest request)
    {
        RaiseNetworkEvent(request);
    }
}
