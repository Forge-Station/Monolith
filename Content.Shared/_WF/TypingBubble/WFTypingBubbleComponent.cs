using Content.Shared.Chat.TypingIndicator;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.TypingBubble;

/// <summary>The typing bubble a character picked in their profile; drawn in place of the species' own.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WFTypingBubbleComponent : Component
{
    [DataField, AutoNetworkedField]
    public ProtoId<TypingIndicatorPrototype> Indicator = SharedTypingIndicatorSystem.InitialIndicatorId;
}
