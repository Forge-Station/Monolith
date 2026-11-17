namespace Content.Shared._Forge.Persistence;

/// <summary>
/// Marks a grid as eligible for weekly world persistence.
/// Grids without this (abandoned shuttles, debris, space junk) are omitted from map saves.
/// Stations, POIs, and BSS gates receive it automatically.
/// </summary>
[RegisterComponent]
public sealed partial class WorldPersistableComponent : Component
{
}
