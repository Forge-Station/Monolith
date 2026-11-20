using Robust.Shared.GameStates;

namespace Content.Shared._WF.Voice;

/// <summary>Speaks the entity's chat aloud with this voice, set from their profile.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class SpeechVoiceComponent : Component
{
    /// <summary>The voice this entity speaks with.</summary>
    [DataField, AutoNetworkedField]
    public SpeechVoice Voice = new();
}
