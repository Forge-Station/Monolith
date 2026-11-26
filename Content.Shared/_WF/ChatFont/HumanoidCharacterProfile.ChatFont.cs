using Content.Shared._WF.ChatFont;
using Robust.Shared.Prototypes;

namespace Content.Shared.Preferences;

public sealed partial class HumanoidCharacterProfile
{
    /// <summary>The font this character's speech is drawn in; validation resets an unknown one to the default.</summary>
    [DataField]
    public ProtoId<WFChatFontPrototype> ChatFont { get; set; } = WFChatFontRules.Default;

    /// <summary>Copy of this profile with the given chat font.</summary>
    public HumanoidCharacterProfile WithChatFont(ProtoId<WFChatFontPrototype> font)
    {
        return new(this) { ChatFont = font };
    }
}
