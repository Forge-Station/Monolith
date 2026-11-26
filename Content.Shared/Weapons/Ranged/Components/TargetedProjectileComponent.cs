using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.GameStates;

namespace Content.Shared.Weapons.Ranged.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
// WOLFGATE(Xenonids): the xeno projectile system sets the target of its shots
[Access(typeof(SharedGunSystem), typeof(Content.Shared._WF.Xenonids.Projectile.XenoProjectileSystem))]
public sealed partial class TargetedProjectileComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid Target;
}
