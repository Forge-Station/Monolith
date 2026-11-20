using Robust.Shared.Audio;
using Robust.Shared.GameStates;

namespace Content.Shared._WF.RMCCore.Tackle;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(TackleSystem))]
public sealed partial class TackleableComponent : Component
{
    [DataField, AutoNetworkedField]
    public SoundSpecifier? KnockdownSound = new SoundPathSpecifier("/Audio/_WF/RMCCore/Weapons/alien_knockdown.ogg")
    {
        Params = AudioParams.Default.WithVolume(-3f),
    };
}
