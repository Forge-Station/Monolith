using Robust.Shared.GameStates;

namespace Content.Shared._WF.RMCCore.Sound;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(CMSoundSystem))]
public sealed partial class SoundOnDeathSoundComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid? Parent;
}
