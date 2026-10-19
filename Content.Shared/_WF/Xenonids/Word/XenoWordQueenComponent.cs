using Content.Shared.FixedPoint;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;

namespace Content.Shared._WF.Xenonids.Word;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(XenoWordQueenSystem))]
public sealed partial class XenoWordQueenComponent : Component
{
    [DataField, AutoNetworkedField]
    public FixedPoint2 PlasmaCost = 50;

    [DataField, AutoNetworkedField]
    public SoundSpecifier Sound = new SoundCollectionSpecifier("XenoQueenCommand", AudioParams.Default.WithVolume(-6));
}
