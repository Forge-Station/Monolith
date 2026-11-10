using System.Threading.Tasks;
using Content.Shared._WF.Voice;
using Content.Shared.Speech;
using Robust.Client.Audio;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Client._WF.Voice;

/// <summary>Plays voices at the speaker or over the radio, and the creator's Test button locally.</summary>
public sealed partial class SpeechVoiceSystem : EntitySystem
{
    [Dependency] private IAudioManager _audioManager = default!;
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private IRobustRandom _random = default!;

    private static readonly TimeSpan DisposeMargin = TimeSpan.FromSeconds(0.5);

    /// <summary>How much quieter a voice is over the radio than in person, in dB.</summary>
    private const float RadioVolume = -4f;

    /// <summary>How much quieter a whisper is, in dB.</summary>
    private const float WhisperVolume = -6f;

    /// <summary>Voices rendering or playing at once. Past this, new lines are skipped.</summary>
    private const int MaxPlaying = 8;

    /// <summary>How many of those can be radio, so a busy channel leaves room for people nearby.</summary>
    private const int MaxRadio = 4;

    /// <summary>How many of those can come from one source, so one person can't take them all.</summary>
    private const int MaxPerSource = 2;

    /// <summary>Lines rendering on a worker thread, so a long line never stalls a frame.</summary>
    private readonly List<(Task<int> Render, SpeechVoiceBuffers Buffers, EntityUid? Source, bool Whisper, bool Radio)>
        _rendering = new();

    /// <summary>Streams still playing, disposed once they end so a long round doesn't pile up buffers.</summary>
    private readonly List<(EntityUid? Audio, AudioStream Stream, TimeSpan End, EntityUid? Source, bool Radio)>
        _playing = new();

    /// <summary>Buffers from finished renders. Only touched on the main thread.</summary>
    private readonly Stack<SpeechVoiceBuffers> _freeBuffers = new();

    /// <summary>When each speaker's chatter last played, for the same cooldown upstream uses.</summary>
    private readonly Dictionary<EntityUid, TimeSpan> _lastChatter = new();

    private bool _enabled;
    private float _volume = 1f;

    /// <summary>The message sound last played in place of a voice.</summary>
    public ProtoId<SpeechSoundsPrototype>? LastMessageSound { get; private set; }

    /// <summary>Lines rendering or playing right now.</summary>
    public int ActiveLines => _rendering.Count + _playing.Count;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<SpeechVoiceSpokeEvent>(OnSpoke);
        SubscribeNetworkEvent<SpeechVoiceRadioEvent>(OnRadio);
        SubscribeLocalEvent<SpeechVoiceComponent, ComponentShutdown>(OnComponentShutdown);

