namespace Content.Shared._WF.ShipShields;

/// <summary>Allows ship defenses to pass protected fire through a shield before absorption.</summary>
[ByRefEvent]
public record struct WFShipShieldInterceptAttemptEvent(EntityUid Grid, EntityUid Shot, EntityUid? Weapon, EntityUid? Shooter)
{
    /// <summary>Whether the shot must pass without consuming shield capacity or producing impact effects.</summary>
    public bool Cancelled;
}
