using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Xenonids.TailSeize;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class XenoTailSeizeComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntProtoId Projectile = "XenoOppressorTailHook";

    [DataField, AutoNetworkedField]
    public float Speed = 30;

    [DataField, AutoNetworkedField]
    public SoundSpecifier Sound = new SoundPathSpecifier("/Audio/_WF/Xenonids/Xeno/oppressor_tail.ogg");
}
