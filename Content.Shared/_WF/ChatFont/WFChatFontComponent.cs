using Robust.Shared.GameStates;

namespace Content.Shared._WF.ChatFont;

/// <summary>The font a character's speech is drawn in, set from their profile. Absent means the speech verb's own.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WFChatFontComponent : Component
{
    /// <summary>The client <c>font</c> prototype id.</summary>
    [DataField, AutoNetworkedField]
    public string Font = "Default";
}
