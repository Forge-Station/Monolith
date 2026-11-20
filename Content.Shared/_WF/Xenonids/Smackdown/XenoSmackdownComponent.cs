using Content.Shared.Damage;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;

namespace Content.Shared._WF.Xenonids.Smackdown;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class XenoSmackdownComponent : Component
{
    [DataField]
    public DamageSpecifier Damage = new();

    [DataField, AutoNetworkedField]
    public SoundSpecifier Sound = new SoundPathSpecifier("/Audio/_WF/Xenonids/Xeno/alien_tail_attack.ogg");
}
