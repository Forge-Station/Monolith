using Content.Client._WF.Voice;
using Content.Shared._WF.Voice;

namespace Content.Client.Lobby.UI;

/// <summary>The Voice tab with the voice card.</summary>
public sealed partial class HumanoidProfileEditor
{
    private SpeechVoicePicker _speechVoicePicker = default!;

    private void InitializeSpeechVoice()
    {
        _speechVoicePicker = new SpeechVoicePicker();
        _speechVoicePicker.OnVoiceChanged += SetSpeechVoice;
        _speechVoicePicker.OnTestPressed += PlaySpeechVoice;
        SpeechVoiceContainer.AddChild(_speechVoicePicker);
        TabContainer.SetTabTitle(VoiceTab.GetPositionInParent(), Loc.GetString("speech-voice-tab"));
    }

    private void UpdateSpeechVoiceControls()
    {
        _speechVoicePicker.SetVoice(Profile?.SpeechVoice ?? new SpeechVoice());
    }

    private void SetSpeechVoice(SpeechVoice voice)
    {
        if (Profile == null)
            return;

        Profile = Profile.WithSpeechVoice(voice);
        SetDirty();
    }

    private void PlaySpeechVoice(string text)
    {
        if (Profile == null)
            return;

        _entManager.System<SpeechVoiceSystem>().Preview(text, Profile.SpeechVoice);
    }
}
