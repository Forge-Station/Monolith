namespace Content.Shared._WF.Vehicles.Crushing;

/// <summary>Why a vehicle was stopped by something that a different vehicle, or a different door, would have given way.</summary>
public enum VehicleSmashBlockReason : byte
{
    /// <summary>The vehicle's weight class is below what it takes to destroy the target.</summary>
    TooLight,

    /// <summary>The door is powered and the driver cannot open it, and a vehicle only smashes an unpowered door.</summary>
    DoorPowered,
}

/// <summary>Raised by ref on a vehicle on the server when it is stopped by something it could have smashed.</summary>
/// <param name="Target">The entity that stopped the vehicle.</param>
/// <param name="Reason">What would have let the vehicle through.</param>
[ByRefEvent]
public readonly record struct VehicleSmashBlockedEvent(EntityUid Target, VehicleSmashBlockReason Reason);

/// <summary>Raised by ref on an entity a vehicle is about to smash; a handler can lower <see cref="Damage"/>.</summary>
/// <param name="Vehicle">The vehicle that hits the entity.</param>
/// <param name="Damage">The blunt damage the hit deals; the rule's damage on hit to start with.</param>
[ByRefEvent]
public record struct VehicleSmashDamageEvent(EntityUid Vehicle, double Damage);
