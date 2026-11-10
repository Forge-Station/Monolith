namespace Content.Shared._WF.Vehicles.Falling;

/// <summary>Raised on a vehicle after it dropped to the level below and took the landing's damage.</summary>
/// <param name="From">The map it fell from.</param>
/// <param name="To">The map it landed on.</param>
[ByRefEvent]
public readonly record struct VehicleFellEvent(EntityUid From, EntityUid To);
