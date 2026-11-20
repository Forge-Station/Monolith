using Content.Shared.Chat.TypingIndicator;
using Robust.Shared.Prototypes;

namespace Content.Shared.Preferences;

public sealed partial class HumanoidCharacterProfile
{
    /// <summary>The typing bubble shown over this character, null for the species' own.</summary>
    [DataField]
    public ProtoId<TypingIndicatorPrototype>? TypingIndicator { get; set; }

    /// <summary>Copy of this profile with the given typing bubble, null for the species' own.</summary>
    public HumanoidCharacterProfile WithTypingIndicator(ProtoId<TypingIndicatorPrototype>? indicator)
    {
        return new(this) { TypingIndicator = indicator };
    }
}
