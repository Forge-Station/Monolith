using System.Numerics;
using Robust.Shared.Network;

namespace Content.Shared._WF.Xenonids.Animation;

public sealed partial class XenoAnimationsSystem : EntitySystem
{
    [Dependency] private INetManager _net = default!;

    public void PlayLungeAnimationEvent(EntityUid entityUid, Vector2 direction)
    {
        var ev = new PlayLungeAnimationEvent(GetNetEntity(entityUid), direction.Normalized(), _net.IsClient);
        if (_net.IsServer)
            RaiseNetworkEvent(ev);
        else
            RaiseLocalEvent(ev);
    }
}
