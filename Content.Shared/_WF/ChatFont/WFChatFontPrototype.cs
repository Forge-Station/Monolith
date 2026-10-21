using Robust.Shared.Prototypes;

namespace Content.Shared._WF.ChatFont;

/// <summary>A font a character can pick for their speech in the character creator.</summary>
[Prototype("wfChatFont")]
public sealed partial class WFChatFontPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>The name shown in the creator's dropdown.</summary>
    [DataField(required: true)]
    public LocId Name;

    /// <summary>The client <c>font</c> prototype the speech is drawn in.</summary>
    [DataField(required: true)]
    public string Font = default!;

    /// <summary>Position in the dropdown, lowest first.</summary>
    [DataField]
    public int Order;
}
