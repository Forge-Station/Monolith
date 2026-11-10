using Content.Shared._WF.RoundSetup;

namespace Content.Client._WF.RoundSetup;

/// <summary>Receives personalized lobby snapshots and sends authenticated setup requests.</summary>
public sealed partial class WFRoundSetupClientSystem : EntitySystem
{
    /// <summary>The latest snapshot for this connection.</summary>
    public WFRoundSetupState? State { get; private set; }

    /// <summary>Notifies open setup windows when a snapshot arrives.</summary>
    public event Action<WFRoundSetupState>? Received;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WFRoundSetupState>(OnState);
    }

    private void OnState(WFRoundSetupState state)
    {
        State = state;
        Received?.Invoke(state);
    }

    /// <summary>Sends an operation; the server checks its caller and current lobby state.</summary>
    public void Send(WFRoundSetupRequest request)
    {
        RaiseNetworkEvent(request);
    }
}
