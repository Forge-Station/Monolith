using Robust.Shared.Serialization;
using Robust.Shared.Timing;

namespace Content.Shared._WF.Xenonids.Lunge;

[Serializable, NetSerializable]
public sealed class XenoLungePredictedHitEvent(NetEntity target, GameTick lastRealTick) : EntityEventArgs
{
    public readonly NetEntity Target = target;
    public readonly GameTick LastRealTick = lastRealTick;
}
