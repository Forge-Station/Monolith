using Content.Shared.Speech;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Voice;

/// <summary>Sent to players in range when a voiced entity speaks, so their client can play the voice.</summary>
[Serializable, NetSerializable]
public sealed class SpeechVoiceSpokeEvent : EntityEventArgs
{
    /// <summary>The entity that spoke.</summary>
    public readonly NetEntity Source;

    /// <summary>The text as this listener reads it, scrambled when they don't know the language.</summary>
    public readonly string Message;

    /// <summary>Whether it was whispered, which plays quieter and carries less far.</summary>
    public readonly bool Whisper;

    /// <summary>A voice that replaces the speaker's own, such as the one a voice mask gives.</summary>
    public readonly SpeechVoice? Voice;

    /// <summary>The speaker's message sound, for a listener with voices off. The client doesn't know it otherwise.</summary>
    public readonly ProtoId<SpeechSoundsPrototype>? Sounds;

    public SpeechVoiceSpokeEvent(NetEntity source,
        string message,
        bool whisper,
        SpeechVoice? voice,
        ProtoId<SpeechSoundsPrototype>? sounds = null)
    {
        Source = source;
        Message = message;
        Whisper = whisper;
        Voice = voice;
        Sounds = sounds;
    }
}
