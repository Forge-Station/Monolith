using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Traders;

/// <summary>
/// A trader's greeting and the menu of things a customer can ask for.
/// </summary>
[Prototype]
public sealed partial class TraderDialoguePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// Line spoken when a conversation starts.
    /// </summary>
    [DataField(required: true)]
    public LocId Greeting;

    /// <summary>
    /// Line spoken when a conversation ends.
    /// </summary>
    [DataField(required: true)]
    public LocId Farewell;

    /// <summary>
    /// Menu entries, in the order they are shown.
    /// </summary>
    [DataField(required: true)]
    public List<TraderDialogueOption> Options = new();
}

/// <summary>
/// One menu entry: what the customer says, what the trader answers, and what happens next.
/// </summary>
[DataDefinition]
public sealed partial class TraderDialogueOption
{
    /// <summary>
    /// Line the customer is made to say when this option is picked.
    /// </summary>
    [DataField(required: true)]
    public LocId Prompt;

    /// <summary>
    /// Line the trader answers with.
    /// </summary>
    [DataField(required: true)]
    public LocId Response;

    /// <summary>
    /// Service run after the trader has answered.
    /// </summary>
    [DataField]
    public TraderAction Action = TraderAction.None;

    /// <summary>
    /// What the customer must have put in the barter zone first. Browsing is always free: this is
    /// for options that hand something of the customer's straight to a service, never for the ones
    /// that only open a catalogue, which take payment at their own checkout.
    /// </summary>
    [DataField]
    public TraderRequirement Requires = TraderRequirement.None;

    /// <summary>
    /// Free-form argument handed to the service. <see cref="TraderAction.BuyShip"/> reads it as the
    /// shipyard console prototype whose listing to front.
    /// </summary>
    [DataField]
    public string? Argument;
}

/// <summary>
/// Service a dialogue option runs.
/// </summary>
public enum TraderAction : byte
{
    None,
    OpenShop,
    Refuel,
    BuyShip,
    SellShip,
    UsedShips,
}

/// <summary>
/// What has to be in the barter zone for an option to be usable.
/// </summary>
public enum TraderRequirement : byte
{
    None,

    /// <summary>
    /// Any cash or any ID card.
    /// </summary>
    Payment,

    /// <summary>
    /// Anything a shipyard console's card slot takes: the customer's own ID card, or a bearer
    /// shipyard voucher belonging to nobody.
    /// </summary>
    Id,

    /// <summary>
    /// An ID card carrying a shuttle deed.
    /// </summary>
    DeedId,
}
