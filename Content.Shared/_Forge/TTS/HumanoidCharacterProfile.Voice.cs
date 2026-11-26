using Content.Shared._Forge.TTS;
using Content.Shared.Humanoid;

namespace Content.Shared.Preferences;

public sealed partial class HumanoidCharacterProfile
{
    public string Voice { get; set; } = Humanoid.SharedHumanoidAppearanceSystem.DefaultVoice;

    public string BarkVoice { get; set; } = Humanoid.SharedHumanoidAppearanceSystem.DefaultBarkVoice;

    public HumanoidCharacterProfile WithVoice(string voice)
    {
        return new(this) { Voice = voice };
    }

    public HumanoidCharacterProfile WithBarkVoice(string barkVoice)
    {
        return new(this) { BarkVoice = barkVoice };
    }

    /// <summary>Whether a voice prototype is offered for this sex. Unsexed voices fit every body.</summary>
    public static bool CanHaveVoice(TTSVoicePrototype voice, Sex sex)
    {
        return voice.Sex == Sex.Unsexed || voice.Sex == sex;
    }
}
