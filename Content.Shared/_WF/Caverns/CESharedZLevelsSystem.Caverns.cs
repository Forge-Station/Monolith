using Content.Shared._CE.ZLevels.Core.Components;

namespace Content.Shared._CE.ZLevels.Core.EntitySystems;

public abstract partial class CESharedZLevelsSystem
{
    /// <summary>Whether a body that just moved down a level came onto ground right under it, such as stairs, and so did not fall.</summary>
    // Read after the move, when the ground height is the new level's.
    private static bool WfSteppedDown(CEZPhysicsComponent body)
    {
        return body.LocalPosition - body.CachedGroundHeight <= AirborneHeightThreshold;
    }

    /// <summary>
    /// Whether a body in a cavern has solid ground over it that has no tiles to find: terrain that isn't loaded is
    /// empty, yet it is no hole. The server tells the two apart; a client can't, and answers for what it can predict.
    /// </summary>
    protected virtual bool WfSealedAbove(Entity<CEZPhysicsComponent> body)
    {
        return false;
    }
}
