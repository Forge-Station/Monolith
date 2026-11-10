using Robust.Shared.Maths;

namespace Content.Shared._WF.Vehicles.Grid;

/// <summary>Raised on a grid vehicle that would cover an empty tile; a handler sets <see cref="Allow"/> to let it.</summary>
/// <param name="Grid">The grid the vehicle is driving on.</param>
/// <param name="Tile">The empty tile, in the grid's tile indices.</param>
/// <param name="Allow">Whether the vehicle may cover the tile.</param>
/// <param name="HandOver">
/// Whether this is the origin having crossed the edge, where allowing takes the vehicle off the grid and onto the map,
/// rather than a footprint probing a tile ahead.
/// </param>
[ByRefEvent]
public record struct VehicleOpenTileAttemptEvent(EntityUid Grid, Vector2i Tile, bool Allow = false, bool HandOver = false);
