namespace Content.Server._WF.Drydock;

/// <summary>
/// Work booked on a berth console for a ship. Held until the ship settles in the berth, or until the booking lapses. Not saved.
/// </summary>
[RegisterComponent, UnsavedComponent]
public sealed partial class WFDrydockBookingComponent : Component
{
    /// <summary>Ship the work is booked for.</summary>
    public EntityUid Ship;

    /// <summary>Actor who booked the work and pays for it.</summary>
    public EntityUid Payer;

    /// <summary>Operator that took the booking.</summary>
    public EntityUid Operator;

    /// <summary>Helm the booking was made from, if relayed.</summary>
    public EntityUid? Helm;

    /// <summary>Upgrade to refit, or null for a repair.</summary>
    public string? Upgrade;

    /// <summary>Time the booking lapses.</summary>
    public TimeSpan Expires;

    /// <summary>Time the ship first held still in the berth, or null while it is moving.</summary>
    public TimeSpan? StillSince;
}
