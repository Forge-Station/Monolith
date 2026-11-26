using Content.Shared.Physics;
using Robust.Shared.GameStates;
using Robust.Shared.Physics.Collision.Shapes;

namespace Content.Shared._WF.RMCCore.Movement;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(RMCMovementSystem))]
public sealed partial class RMCMobCollisionComponent : Component
{
    [DataField, AutoNetworkedField]
    public string FixtureId = "rmc_mob_collision";

    [DataField, AutoNetworkedField]
    public IPhysShape FixtureShape = new PhysShapeCircle(0.4f);

    [DataField, AutoNetworkedField]
    public CollisionGroup FixtureLayer = MobCollisionLayer;

    // WOLFGATE: RMC adds CollisionGroup.MobCollision (1 << 24) upstream; the same free bit is used here without touching CollisionGroup.
    public const CollisionGroup MobCollisionLayer = (CollisionGroup) (1 << 24);
}
