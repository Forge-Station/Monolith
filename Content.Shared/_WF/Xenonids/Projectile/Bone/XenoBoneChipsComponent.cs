using Content.Shared._WF.Xenonids.Projectile.Spit;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Xenonids.Projectile.Bone;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(XenoSpitSystem))]
public sealed partial class XenoBoneChipsComponent : Component
{
    [DataField, AutoNetworkedField]
    public float Speed = 20;

    [DataField, AutoNetworkedField]
    public EntProtoId ProjectileId = "XenoBoneChipsProjectile";
}
