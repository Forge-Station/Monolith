using Content.Shared._CE.ZLevels.Core.Components;

namespace Content.Shared._CE.ZLevels.Core.EntitySystems;

public abstract partial class CESharedZLevelsSystem
{
    /// <summary>Reads the ground under a body again and wakes it, for ground that changed with no tile changing.</summary>
    public void WfRecacheGround(Entity<CEZPhysicsComponent> body)
    {
        RequestCacheMovement(body);
        WakeBody((body.Owner, body.Comp));
    }

    /// <summary>
    /// Whether an empty tile of a grid is planet terrain that only isn't loaded, so a body over it stands on ground
    /// instead of falling. Only the server knows unloaded ground from a hole; a client's own body is always on loaded
    /// ground, which is all it predicts.
    /// </summary>
    protected virtual bool WfUnloadedGround(EntityUid grid, Vector2i tile)
    {
        return false;
    }
}
