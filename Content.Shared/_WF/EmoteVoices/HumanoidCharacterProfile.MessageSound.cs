using Content.Shared._WF.EmoteVoices;
using Robust.Shared.Prototypes;

namespace Content.Shared.Preferences;

/// <summary>The message sound chosen in the creator.</summary>
public sealed partial class HumanoidCharacterProfile
{
    /// <summary>Sound played when the character speaks; null keeps the species' own.</summary>
    [DataField]
    public ProtoId<MessageSoundPrototype>? MessageSound { get; set; }

    /// <summary>A copy with the given message sound; null keeps the species' own.</summary>
    public HumanoidCharacterProfile WithMessageSound(ProtoId<MessageSoundPrototype>? sound)
    {
        return new(this) { MessageSound = sound };
    }
}
