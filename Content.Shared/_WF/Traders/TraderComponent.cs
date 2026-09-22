using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Traders;

/// <summary>
/// A static NPC that serves one customer at a time through a dialogue menu.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class TraderComponent : Component
{
    /// <summary>
    /// Container holding items a customer handed over when the trader has no table.
    /// </summary>
    public const string OfferContainerId = "trader-offer";

    /// <summary>
    /// Container holding items the trader has taken off the customer for the duration of a service.
    /// </summary>
    public const string HoldContainerId = "trader-hold";

    /// <summary>
    /// Dialogue tree this trader offers.
    /// </summary>
    [DataField(required: true)]
    public ProtoId<TraderDialoguePrototype> Dialogue;

    /// <summary>
    /// Extra reach granted to everyone interacting with the trader while it has a table.
    /// </summary>
    [DataField]
    public float TableReachBonus = 1f;

    /// <summary>
    /// Table the trader trades over, if any. Networked so reach is predicted.
    /// </summary>
    [ViewVariables, AutoNetworkedField]
    public EntityUid? Table;

    /// <summary>
    /// Customer currently being served.
    /// </summary>
    [ViewVariables]
    public EntityUid? Customer;

    /// <summary>
    /// How far a customer may wander before the conversation ends.
    /// </summary>
    [DataField]
    public float MaxCustomerDistance = 3.5f;

    /// <summary>
    /// How long a customer may go without input before the conversation ends.
    /// </summary>
    [DataField]
    public TimeSpan IdleTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Delay between the customer speaking and the trader replying.
    /// </summary>
    [DataField]
    public TimeSpan ReplyDelay = TimeSpan.FromSeconds(0.25);

    /// <summary>
    /// Paper used for receipts.
    /// </summary>
    [DataField]
    public EntProtoId ReceiptPrototype = "Paper";

    /// <summary>
    /// Sprite-only marker spawned on the barter tile.
    /// </summary>
    [DataField]
    public EntProtoId ZoneMarker = "WFTraderBarterZone";

    /// <summary>
    /// Stamp sprite state used on receipts.
    /// </summary>
    [DataField]
    public string StampState = "paper_stamp-generic";

    /// <summary>
    /// Stamp colour used on receipts.
    /// </summary>
    [DataField]
    public Color StampColor = Color.FromHex("#BB3232");

    /// <summary>
    /// Marker entity currently drawn over the barter tile.
    /// </summary>
    [ViewVariables]
    public EntityUid? ZoneMarkerEntity;

    /// <summary>
    /// Items the trader has taken from the customer, wherever they currently sit. They all go back
    /// to the output spot when the conversation ends, so an ID can never be lost inside the NPC.
    /// </summary>
    [ViewVariables]
    public List<EntityUid> Held = new();

    /// <summary>
    /// Line the customer is currently being shown.
    /// </summary>
    [ViewVariables]
    public string CurrentLine = string.Empty;

    /// <summary>
    /// Set while the trader itself is putting the conversation window away, so closing it does not
    /// read as the customer walking off.
    /// </summary>
    [ViewVariables]
    public bool HidingDialogue;

    /// <summary>
    /// Whether the menu is currently showing yes/no instead of the option list.
    /// </summary>
    [ViewVariables]
    public bool Confirming;

    /// <summary>
    /// When the customer last did anything.
    /// </summary>
    [ViewVariables]
    public TimeSpan LastInput;

    /// <summary>
    /// When the queued reply should be spoken, if any.
    /// </summary>
    [ViewVariables]
    public TimeSpan? ReplyAt;

    /// <summary>
    /// Line to speak once <see cref="ReplyAt"/> passes.
    /// </summary>
    [ViewVariables]
    public string? ReplyLine;

    /// <summary>
    /// Action to run once the queued reply has been spoken.
    /// </summary>
    [ViewVariables]
    public TraderAction ReplyAction = TraderAction.None;

    /// <summary>
    /// Argument of the option that queued the reply.
    /// </summary>
    [ViewVariables]
    public string? ReplyArgument;

    /// <summary>
    /// How long a refused option waits for what it asked for before giving up.
    /// </summary>
    [DataField]
    public TimeSpan PendingTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Option the customer picked that was refused for something missing from the barter zone. The
    /// trader runs it by itself the moment that thing turns up.
    /// </summary>
    [ViewVariables]
    public int? PendingOption;

    /// <summary>
    /// When <see cref="PendingOption"/> stops waiting.
    /// </summary>
    [ViewVariables]
    public TimeSpan PendingUntil;
}
