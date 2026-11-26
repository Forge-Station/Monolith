using Content.Shared.Speech;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.EmoteVoices;

/// <summary>A message sound a character can pick in the creator, in place of their species' own.</summary>
[Prototype("wfMessageSound")]
public sealed partial class MessageSoundPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>Name shown in the creator.</summary>
    [DataField(required: true)]
    public LocId Name;

    /// <summary>The sounds played when the character speaks; null is silent.</summary>
    [DataField]
    public ProtoId<SpeechSoundsPrototype>? Sounds;
}
