using Content.Shared._Forge.JoinQueue;
using Robust.Client.State;
using Robust.Shared.Network;

namespace Content.Client._Forge.JoinQueue;

public sealed partial class JoinQueueManager
{
    [Dependency] private IClientNetManager _net = default!;
    [Dependency] private IStateManager _state = default!;


    public void Initialize()
    {
        _net.RegisterNetMessage<QueueUpdateMessage>(OnQueueUpdate);
    }


    private void OnQueueUpdate(QueueUpdateMessage msg)
    {
        if (_state.CurrentState is not QueueState)
            _state.RequestStateChange<QueueState>();

        ((QueueState) _state.CurrentState).OnQueueUpdate(msg);
    }
}
