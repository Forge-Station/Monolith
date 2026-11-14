using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._CE.ZLevels.Core.EntitySystems;
using Content.Shared.Throwing;

namespace Content.Shared._CE.ZLevels.Throwing;

public sealed partial class CEZLevelThrowingSystem : EntitySystem
{
    [Dependency] private CESharedZLevelsSystem _zLevels = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<CEZPhysicsComponent, ThrownEvent>(OnThrown);
    }

    private void OnThrown(Entity<CEZPhysicsComponent> ent, ref ThrownEvent args)
    {
        // WOLFGATE(Planets) START: a throw only has an arc where there are levels to arc through.
        // Off a z-network nothing ever integrates this velocity, so it sat on the body until its ship next reached a
        // planet and launched it then.
        var map = Transform(ent).MapUid;
        if (!HasComp<CEZMapComponent>(map) && !HasComp<CEZTransitMapComponent>(map))
            return;
        // WOLFGATE END

        if (!TryComp<ThrownItemComponent>(ent, out var thrown)
            || thrown.LandTime is not { } landTime
            || thrown.ThrownTime is not { } thrownTime)
            return;

        var flyTime = (float)(landTime - thrownTime).TotalSeconds;
        if (flyTime <= 0f)
            return;

        var distToGround = ent.Comp.LocalPosition - ent.Comp.CachedGroundHeight;
        var v0 = MathF.Max(0f, (0.5f * CESharedZLevelsSystem.ZGravityForce * flyTime - distToGround / flyTime) * 2f);
        _zLevels.SetZVelocity((ent.Owner, ent.Comp), v0);
    }
}
