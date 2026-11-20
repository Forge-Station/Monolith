using Content.Shared.FixedPoint;
using Robust.Shared.GameStates;
using static Content.Shared._WF.RMCCore.Shields.XenoShieldSystem;

namespace Content.Shared._WF.Xenonids.Projectile.Spit.Shield;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(XenoSpitSystem))]
public sealed partial class XenoProjectileShieldOnHitComponent : Component
{
    [DataField, AutoNetworkedField]
    public ShieldType Shield = ShieldType.Praetorian;

    [DataField, AutoNetworkedField]
    public FixedPoint2 Amount = 15;

    [DataField, AutoNetworkedField]
    public FixedPoint2 Max = 45;
}
