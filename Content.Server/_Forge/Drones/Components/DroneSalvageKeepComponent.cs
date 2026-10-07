namespace Content.Server._Forge.Drones.Components;

/// <summary>
/// Whitelisted salvage from a combat drone: AI core, blackbox loot bag, contract proof, etc.
/// Survives wreck cleanup and does not grid-traverse onto passing ships.
/// </summary>
[RegisterComponent]
public sealed partial class DroneSalvageKeepComponent : Component;
