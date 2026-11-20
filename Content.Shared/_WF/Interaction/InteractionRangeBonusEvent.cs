namespace Content.Shared._WF.Interaction;

/// <summary>
/// Raised by-ref on an interaction target to let it extend the range of the reach check.
/// Only applied when the check actually has a finite range.
/// </summary>
[ByRefEvent]
public struct InteractionRangeBonusEvent
{
    /// <summary>
    /// The entity trying to reach the target.
    /// </summary>
    public readonly EntityUid User;

    /// <summary>
    /// The entity being reached for.
    /// </summary>
    public readonly EntityUid Target;

    /// <summary>
    /// Extra tiles of reach granted by handlers.
    /// </summary>
    public float Bonus;

    public InteractionRangeBonusEvent(EntityUid user, EntityUid target)
    {
        User = user;
        Target = target;
        Bonus = 0f;
    }
}
