using Content.Shared._WF.RMCCore.Hands;
using Content.Server.Hands.Systems;
using Robust.Shared.Map;

namespace Content.Server._WF.RMCCore.Hands;

public sealed partial class ServerRMCHandsSystem : RMCHandsSystem
{
    [Dependency] private HandsSystem _hands = default!;

    public override void ThrowHeldItem(EntityUid player, EntityCoordinates coordinates, float minDistance = 0.1f)
    {
        _hands.ThrowHeldItem(player, coordinates, minDistance);
    }
}
