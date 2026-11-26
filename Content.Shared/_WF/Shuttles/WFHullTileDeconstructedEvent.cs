namespace Content.Shared._WF.Shuttles;

/// <summary>Raised on a grid when a validated construction operation leaves no floor at a location, so it leaves the ship's design.</summary>
[ByRefEvent]
public readonly record struct WFHullTileDeconstructedEvent(Vector2i Index);
