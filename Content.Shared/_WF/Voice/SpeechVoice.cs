using Robust.Shared.Serialization;

namespace Content.Shared._WF.Voice;

/// <summary>The effect laid over a voice.</summary>
[Serializable, NetSerializable]
public enum SpeechVoiceFilter : byte
{
    None,
    Robot,
    Alien,
    Insect,
    Ghost,
    Dog,
    Cat,
    Bird,
    Lizard,
    Mouse,
    Slime,
    Plant,
}

/// <summary>A character's voice, picked in the creator and played when they talk.</summary>
[DataDefinition, Serializable, NetSerializable]
public sealed partial class SpeechVoice : IEquatable<SpeechVoice>
{
    public const float MinPitch = 0.5f;
    public const float MaxPitch = 2f;
    public const float MaxPitchVariance = 0.4f;
    public const float MinSpeed = 0.04f;
    public const float MaxSpeed = 0.14f;
    public const float MaxIntonation = 0.5f;
    public const float MaxVibratoDepth = 0.3f;
    public const float MinVibratoRate = 1f;
    public const float MaxVibratoRate = 12f;
    public const float MaxTremolo = 1f;

    private const float DefaultPitch = 1f;
    private const float DefaultPitchVariance = 0.1f;
    private const float DefaultSpeed = 0.075f;
    private const float DefaultIntonation = 0.2f;
    private const float DefaultVibratoRate = 6f;

    /// <summary>How far a spoken voice carries. Matches ChatSystem.VoiceRange.</summary>
    public const float VoiceRange = 10f;

    /// <summary>How far a whispered voice carries. Matches ChatSystem.WhisperMuffledRange.</summary>
    public const float WhisperRange = 5f;

    /// <summary>How far a whisper stays clear, then it is muffled. Matches ChatSystem.WhisperClearRange.</summary>
    public const float WhisperClearRange = 2f;

    /// <summary>Whether the character speaks with this voice. Off, they use the message sound instead.</summary>
    [DataField]
    public bool Enabled = true;

    /// <summary>Voice pitch as a multiple of the base pitch. Formants rise with it, more slowly.</summary>
    [DataField]
    public float Pitch = DefaultPitch;

    /// <summary>Random pitch change per word, as a fraction of the pitch.</summary>
    [DataField]
    public float PitchVariance = DefaultPitchVariance;

    /// <summary>Seconds per syllable, so lower is faster.</summary>
    [DataField]
    public float Speed = DefaultSpeed;

    /// <summary>How far the pitch rises at the end of a question and falls at the end of a statement.</summary>
    [DataField]
    public float Intonation = DefaultIntonation;

    /// <summary>Vibrato depth as a fraction of the pitch.</summary>
    [DataField]
    public float VibratoDepth;

    /// <summary>Vibrato cycles per second.</summary>
    [DataField]
    public float VibratoRate = DefaultVibratoRate;

    /// <summary>How deep the loudness wobbles, 0 to 1.</summary>
    [DataField]
    public float Tremolo;

    /// <summary>The species or effect filter.</summary>
    [DataField]
    public SpeechVoiceFilter Filter = SpeechVoiceFilter.None;

    /// <summary>How strongly the filter shows, 0 to 1.</summary>
    [DataField]
    public float FilterStrength = 1f;

    /// <summary>A copy of this voice.</summary>
    public SpeechVoice Clone()
    {
        return (SpeechVoice) MemberwiseClone();
    }

    /// <summary>An FNV-1a hash of the text, the same on every machine and every run.</summary>
    public static uint Hash(string text)
    {
        var hash = 2166136261u;
        foreach (var c in text)
        {
            hash = (hash ^ c) * 16777619u;
        }

        return hash;
    }

    /// <summary>A plain voice made from a name, the same every time for the same name.</summary>
    public static SpeechVoice FromName(string name)
    {
        var hash = Hash(name);

        float Pick(float min, float max, int shift)
        {
            return min + (max - min) * ((hash >> shift) & 0xFF) / 255f;
        }

        return new SpeechVoice
        {
            Pitch = Pick(0.7f, 1.5f, 0),
            PitchVariance = Pick(0.05f, 0.2f, 8),
            Speed = Pick(0.06f, 0.1f, 16),
            Intonation = Pick(0.1f, 0.3f, 24),
        };
    }

    /// <summary>A copy with every value inside its slider's range and an unknown filter turned off.</summary>
    public SpeechVoice Clamped()
    {
        var voice = Clone();
        voice.Pitch = Clamp(Pitch, MinPitch, MaxPitch, DefaultPitch);
        voice.PitchVariance = Clamp(PitchVariance, 0f, MaxPitchVariance, DefaultPitchVariance);
        voice.Speed = Clamp(Speed, MinSpeed, MaxSpeed, DefaultSpeed);
        voice.Intonation = Clamp(Intonation, 0f, MaxIntonation, DefaultIntonation);
        voice.VibratoDepth = Clamp(VibratoDepth, 0f, MaxVibratoDepth, 0f);
        voice.VibratoRate = Clamp(VibratoRate, MinVibratoRate, MaxVibratoRate, DefaultVibratoRate);
        voice.Tremolo = Clamp(Tremolo, 0f, MaxTremolo, 0f);
        voice.FilterStrength = Clamp(FilterStrength, 0f, 1f, 1f);

        if (!Enum.IsDefined(Filter))
            voice.Filter = SpeechVoiceFilter.None;

        return voice;
    }

    /// <summary>Clamps a value, or gives the fallback for NaN and infinity, which a modified client can send.</summary>
    private static float Clamp(float value, float min, float max, float fallback)
    {
        return float.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
    }

    /// <summary>Whether every setting matches, with a small tolerance on the floats.</summary>
    public bool Equals(SpeechVoice? other)
    {
        if (other is null)
            return false;

        return Enabled == other.Enabled
               && Filter == other.Filter
               && MathHelper.CloseTo(Pitch, other.Pitch)
               && MathHelper.CloseTo(PitchVariance, other.PitchVariance)
               && MathHelper.CloseTo(Speed, other.Speed)
               && MathHelper.CloseTo(Intonation, other.Intonation)
               && MathHelper.CloseTo(VibratoDepth, other.VibratoDepth)
               && MathHelper.CloseTo(VibratoRate, other.VibratoRate)
               && MathHelper.CloseTo(Tremolo, other.Tremolo)
               && MathHelper.CloseTo(FilterStrength, other.FilterStrength);
    }

    public override bool Equals(object? obj)
    {
        return obj is SpeechVoice other && Equals(other);
    }

    /// <summary>Only the filter, since the floats compare with a tolerance.</summary>
    public override int GetHashCode()
    {
        return Filter.GetHashCode();
    }
}
