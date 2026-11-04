using System.Numerics;

namespace Content.Server._WF.Vehicles.Core;

/// <summary>Raised when a moving vehicle lays a pair of tread marks, with where its treads are in its grid's space.</summary>
[ByRefEvent]
public readonly record struct WFVehicleTreadsEvent(EntityUid Vehicle, EntityUid Grid, Vector2 Left, Vector2 Right);
