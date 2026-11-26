using Content.Shared._WF.Voice;

namespace Content.Shared.Preferences;

/// <summary>The profile's voice settings.</summary>
public sealed partial class HumanoidCharacterProfile
{
    /// <summary>The voice chosen in the creator.</summary>
    [DataField]
    public SpeechVoice SpeechVoice { get; set; } = new();

    /// <summary>A copy with the given voice.</summary>
    public HumanoidCharacterProfile WithSpeechVoice(SpeechVoice voice)
    {
        return new(this) { SpeechVoice = voice };
    }
}
