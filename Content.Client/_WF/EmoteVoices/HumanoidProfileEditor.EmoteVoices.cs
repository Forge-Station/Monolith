using Content.Client._WF.EmoteVoices;
using Content.Shared._WF.EmoteVoices;
using Content.Shared.Chat.Prototypes;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Client.Lobby.UI;

/// <summary>The Voice card: the message sound, scream and laugh a character uses in place of their species' own.</summary>
public sealed partial class HumanoidProfileEditor
{
    private EmoteVoicePicker _emoteVoicePicker = default!;

    /// <summary>The preview sound still playing, stopped when the next one starts.</summary>
    private EntityUid? _emoteVoicePreview;

    private void InitializeEmoteVoices()
    {
        _emoteVoicePicker = new EmoteVoicePicker();
        _emoteVoicePicker.OnVoiceSelected += SetEmoteVoice;
        _emoteVoicePicker.OnPlayPressed += PlayEmoteVoice;
        _emoteVoicePicker.OnMessageSoundSelected += SetMessageSound;
        _emoteVoicePicker.OnMessageSoundPlayPressed += PlayMessageSound;
        EmoteVoiceContainer.AddChild(_emoteVoicePicker);
    }

    /// <summary>Shows the loaded profile's voices; a preview of the previous profile stops.</summary>
    private void UpdateEmoteVoiceControls()
    {
        StopEmoteVoicePreview();
        _emoteVoicePicker.SetVoice(EmoteVoiceRules.Scream, Profile?.ScreamVoice);
        _emoteVoicePicker.SetVoice(EmoteVoiceRules.Laugh, Profile?.LaughVoice);
        _emoteVoicePicker.SetMessageSound(Profile?.MessageSound);
    }

    private void SetMessageSound(ProtoId<MessageSoundPrototype>? sound)
    {
        if (Profile == null)
            return;

        Profile = Profile.WithMessageSound(sound);
        SetDirty();
        PlayMessageSound();
    }

    /// <summary>Plays what the edited character sounds like when they speak: their pick, or their species' own.</summary>
    private void PlayMessageSound()
    {
        if (Profile == null)
            return;

        StopEmoteVoicePreview();

        var sounds = MessageSoundRules.Resolve(Profile.Species, Profile.MessageSound, _prototypeManager, _entManager.ComponentFactory);
        if (sounds == null || !_prototypeManager.TryIndex(sounds, out var proto))
            return;

        _emoteVoicePreview = _entManager.System<SharedAudioSystem>()
            .PlayGlobal(proto.SaySound, Filter.Local(), false, AudioParams.Default.WithVolume(-2f))?.Entity;
    }

    private void SetEmoteVoice(ProtoId<EmotePrototype> emote, ProtoId<EmoteVoicePrototype>? voice)
    {
        if (Profile == null)
            return;

        if (emote == EmoteVoiceRules.Scream)
            Profile = Profile.WithScreamVoice(voice);
        else if (emote == EmoteVoiceRules.Laugh)
            Profile = Profile.WithLaughVoice(voice);
        else
            return;

        SetDirty();
        PlayEmoteVoice(emote);
    }

    /// <summary>Plays what the emote sounds like for the edited character: their voice, or their species' own.</summary>
    private void PlayEmoteVoice(ProtoId<EmotePrototype> emote)
    {
        if (Profile == null)
            return;

        var voice = emote == EmoteVoiceRules.Scream ? Profile.ScreamVoice
            : emote == EmoteVoiceRules.Laugh ? Profile.LaughVoice
            : null;

        StopEmoteVoicePreview();

        if (!EmoteVoiceRules.TryGetEmoteSound(Profile.Species, Profile.Sex, emote, voice,
                _prototypeManager, _entManager.ComponentFactory, out var sound, out var audioParams))
            return;

        _emoteVoicePreview = _entManager.System<SharedAudioSystem>()
            .PlayGlobal(sound, Filter.Local(), false, audioParams)?.Entity;
    }

    private void StopEmoteVoicePreview()
    {
        // The editor can leave the tree while the client shuts down, after its systems are gone.
        if (_emoteVoicePreview == null || !_entManager.TrySystem<SharedAudioSystem>(out var audio))
            return;

        _emoteVoicePreview = audio.Stop(_emoteVoicePreview);
    }
}
