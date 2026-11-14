using Robust.Shared.Audio;
using Robust.Shared.GameStates;

namespace Content.Shared._WF.RMCCore.Sound;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(CMSoundSystem))]
public sealed partial class SoundOnDeathComponent : Component
{
    [DataField(required: true), AutoNetworkedField]
    public SoundSpecifier? Sound;

    [DataField, AutoNetworkedField]
    public EntityUid? Entity;
}