        Subs.CVar(_cfg, SpeechVoiceCVars.Enabled, value => _enabled = value, true);
        Subs.CVar(_cfg, SpeechVoiceCVars.Volume, value => _volume = Math.Clamp(value, 0f, SpeechVoiceCVars.MaxVolume), true);
    }

    public override void Shutdown()
    {
        base.Shutdown();

        foreach (var (audio, stream, _, _, _) in _playing)
        {
            Release(audio, stream);
        }

        _playing.Clear();
        _rendering.Clear();
        _freeBuffers.Clear();
        _lastChatter.Clear();
    }

    public override void FrameUpdate(float frameTime)
    {
        for (var i = _rendering.Count - 1; i >= 0; i--)
        {
            var (render, buffers, source, whisper, radio) = _rendering[i];
            if (!render.IsCompleted)
                continue;

            _rendering.RemoveAt(i);
            if (render.IsCompletedSuccessfully && (source == null || Exists(source)))
            {
                // The task has finished, so Result returns at once.
#pragma warning disable RA0004
                Play(buffers.Pcm.AsSpan(0, render.Result), source, whisper, radio);
#pragma warning restore RA0004
            }

            _freeBuffers.Push(buffers);
        }

        for (var i = _playing.Count - 1; i >= 0; i--)
        {
            var (audio, stream, end, _, _) = _playing[i];
            if (_timing.RealTime < end)
                continue;

            Release(audio, stream);
            _playing.RemoveAt(i);
        }
    }

    /// <summary>Plays a voice to the local player only, whether or not voices are switched off.</summary>
    public void Preview(string text, SpeechVoice voice)
    {
        Queue(text, voice.Clamped(), null, false, false);
    }

    private void OnComponentShutdown(Entity<SpeechVoiceComponent> ent, ref ComponentShutdown args)
    {
        _lastChatter.Remove(ent);
    }

    private void OnSpoke(SpeechVoiceSpokeEvent ev)
    {
        var source = GetEntity(ev.Source);
        if (!TryComp<SpeechVoiceComponent>(source, out var comp))
            return;

        if (!_enabled)
        {
            PlayMessageSound(source, ev.Message, ev.Sounds);
            return;
        }

        var voice = ev.Voice ?? comp.Voice.Clone();
        Queue(ev.Message, voice, source, ev.Whisper, false);
    }

    /// <summary>The speaker's chatter sound in place of the voice, picked as upstream does.</summary>
    private void PlayMessageSound(EntityUid source, string message, ProtoId<SpeechSoundsPrototype>? id)
    {
        if (message.Length == 0
            || !TryComp<SpeechComponent>(source, out var speech)
            || !_prototype.TryIndex(id, out var sounds))
        {
            return;
        }

        var now = _timing.CurTime;
        var cooldown = TimeSpan.FromSeconds(speech.SoundCooldownTime);
        if (_lastChatter.TryGetValue(source, out var last) && now - last < cooldown)
            return;

        _lastChatter[source] = now;
        LastMessageSound = id;

        var upper = 0;
        foreach (var c in message)
        {
            if (char.IsUpper(c))
                upper++;
        }

        var sound = upper > message.Length / 2
            ? sounds.ExclaimSound
            : message[^1] switch
            {
                '?' => sounds.AskSound,
                '!' => sounds.ExclaimSound,
                _ => sounds.SaySound,
            };

        _audio.PlayEntity(sound, Filter.Local(), source, false,
            speech.AudioParams.WithPitchScale((float) _random.NextGaussian(1, sounds.Variation)));
    }

    private void OnRadio(SpeechVoiceRadioEvent ev)
    {
        if (!_enabled)
            return;

        EntityUid? speaker = null;
        if (ev.Speaker.IsValid())
        {
            speaker = GetEntity(ev.Speaker);
            if (!Exists(speaker))
                return;
        }

        Queue(ev.Message, ev.Voice, speaker, false, true);
    }

    /// <summary>Deletes the audio entity, which frees its OpenAL source, before freeing the buffer it plays.</summary>
    private void Release(EntityUid? audio, AudioStream stream)
    {
        if (audio is { } entity && !TerminatingOrDeleted(entity))
        {
            if (TryComp(entity, out AudioComponent? component))
                component.StopPlaying();

            Del(entity);
        }

        stream.Dispose();
    }

    private void Queue(string text, SpeechVoice voice, EntityUid? source, bool whisper, bool radio)
    {
        // Turned all the way down, so don't spend the render.
        if (_volume <= 0f || !CanQueue(source, radio))
            return;

        var buffers = _freeBuffers.Count > 0 ? _freeBuffers.Pop() : new SpeechVoiceBuffers();

        // Caught inside the task, since a faulted task's exception type is not allowed by the sandbox.
        var render = Task.Run(() =>
        {
            try
            {
                return new SpeechVoiceSynth().Render(text, voice, radio, buffers);
            }
            catch (Exception e)
            {
                Log.Error($"Voice render failed: {e}");
                return 0;
            }
        });
        _rendering.Add((render, buffers, source, whisper, radio));
    }

    /// <summary>Whether a new line fits the limits. The creator's Test button only has the per-source one.</summary>
    private bool CanQueue(EntityUid? source, bool radio)
    {
        var radioLines = 0;
        var sameSource = 0;

        foreach (var line in _rendering)
        {
            if (line.Radio)
                radioLines++;
            if (line.Source == source && line.Radio == radio)
                sameSource++;
        }

        foreach (var line in _playing)
        {
            if (line.Radio)
                radioLines++;
            if (line.Source == source && line.Radio == radio)
                sameSource++;
        }

        var preview = source == null && !radio;
        if (!preview && ActiveLines >= MaxPlaying)
            return false;

        if (radio && radioLines >= MaxRadio)
            return false;

        // A line in the ear doesn't say who is talking, so only the radio limit applies to it.
        return source == null && radio || sameSource < MaxPerSource;
    }

    private void Play(ReadOnlySpan<short> samples, EntityUid? source, bool whisper, bool radio)
    {
        if (samples.Length == 0)
            return;

        var stream = _audioManager.LoadAudioRaw(samples, 1, SpeechVoiceSynth.SampleRate);
        var audioParams = AudioParams.Default
            .WithMaxDistance(whisper ? SpeechVoice.WhisperRange : SpeechVoice.VoiceRange)
            .WithVolume((radio ? RadioVolume : whisper ? WhisperVolume : 0f) + SharedAudioSystem.GainToVolume(_volume));

        var playing = source is { } entity
            ? _audio.PlayEntity(stream, entity, null, audioParams)
            : _audio.PlayGlobal(stream, null, audioParams);

        _playing.Add((playing?.Entity, stream, _timing.RealTime + stream.Length + DisposeMargin, source, radio));
    }
}
