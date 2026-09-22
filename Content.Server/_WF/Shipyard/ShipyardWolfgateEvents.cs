namespace Content.Server._WF.Shipyard;

/// <summary>
/// Wolfgate: raised just before a sold ship is deleted, while the grid is still intact and still
/// carries its deed. Last chance to copy it.
/// </summary>
[ByRefEvent]
public record struct ShipSoldEvent(
    EntityUid Shuttle,
    EntityUid Console,
    EntityUid Station,
    int Appraisal);

/// <summary>
/// Wolfgate: raised on a shipyard console before it sells or unassigns, so a trader hosting the
/// console can refuse.
/// </summary>
[ByRefEvent]
public record struct ShipyardConsoleActionAttemptEvent(EntityUid Actor, ShipyardConsoleAction Action)
{
    /// <summary>
    /// Set to refuse the action.
    /// </summary>
    public bool Cancelled = false;
}

/// <summary>
/// Console buttons a host can refuse.
/// </summary>
public enum ShipyardConsoleAction : byte
{
    Sell,
    UnassignDeed,
}
