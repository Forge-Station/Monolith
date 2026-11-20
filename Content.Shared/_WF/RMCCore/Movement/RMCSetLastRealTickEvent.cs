using Robust.Shared.Serialization;
using Robust.Shared.Timing;

namespace Content.Shared._WF.RMCCore.Movement;

[Serializable, NetSerializable]
public sealed class RMCSetLastRealTickEvent(GameTick tick) : EntityEventArgs
{
    public readonly GameTick Tick = tick;
}
