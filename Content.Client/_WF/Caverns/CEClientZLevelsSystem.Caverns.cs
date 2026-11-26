using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WF.Caverns;

namespace Content.Client._CE.ZLevels.Core;

public sealed partial class CEClientZLevelsSystem
{
    /// <inheritdoc/>
    // A client can't tell a hole from ground that isn't loaded: both are empty tiles. Stairs only ever stand under a
    // hole, so a body they have carried to the top is predicted through; anything in the air waits for the server.
    protected override bool WfSealedAbove(Entity<CEZPhysicsComponent> body)
    {
        return body.Comp.CachedGroundHeight < 1f && HasComp<WFCavernLayerComponent>(Transform(body).MapUid);
    }
}
