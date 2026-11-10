using Robust.Shared.Configuration;

namespace Content.Shared._WF.Voice;

/// <summary>Settings for voices.</summary>
[CVarDefs]
public sealed class SpeechVoiceCVars
{
    /// <summary>Plays characters' voices. Off, they all use their message sounds. The creator's Test button plays either way.</summary>
    public static readonly CVarDef<bool> Enabled =
        CVarDef.Create("wf.voice.enabled", true, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>The highest <see cref="Volume"/>, as far as a voice goes before it clips on its own.</summary>
    public const float MaxVolume = 1.5f;

    /// <summary>Gain on every voice, the creator's Test button included.</summary>
    public static readonly CVarDef<float> Volume =
        CVarDef.Create("wf.voice.volume", 1f, CVar.CLIENTONLY | CVar.ARCHIVE);
}
