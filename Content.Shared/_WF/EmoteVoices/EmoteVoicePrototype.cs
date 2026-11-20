using Content.Shared.Chat.Prototypes;
using Content.Shared.Humanoid;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.EmoteVoices;

/// <summary>A sound a character can pick in the creator for one vocal emote, in place of their species' own.</summary>
[Prototype]
public sealed partial class EmoteVoicePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>The emote whose sound this voice replaces.</summary>
    [DataField(required: true)]
    public ProtoId<EmotePrototype> Emote;

    /// <summary>Name shown in the creator.</summary>
    [DataField(required: true)]
    public LocId Name;

    /// <summary>Played for every sex without an entry in <see cref="SexSounds"/>. With neither, the emote is silent.</summary>
    [DataField]
    public SoundSpecifier? Sound;

    /// <summary>Replaces <see cref="Sound"/> for characters of that sex.</summary>
    [DataField]
    public Dictionary<Sex, SoundSpecifier> SexSounds = new();

    /// <summary>Audio params for this voice. The species' own emote params never apply to it.</summary>
    [DataField("params")]
    public AudioParams Params = AudioParams.Default.WithVariation(0.125f);

    /// <summary>The sound this voice plays for a character of the given sex, or null when it is silent.</summary>
    public SoundSpecifier? GetSound(Sex sex)
    {
        return SexSounds.GetValueOrDefault(sex) ?? Sound;
    }
}
