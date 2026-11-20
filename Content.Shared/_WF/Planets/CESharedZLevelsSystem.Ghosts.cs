using Content.Shared._CE.ZLevels.Core.Components;

namespace Content.Shared._CE.ZLevels.Core.EntitySystems;

public abstract partial class CESharedZLevelsSystem
{
    /// <summary>
    /// A body with neither gravity nor stepping, a ghost, has no ground: it never sticks to stairs, and keeps no height
    /// between levels while it isn't moving up or down.
    /// </summary>
    // Sticking set a ghost over stairs to their height, which it kept when it left them: stuck between levels.
    private static void WfGroundless(CEZPhysicsComponent comp, ref bool stickyGround)
    {
        if (comp.VelocityGravity || comp.AutoStep)
            return;

        stickyGround = false;
        if (comp.Velocity == 0f)
            comp.LocalPosition = 0f;
    }
}
