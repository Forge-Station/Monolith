// Author: @lenta313. Все права не защищены / No rights reserved.
using System.Collections.Generic;
using Content.Server.Destructible;
using Content.Server.Destructible.Thresholds.Behaviors;
using Content.Shared.Body.Part;
using JetBrains.Annotations;

namespace Content.Server._Forge.EmergencyLimb;

/// <summary>
/// Destructible behavior for emergency limbs: when the limb breaks, the WHOLE limb chain
/// (arm + hand, or leg + foot) is deleted at once — no stray stump parts left dropping on the floor.
/// Each removal raises BodyPartRemovedEvent, which resets the body's custom texture layer.
/// </summary>
[UsedImplicitly]
[DataDefinition]
public sealed partial class EmergencyLimbCrumbleBehavior : IThresholdBehavior
{
    public void Execute(EntityUid owner, DestructibleSystem system, EntityUid? cause = null)
    {
        var entMan = system.EntityManager;

        if (!entMan.TryGetComponent(owner, out BodyPartComponent? part))
        {
            entMan.QueueDeleteEntity(owner);
            return;
        }

        // Whole limb chain: this part + its hand/foot (and anything below).
        var chain = new List<EntityUid>();
        foreach (var (childId, _) in system.BodySystem.GetBodyPartChildren(owner, part))
            chain.Add(childId);

        foreach (var id in chain)
            entMan.QueueDeleteEntity(id);
    }
}
