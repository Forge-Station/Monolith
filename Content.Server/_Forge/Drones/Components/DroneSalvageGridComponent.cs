namespace Content.Server._Forge.Drones.Components;

/// <summary>
/// Marks a procedural combat-drone hull. When the grid is shredded or cleaned up,
/// only entities with <see cref="DroneSalvageKeepComponent"/> survive as free-floating salvage;
/// guns, boards, thrusters and other wreck junk are deleted.
/// </summary>
[RegisterComponent]
public sealed partial class DroneSalvageGridComponent : Component;
