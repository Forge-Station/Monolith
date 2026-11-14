using Robust.Shared.Timing;

namespace Content.Shared._WF.Xenonids.Projectile;

[RegisterComponent]
public sealed partial class XenoClientProjectileShotComponent : Component
{
    public GameTick LatestPredictedTick;
}
