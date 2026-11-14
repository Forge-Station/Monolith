using Content.Shared.Radio;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Traders;

/// <summary>
/// Lets a trader buy ships off customers and resell them as-is a few minutes later.
/// </summary>
[RegisterComponent]
public sealed partial class TraderUsedShipsComponent : Component
{
    /// <summary>
    /// Shipyard console prototype whose sale rules - taxes, sale rate, radio channels - this
    /// trader buys under. Copied onto the trader for the duration of a sale.
    /// </summary>
    [DataField]
    public EntProtoId Console = "ComputerShipyard";

    /// <summary>
    /// Fraction added to what the seller was paid to get the resale price.
    /// </summary>
    [DataField]
    public float UsedShipMarkup = 0.05f;

    /// <summary>
    /// How long a ship sits out back before it goes on the lot.
    /// </summary>
    [DataField]
    public TimeSpan RelistDelay = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Radio channel new stock is called out on, the same one the shipyard consoles use.
    /// </summary>
    [DataField]
    public ProtoId<RadioChannelPrototype> RadioChannel = "Traffic";

    /// <summary>
    /// Set while the customer is being asked to accept a sale quote.
    /// </summary>
    [ViewVariables]
    public bool AwaitingConfirmation;

    /// <summary>
    /// Ship the pending quote was made for.
    /// </summary>
    [ViewVariables]
    public EntityUid? QuotedShip;
}
