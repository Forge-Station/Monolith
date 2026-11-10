using JetBrains.Annotations;
using Robust.Shared.GameObjects;

namespace Content.Shared._WF.Vehicles.Core;

/// <summary>Raised on a vehicle, and broadcast, once its interior map has been loaded; server only.</summary>
[ByRefEvent, UsedImplicitly]
public readonly record struct VehicleInteriorLoadedEvent(EntityUid Vehicle, EntityUid MapUid, EntityUid Grid);

/// <summary>Raised on a seat before someone is strapped into it; cancel it to refuse the seat.</summary>
[ByRefEvent, UsedImplicitly]
public record struct VehicleSeatAttemptEvent(EntityUid Occupant, EntityUid? User, bool Popup, bool Cancelled = false);
