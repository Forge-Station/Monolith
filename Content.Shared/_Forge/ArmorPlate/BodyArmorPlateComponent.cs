using Robust.Shared.GameStates;

namespace Content.Shared._Mono.ArmorPlate;

/// <summary>
/// Allows an entity to use an ArmorPlateHolder directly on itself
/// instead of requiring the holder to be equipped through Inventory.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class BodyArmorPlateComponent : Component
{
}