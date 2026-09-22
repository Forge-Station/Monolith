using Content.Shared._WF.Traders;

namespace Content.Server._WF.Traders;

/// <summary>
/// Raised on a trader after it has answered a dialogue option, so a service system can act.
/// Leave it unhandled and the trader apologises instead.
/// </summary>
[ByRefEvent]
public record struct TraderActionEvent(EntityUid Trader, EntityUid Customer, TraderAction Action, string? Argument)
{
    /// <summary>
    /// Set by whichever system ran the service.
    /// </summary>
    public bool Handled = false;
}

/// <summary>
/// Raised on a trader when the customer answers a yes/no question.
/// </summary>
[ByRefEvent]
public record struct TraderConfirmedEvent(EntityUid Trader, EntityUid Customer, bool Accepted);

/// <summary>
/// Raised on a trader once a conversation has ended and everything held has been handed back, so a
/// service can tear down whatever it set up for that customer.
/// </summary>
[ByRefEvent]
public record struct TraderConversationEndedEvent(EntityUid Trader, EntityUid Customer);
