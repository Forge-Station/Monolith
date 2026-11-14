using Robust.Shared.Serialization;

namespace Content.Shared._WF.Voice;

/// <summary>Sent to players who hear a voiced line over the radio.</summary>
[Serializable, NetSerializable]
public sealed class SpeechVoiceRadioEvent : EntityEventArgs
{
    /// <summary>The radio speaker it plays from, or invalid when it plays in the listener's ear.</summary>
    public readonly NetEntity Speaker;

    /// <summary>The text as this listener reads it, scrambled when they don't know the language.</summary>
    public readonly string Message;

    /// <summary>The talker's voice, sent along because a client only has the voices of characters it can see.</summary>
    public readonly SpeechVoice Voice;

    public SpeechVoiceRadioEvent(NetEntity speaker, string message, SpeechVoice voice)
    {
        Speaker = speaker;
        Message = message;
        Voice = voice;
    }
}
