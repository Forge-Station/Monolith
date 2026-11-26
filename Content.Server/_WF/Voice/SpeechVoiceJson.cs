using System.Text.Json;
using Content.Shared._WF.Voice;

namespace Content.Server._WF.Voice;

/// <summary>DB mapping for SpeechVoice, stored as JSON in profile.speech_voice. Reading never throws.</summary>
public static class SpeechVoiceJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        IncludeFields = true,
    };

    /// <summary>The stored voice, or the default one when the column is empty or unreadable.</summary>
    public static SpeechVoice FromStored(string? stored)
    {
        if (string.IsNullOrEmpty(stored))
            return new SpeechVoice();

        try
        {
            return (JsonSerializer.Deserialize<SpeechVoice>(stored, Options) ?? new SpeechVoice()).Clamped();
        }
        catch (JsonException)
        {
            return new SpeechVoice();
        }
    }

    /// <summary>The database form of a voice. Fields added later read back as their defaults.</summary>
    public static string ToStored(SpeechVoice voice)
    {
        return JsonSerializer.Serialize(voice, Options);
    }
}
