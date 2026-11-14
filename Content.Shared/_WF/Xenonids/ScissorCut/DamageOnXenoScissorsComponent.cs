using Content.Shared.Damage;
using Robust.Shared.GameStates;

namespace Content.Shared._WF.Xenonids.ScissorCut;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class DamageOnXenoScissorsComponent : Component
{
    [DataField, AutoNetworkedField]
    public DamageSpecifier Damage = new();
}
