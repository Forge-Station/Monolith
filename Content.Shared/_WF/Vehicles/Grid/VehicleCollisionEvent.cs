namespace Content.Shared._WF.Vehicles.Grid;

/// <summary>What a <see cref="VehicleCollisionEvent"/> reports.</summary>
public enum VehicleCollisionKind : byte
{
    /// <summary>The vehicle ran into something it cannot pass; wheels take damage from this.</summary>
    Impact,

    /// <summary>A drive step was blocked and the vehicle stopped; the cabin shakes from this.</summary>
    Crash,
}

/// <summary>Raised on a grid vehicle on the server when it hits something hard; wheels and the crash effect listen.</summary>
/// <param name="Kind">Whether this is a single impact or the end of a blocked drive step.</param>
/// <param name="Other">The entity that was hit, when there is one.</param>
/// <param name="Speed">The signed speed in tiles per second just before the stop.</param>
/// <param name="MaxSpeed">The vehicle's current top speed with modifiers, to scale the effect.</param>
[ByRefEvent]
public readonly record struct VehicleCollisionEvent(VehicleCollisionKind Kind, EntityUid? Other, float Speed, float MaxSpeed);

/// <summary>Raised on someone a vehicle has just run over, on the server.</summary>
[ByRefEvent]
public readonly record struct VehicleRanOverEvent(EntityUid Vehicle, float Speed);
